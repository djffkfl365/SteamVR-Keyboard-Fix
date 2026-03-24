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
    /// Windows Service that watches for SteamVR (vrserver.exe) startup and automatically
    /// removes the en-US keyboard layout that SteamVR silently adds to the system.
    ///
    /// --- Detection mechanism ---
    /// Uses the WMI extrinsic event class Win32_ProcessStartTrace with no WITHIN clause,
    /// so the kernel pushes the notification directly — zero polling, zero CPU when idle.
    ///
    /// --- Removal: two distinct cases ---
    ///   [Case A] Ghost layout
    ///     Definition : en-US is NOT in HKCU\Keyboard Layout\Preload, but IS present
    ///                  in the runtime HKL list returned by GetKeyboardLayoutList().
    ///                  The OS loaded it transiently (LoadKeyboardLayout by SteamVR).
    ///     Fix        : UnloadKeyboardLayout()(hkl) to evict it from the runtime list. — no registry changes needed.
    ///
    ///   [Case B] Registered layout — en-US IS in the registry
    ///            (HKCU\Control Panel\International\User Profile\Languages).
    ///            Fix: Remove the InputMethodTip value, delete the en-US subkey,
    ///            remove en-US from the Languages multi-string, then broadcast
    ///            WM_SETTINGCHANGE so the shell reloads the language list.
    ///
    ///   [Case C] Truly absent(Normal)
    ///     Definition : en-US is neither in registry nor in the runtime HKL list.
    ///     Fix        : Nothing.
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

        // Registry paths (relative to HKCU)
        private const string PreloadKeyPath     = @"Keyboard Layout\Preload";
        private const string SubstitutesKeyPath = @"Keyboard Layout\Substitutes";
        private const string UserProfileKeyPath = @"Control Panel\International\User Profile";

        // en-US identifiers
        private const string EnUsTag      = "en-US";
        private const string EnUsLayoutId = "00000409";       // KLID used in Preload/Substitutes
        private const string EnUsTip      = "0409:00000409";  // InputMethodTip used in User Profile subkey
        private static readonly nint EnUsHkl = (nint)0x04090409; // HKL handle (LANGID | KLID)

        /// <summary>
        /// Time to wait after vrserver.exe is detected before attempting layout removal.
        /// SteamVR writes the registry entries while connecting to HMD;
        /// this delay ensures the entries are present before we read them.
        /// </summary>
        private static readonly TimeSpan RemovalDelay = TimeSpan.FromSeconds(10);

        // ─── Win32 P/Invoke ───────────────────────────────────────────────────
        /// <summary>
        /// Returns the number of loaded HKLs in the system
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetKeyboardLayoutList(int nBuff, [Out] nint[] lpList);

        /// <summary>
        /// Unloads a keyboard layout from the system
        /// </summary>
        /// <param name="hkl">HKL of target layout</param>
        /// <returns></returns>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnloadKeyboardLayout(nint hkl);

        /// <summary>
        /// Loads a keyboard layout.
        /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint LoadKeyboardLayout(string pwszKLID, uint Flags);

        internal const uint KLF_NOTELLSHELL = 0x00000080; // suppress shell notification on load
        internal const uint KLF_REPLACELANG = 0x00000010;

        // WM_SETTINGCHANGE: tells Explorer / CTF Loader to reload the language list
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
            // Win32_ProcessStartTrace is an extrinsic event class — no WITHIN clause needed.
            // The kernel pushes the event immediately on process creation; no polling occurs.
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
            try { pid = (uint)e.NewEvent.Properties["ProcessID"].Value; } catch { /* Ignore PID exfraction failure */ }

            Log($"vrserver.exe detected (PID={pid}). Scheduling cleanup in {RemovalDelay.TotalSeconds}s.",
                EventLogEntryType.Information, 2000);

            // Schedule cleanup on a thread-pool thread so we do not block the WMI callback thread.
            _ = Task.Run(() => DelayedCleanupAsync(_cts.Token));
        }

        private async Task DelayedCleanupAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(RemovalDelay, ct);
                RemoveEnUsKeyboardLayout();
            }
            catch (OperationCanceledException) { /* Service stopped normally */ }
            catch (Exception ex)
            {
                Log($"Unexpected error during cleanup: {ex}", EventLogEntryType.Error, 9001);
            }
        }

        // ── Top-level removal dispatcher ──────────────────────────────────────

        /// <summary>
        /// Diagnoses the current state of the en-US keyboard layout and dispatches
        /// to the appropriate removal path (Case A, B, or C).
        /// </summary>
        internal void RemoveEnUsKeyboardLayout()
        {
            try
            {
                bool inPreload = IsEnUsInPreload(out string? preloadValueName);
                bool inHklList = IsEnUsInHklList(out nint activeHkl);

                Log($"Diagnosis — Preload: {inPreload} (value: '{preloadValueName ?? "n/a"}'), " +
                    $"HKL list: {inHklList} (0x{activeHkl:X8})",
                    EventLogEntryType.Information, 2003);

                if (!inPreload && !inHklList)
                {
                    // Case C: truly absent
                    Log("en-US is truly absent. Nothing to do.", EventLogEntryType.Information, 2002);
                    return;
                }

                if (!inPreload && inHklList)
                {
                    // Case A: ghost layout — runtime only, no registry entries
                    Log("Case A: Ghost layout detected (in HKL list, not in Preload).",
                        EventLogEntryType.Information, 2004);
                    RemoveGhostLayout(activeHkl);
                    return;
                }

                // Case B: registered layout — registry entries exist
                // inPreload == true; inHklList may be true or false
                Log("Case B: Registered layout detected (present in Preload).",
                EventLogEntryType.Information, 2005);
                RemoveRegisteredLayout();
            }
            catch (Exception ex)
            {
                Log($"RemoveEnUsKeyboardLayout failed: {ex}", EventLogEntryType.Error, 9002);
            }
        }

        // ── Case A: Ghost layout removal ──────────────────────────────────────

        /// <summary>
        /// Removes a ghost en-US layout that exists only in the runtime HKL list.
        ///
        /// Uses the "load → unload" pattern:
        ///   1. LoadKeyboardLayout("00000409", KLF_NOTELLSHELL | KLF_REPLACELANG)
        ///      Re-loads the same layout under our own reference, replacing the
        ///      SteamVR-activated handle.  KLF_NOTELLSHELL suppresses shell flicker.
        ///   2. UnloadKeyboardLayout(reloadedHkl)
        ///      Removes our reference.  Because SteamVR's reference was replaced in
        ///      step 1, the layout is fully evicted from the session.
        ///   3. BroadcastSettingChange() — refreshes the shell UI.
        /// </summary>
        internal void RemoveGhostLayout(nint hkl)
        {
            nint reloadedHkl = LoadKeyboardLayout(EnUsLayoutId, KLF_NOTELLSHELL | KLF_REPLACELANG);
            if (reloadedHkl == (nint) 0)
            {
                Log($"LoadKeyboardLayout failed (error {Marshal.GetLastWin32Error()}). " +
                    "Attempting direct unload with original handle.",
                    EventLogEntryType.Warning, 8001);
                reloadedHkl = hkl;
            }
            else
            {
                Log($"LoadKeyboardLayout succeeded (HKL: 0x{reloadedHkl:X8}).",
                    EventLogEntryType.Information, 2006);
            }

            bool unloaded = UnloadKeyboardLayout(reloadedHkl);
            if (unloaded)
                Log("Ghost layout unloaded successfully.", EventLogEntryType.Information, 2001);
            else
                Log($"UnloadKeyboardLayout failed (Win32 error {Marshal.GetLastWin32Error()}). " +
                    "A session restart may be required.",
                    EventLogEntryType.Warning, 8002);
        }

        internal void RemoveRegisteredLayout()
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
                Log($"User Profile registry cleanup failed", EventLogEntryType.Error, 9003);
            }

            // Shell에 변경 알림 브로드캐스트 (Set-WinUserLanguageList와 동일)
            BroadcastSettingChange();

            Log("Registered en-US layout removal completed.", EventLogEntryType.Information, 2001);
        }
        // ── Diagnosis helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Returns true when HKCU\Keyboard Layout\Preload contains a value
        /// whose data equals "00000409" (en-US layout ID).
        /// Sets <paramref name="valueName"/> to the value's name (e.g. "2") when found.
        /// </summary>
        internal bool IsEnUsInPreload(out string? valueName)
        {
            valueName = null;
            using var key = Registry.CurrentUser.OpenSubKey(PreloadKeyPath, writable: false);
            if (key == null) return false;
            foreach (var name in key.GetValueNames())
            {
                if ((key.GetValue(name) as string ?? string.Empty)
                        .Equals(EnUsLayoutId, StringComparison.OrdinalIgnoreCase))
                {
                    valueName = name;
                    return true;
                }
            }
            return false;
        }

        // Overload without out param for debug REPL convenience
        internal bool IsEnUsInRegistry()
        {
            return IsEnUsInPreload(out _);
        }

        /// <summary>
        /// Returns true when the runtime HKL list (GetKeyboardLayoutList) contains
        /// the en-US handle 0x04090409.
        ///
        /// Ghost layouts are NOT in the registry but DO appear here.
        /// Truly absent layouts appear in neither place.
        /// </summary>
        internal bool IsEnUsInHklList(out nint foundHkl)
        {
            foundHkl = (nint)0;

            // 먼저 count만 조회
            int count = GetKeyboardLayoutList(0, Array.Empty<nint>());
            if (count <= 0) return false;

            var hkls = new nint[count];
            int filled = GetKeyboardLayoutList(count, hkls);

            Log($"GetKeyboardLayoutList: [{string.Join(", ", hkls.Take(filled).Select(h => $"0x{h:X8}"))}]",
                EventLogEntryType.Information, 2018);

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
        /// Broadcasts WM_SETTINGCHANGE("intl") to all top-level windows.
        /// Explorer, the language bar, and CTF Loader respond by reloading
        /// the input language list — this is what Set-WinUserLanguageList does internally.
        /// </summary>
        internal void BroadcastSettingChange()
        {
            try
            {
                SendMessageTimeout(
                    HWND_BROADCAST, WM_SETTINGCHANGE, (nint)0,
                    "intl", SMTO_ABORTIFHUNG, 2000, out _);
                Log("WM_SETTINGCHANGE('intl') broadcast sent.", EventLogEntryType.Information, 2019);
            }
            catch (Exception ex)
            {
                Log($"BroadcastSettingChange failed (non-fatal): {ex.Message}",
                    EventLogEntryType.Warning, 8004);
            }
        }

        // ─── Logging ──────────────────────────────────────────────────────────

        internal void Log(string message,
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
