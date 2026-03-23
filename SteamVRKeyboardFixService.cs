using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SteamVRKeyboardFix
{
    /// <summary>
    /// Windows Service that monitors for SteamVR (vrserver.exe) process creation
    /// and automatically removes the en-US keyboard layout that SteamVR adds.
    ///
    /// Detection mechanism:
    ///   - WMI extrinsic event: Win32_ProcessStartTrace
    ///   - No WITHIN clause → no polling, kernel-level push event (CPU ≈ 0% when idle)
    ///
    /// Removal mechanism (pure C#, no PowerShell):
    ///   Two distinct cases handled separately:
    ///
    ///   [Case A] Ghost layout — en-US is NOT in the registry at all, but IS present
    ///            in the runtime HKL list returned by GetKeyboardLayoutList().
    ///            The OS loaded it transiently (e.g. via SteamVR / LoadKeyboardLayout).
    ///            Fix: UnloadKeyboardLayout(hkl) to evict it from the runtime list.
    ///            No registry changes needed.
    ///
    ///   [Case B] Registered layout — en-US IS in the registry
    ///            (HKCU\Control Panel\International\User Profile\Languages).
    ///            Fix: Remove the InputMethodTip value, delete the en-US subkey,
    ///            remove en-US from the Languages multi-string, then broadcast
    ///            WM_SETTINGCHANGE so the shell reloads the language list.
    ///
    ///   [Case C] Truly absent — en-US is neither in the registry nor in the
    ///            runtime HKL list. Nothing to do.
    ///
    /// Ghost vs. absent distinction:
    ///   Both have no registry entry. The difference is in the Win32 runtime:
    ///     GetKeyboardLayoutList() returns HKL 0x04090409 for en-US when it is
    ///     ghost-present, and does not return it when it is truly absent.
    /// </summary>
    public partial class SteamVRKeyboardFixService : ServiceBase
    {
        // ─── Constants ────────────────────────────────────────────────────────

        private const string TargetProcessName = "vrserver.exe";

        private const string WmiQuery =
            "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName = 'vrserver.exe'";

        private const string UserProfileKeyPath =
            @"Control Panel\International\User Profile";

        private const string EnUsTag = "en-US";
        private const string EnUsTip = "0409:00000409";

        // HKL value for en-US United States layout:
        //   low  word = LANGID  0x0409 (English United States)
        //   high word = KLID    0x0409 (United States keyboard)
        // → packed as nint: 0x04090409
        private static readonly nint EnUsHkl = (nint)0x04090409;

        private static readonly TimeSpan RemovalDelay = TimeSpan.FromSeconds(10);

        // ─── Win32 P/Invoke ───────────────────────────────────────────────────

        // Returns the number of loaded HKLs in the system (all sessions, Windows 8+)
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetKeyboardLayoutList(int nBuff, [Out] nint[] lpList);

        // Unloads a keyboard layout from the system (Windows 8+: system-wide)
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnloadKeyboardLayout(nint hkl);

        // Loads a keyboard layout — used for the "add then unload" unlock trick
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint LoadKeyboardLayout(string pwszKLID, uint Flags);

        private const uint KLF_ACTIVATE      = 0x00000001;
        private const uint KLF_NOTELLSHELL   = 0x00000080; // suppress shell notification during load
        private const uint KLF_REPLACELANG   = 0x00000010;

        // WM_SETTINGCHANGE broadcast — notifies Explorer/CTF to reload language list
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const nint HWND_BROADCAST   = 0xFFFF;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint SendMessageTimeout(
            nint   hWnd,
            uint   Msg,
            nint   wParam,
            string lParam,
            uint   fuFlags,
            uint   uTimeout,
            out nint lpdwResult);

        // ─── Fields ───────────────────────────────────────────────────────────

        private ManagementEventWatcher?          _watcher;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly EventLog                _log;
        private bool                             _debugMode;

        // ─── Constructor ──────────────────────────────────────────────────────

        public SteamVRKeyboardFixService()
        {
            ServiceName         = "SteamVRKeyboardFix";
            CanStop             = true;
            CanPauseAndContinue = false;
            AutoLog             = false;
            _log = new EventLog("Application") { Source = "SteamVRKeyboardFix" };
        }

        // ─── Service lifecycle ────────────────────────────────────────────────

        protected override void OnStart(string[] args)
        {
            _debugMode = args.Contains("--debug", StringComparer.OrdinalIgnoreCase);
            Log("SteamVRKeyboardFix service starting.", EventLogEntryType.Information, 1000);
            try
            {
                StartWmiWatcher();
                Log($"Listening for '{TargetProcessName}' via Win32_ProcessStartTrace (no polling).",
                    EventLogEntryType.Information, 1001);
            }
            catch (Exception ex)
            {
                Log($"Failed to start WMI watcher: {ex}", EventLogEntryType.Error, 9000);
                Stop();
            }
        }

        protected override void OnStop()
        {
            Log("SteamVRKeyboardFix service stopping.", EventLogEntryType.Information, 1002);
            _cts.Cancel();
            StopWmiWatcher();
        }

        // ─── WMI watcher ─────────────────────────────────────────────────────

        private void StartWmiWatcher()
        {
            // root\cimv2 네임스페이스, Win32_ProcessStartTrace는 extrinsic event이므로
            // WITHIN 절 불필요 → 폴링 없음
            var scope = new ManagementScope(@"\\.\root\cimv2");
            var query = new EventQuery(WmiQuery);
            _watcher  = new ManagementEventWatcher(scope, query);
            _watcher.EventArrived += OnVrServerStarted;
            _watcher.Start();
        }

        private void StopWmiWatcher()
        {
            if (_watcher == null) return;
            try
            {
                _watcher.Stop();
                _watcher.EventArrived -= OnVrServerStarted;
                _watcher.Dispose();
            }
            catch (Exception ex)
            {
                Log($"Error stopping WMI watcher: {ex.Message}", EventLogEntryType.Warning, 8000);
            }
            finally { _watcher = null; }
        }

        // ─── WMI event handler ────────────────────────────────────────────────

        private void OnVrServerStarted(object sender, EventArrivedEventArgs e)
        {
            uint pid = 0;
            try { pid = (uint)e.NewEvent.Properties["ProcessID"].Value; } catch { /* PID 추출 실패는 무시 */ }

            Log($"vrserver.exe detected (PID={pid}). Scheduling cleanup in {RemovalDelay.TotalSeconds}s.",
                EventLogEntryType.Information, 2000);

            _ = Task.Run(() => DelayedCleanupAsync(_cts.Token));
        }

        private async Task DelayedCleanupAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(RemovalDelay, ct);
                RemoveEnUsKeyboardLayout();
            }
            catch (OperationCanceledException) { /* 서비스 중지 시 정상 취소 */ }
            catch (Exception ex)
            {
                Log($"Unexpected error during cleanup: {ex}", EventLogEntryType.Error, 9001);
            }
        }

        // ─── Keyboard layout removal ──────────────────────────────────────────

        private void RemoveEnUsKeyboardLayout()
        {
            try
            {
                // ── Step 1: 레지스트리에서 en-US 등록 여부 확인 ───────────────
                bool inRegistry = IsEnUsInRegistry();

                // ── Step 2: 런타임 HKL 목록에서 en-US 존재 여부 확인 ──────────
                bool inHklList = IsEnUsInHklList(out nint enUsHkl);

                Log($"Diagnosis — registry: {inRegistry}, runtime HKL list: {inHklList}",
                    EventLogEntryType.Information, 2003);

                if (!inRegistry && !inHklList)
                {
                    // Case C: 진짜로 없는 상태 — 아무 작업 없음
                    Log("en-US is truly absent. Nothing to do.",
                        EventLogEntryType.Information, 2002);
                    return;
                }

                if (!inRegistry && inHklList)
                {
                    // Case A: Ghost layout — 레지스트리에 없지만 런타임에 있음
                    Log("Ghost layout detected (runtime HKL present, no registry entry). " +
                        "Unloading via UnloadKeyboardLayout...",
                        EventLogEntryType.Information, 2004);

                    RemoveGhostLayout(enUsHkl);
                    return;
                }

                // Case B: 레지스트리에 정상 등록된 레이아웃 제거
                // (inRegistry == true인 경우. inHklList 여부와 무관하게 처리)
                Log("Registered layout detected (present in registry). Removing via registry...",
                    EventLogEntryType.Information, 2005);

                RemoveRegisteredLayout();
            }
            catch (Exception ex)
            {
                Log($"RemoveEnUsKeyboardLayout failed: {ex}", EventLogEntryType.Error, 9002);
            }
        }

        // ── Case A: Ghost layout 제거 ─────────────────────────────────────────
        //
        // Ghost는 레지스트리 항목 없이 OS가 런타임에만 로드한 상태입니다.
        // UnloadKeyboardLayout으로 HKL을 직접 언로드하면 즉시 제거됩니다.
        //
        // 단, SteamVR이 LoadKeyboardLayout(KLF_ACTIVATE)로 로드한 경우
        // 해당 HKL은 활성화(activate)된 상태이므로, UnloadKeyboardLayout 전에
        // 동일 레이아웃을 다시 LoadKeyboardLayout으로 로드하여 레퍼런스 카운트를
        // 초기화한 뒤 Unload하는 "load → unload" 패턴이 더 안전합니다.
        //
        private void RemoveGhostLayout(nint hkl)
        {
            // KLF_NOTELLSHELL: load 시 shell에 알림 억제 (불필요한 UI 깜빡임 방지)
            nint loadedHkl = LoadKeyboardLayout("00000409", KLF_NOTELLSHELL | KLF_REPLACELANG);
            if (loadedHkl == (nint)0)
            {
                Log($"LoadKeyboardLayout failed (error {Marshal.GetLastWin32Error()}). " +
                    "Attempting direct unload without reload.",
                    EventLogEntryType.Warning, 8001);
                // reload 실패해도 unload 시도
                loadedHkl = hkl;
            }
            else
            {
                Log("LoadKeyboardLayout succeeded (unlock step).", EventLogEntryType.Information, 2006);
            }

            bool unloaded = UnloadKeyboardLayout(loadedHkl);
            if (unloaded)
            {
                Log("Ghost en-US layout unloaded successfully via UnloadKeyboardLayout.",
                    EventLogEntryType.Information, 2001);
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                Log($"UnloadKeyboardLayout failed (Win32 error {err}). " +
                    "The layout may have been removed by another process, or requires elevated rights.",
                    EventLogEntryType.Warning, 8002);
            }
        }

        // ── Case B: 레지스트리에 등록된 레이아웃 제거 ────────────────────────
        //
        // Set-WinUserLanguageList 내부 동작을 레지스트리로 직접 재현합니다.
        //
        // 레지스트리 구조:
        //   HKCU\Control Panel\International\User Profile\
        //     Languages  REG_MULTI_SZ  {"ko-KR", "en-US", ...}
        //     en-US\
        //       "0409:00000409"  REG_DWORD  1
        //
        // #TODO: Incomplete removal
        private void RemoveRegisteredLayout()
        {
            using var profileKey = Registry.CurrentUser.OpenSubKey(UserProfileKeyPath, writable: true)
                ?? throw new InvalidOperationException($"Registry key not found: {UserProfileKeyPath}");

            // en-US 서브키 아래 tip 값 삭제
            using (var enUsKey = profileKey.OpenSubKey(EnUsTag, writable: true))
            {
                if (enUsKey != null)
                {
                    enUsKey.DeleteValue(EnUsTip, throwOnMissingValue: false);
                    Log($"Tip value '{EnUsTip}' deleted.", EventLogEntryType.Information, 2006);
                }
            }

            // 서브키에 다른 tip 값이 없으면 서브키 자체 삭제
            bool otherTipsRemain;
            using (var checkKey = profileKey.OpenSubKey(EnUsTag))
            {
                otherTipsRemain = checkKey != null && checkKey.GetValueNames().Length > 0;
            }

            if (!otherTipsRemain)
            {
                profileKey.DeleteSubKey(EnUsTag, throwOnMissingSubKey: false);
                Log("en-US subkey deleted.", EventLogEntryType.Information, 2007);
            }

            // Languages 목록에서 en-US 제거 (주 언어가 en-US인 경우 제외)
            var languages = (profileKey.GetValue("Languages") as string[]) ?? Array.Empty<string>();
            if (languages.Length > 0 &&
                !languages[0].Equals(EnUsTag, StringComparison.OrdinalIgnoreCase))
            {
                var newLanguages = languages
                    .Where(l => !l.Equals(EnUsTag, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                profileKey.SetValue("Languages", newLanguages, RegistryValueKind.MultiString);
                Log("en-US removed from Languages list.", EventLogEntryType.Information, 2008);
            }

            // Shell에 변경 알림 브로드캐스트 (Set-WinUserLanguageList와 동일)
            BroadcastSettingChange();

            Log("Registered en-US layout removed successfully.", EventLogEntryType.Information, 2001);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// HKCU\...\User Profile\Languages 다중값에 en-US가 포함되어 있는지 확인합니다.
        /// </summary>
        private static bool IsEnUsInRegistry()
        {
            using var profileKey = Registry.CurrentUser.OpenSubKey(UserProfileKeyPath, writable: false);
            if (profileKey == null) return false;

            var languages = profileKey.GetValue("Languages") as string[];
            return languages?.Contains(EnUsTag, StringComparer.OrdinalIgnoreCase) ?? false;
        }

        /// <summary>
        /// Win32 GetKeyboardLayoutList()로 런타임 HKL 목록을 조회하여
        /// en-US (HKL 0x04090409)의 존재 여부와 실제 HKL 핸들을 반환합니다.
        ///
        /// Ghost layout은 레지스트리에 흔적이 없지만 이 목록에는 나타납니다.
        /// Absent layout은 이 목록에도 나타나지 않습니다.
        /// </summary>
        private bool IsEnUsInHklList(out nint foundHkl)
        {
            foundHkl = (nint)0;

            // 먼저 count만 조회
            int count = GetKeyboardLayoutList(0, Array.Empty<nint>());
            if (count <= 0) return false;

            var hkls = new nint[count];
            int filled = GetKeyboardLayoutList(count, hkls);

            Log($"GetKeyboardLayoutList returned {filled} HKL(s): " +
                string.Join(", ", hkls.Take(filled).Select(h => $"0x{h:X8}")),
                EventLogEntryType.Information, 2011);

            // HKL 매칭:
            //   low  word (LANGID) = 0x0409  → English United States
            //   high word (device) = 0x0409  → United States keyboard
            // SteamVR가 LoadKeyboardLayout("00000409") 으로 로드하면 정확히 이 값이 됩니다.
            // 다른 en-US 변형(Dvorak 등)은 high word가 다르므로 별도 처리가 필요하지 않습니다.
            for (int i = 0; i < filled; i++)
            {
                if (hkls[i] == EnUsHkl)
                {
                    foundHkl = hkls[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// WM_SETTINGCHANGE("intl")를 브로드캐스트하여 Explorer/CTF Loader가
        /// 언어 목록을 재로드하도록 알립니다.
        /// Set-WinUserLanguageList cmdlet 내부와 동일한 동작입니다.
        /// </summary>
        private void BroadcastSettingChange()
        {
            try
            {
                SendMessageTimeout(
                    HWND_BROADCAST, WM_SETTINGCHANGE, (nint)0,
                    "intl", SMTO_ABORTIFHUNG, 2000, out _);
                Log("WM_SETTINGCHANGE('intl') broadcast sent.", EventLogEntryType.Information, 2010);
            }
            catch (Exception ex)
            {
                Log($"BroadcastSettingChange failed (non-fatal): {ex.Message}",
                    EventLogEntryType.Warning, 8004);
            }
        }

        // ─── Logging ──────────────────────────────────────────────────────────

        private void Log(string message,
                         EventLogEntryType type    = EventLogEntryType.Information,
                         int               eventId = 1000)
        {
            if (_debugMode)
                Console.WriteLine($"[{type}] ({eventId}) {message}");
            else
                _log.WriteEntry(message, type, eventId);
        }
    }
}
