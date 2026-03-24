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
    ///   [Case B] Registered layout
    ///     Definition : en-US IS in HKCU\Keyboard Layout\Preload (registry entries exist).
    ///     Fix        : "Add → Remove" trick via control.exe + intl.cpl XML automation.
    ///                  intl.cpl invokes the OS Globalization API — the same code path as
    ///                  clicking Add/Remove in the Control Panel — so registry and runtime
    ///                  state are both cleaned atomically without manual registry writes.
    ///
    ///   [Case C] Truly absent(Normal)
    ///     Definition : en-US is neither in registry nor in the runtime HKL list.
    ///     Fix        : Nothing.
    ///
    /// --- Registry layout (three locations) ---
    ///   HKCU\Keyboard Layout\Preload
    ///     "1" = "00000412"  (ko-KR)
    ///     "2" = "00000409"  ← en-US presents
    ///
    ///   HKCU\Keyboard Layout\Substitutes
    ///     (optional; entries mapping locale-id → layout-id)
    ///
    ///   HKCU\Control Panel\International\User Profile
    ///     Languages  REG_MULTI_SZ  {"ko-KR", "en-US", ...}
    ///     en-US\
    ///       "0409:00000409"  REG_DWORD  1
    /// </summary>
    public partial class SteamVRKeyboardFixService : ServiceBase
    {
        // ─── Constants ────────────────────────────────────────────────────────

        private const string TargetProcessName = "vrserver.exe";

        private const string WmiQuery =
            "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName = 'vrserver.exe'";

        // Registry paths (relative to HKCU)
        private const string PreloadKeyPath = @"Keyboard Layout\Preload";

        // en-US identifiers
        private const string EnUsLayoutId = "00000409";       // KLID used in Preload / LoadKeyboardLayout
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
        /// Used in the "load → unload" unlock pattern for both Case A and Case B.
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

            BroadcastSettingChange();
        }

        // ── Case B: Registered layout removal ────────────────────────────────

        /// <summary>
        /// Removes an en-US layout that is registered in the registry.
        ///
        /// Uses the "Add → Remove" trick via control.exe + intl.cpl XML automation,
        /// which is the same code path as clicking Add/Remove in the Control Panel.
        /// This approach delegates all registry writes and WM_SETTINGCHANGE broadcasting
        /// to the OS Globalization API, so no manual registry manipulation is needed.
        ///
        /// Mechanism:
        ///   control.exe invokes intl.cpl with an answer file (XML) that contains:
        ///     <gs:InputLanguageID Action="add"    ID="0409:00000409"/>  — unlock step
        ///     <gs:InputLanguageID Action="remove" ID="0409:00000409"/>  — removal step
        ///   intl.cpl processes both instructions sequentially via the Windows
        ///   Globalization Services API (same as lpksetup / INTL.CPL internals).
        ///
        /// The XML is written to a temporary file, passed to control.exe, then deleted.
        /// control.exe is awaited synchronously with a timeout of <see cref="IntlCplTimeoutMs"/>.
        /// </summary>
        internal void RemoveRegisteredLayout()
        {
            // XML answer file content — identical to the manually used Remove_en-US.xml.
            // Action="add" first: registers the layout properly so the OS accepts the removal.
            // Action="remove" second: removes it cleanly through the OS Globalization API.
            const string xmlContent =
                "<gs:GlobalizationServices xmlns:gs=\"urn:longhornGlobalizationUnattend\">\r\n" +
                "    <gs:UserList>\r\n" +
                "        <gs:User UserID=\"Current\"/>\r\n" +
                "    </gs:UserList>\r\n" +
                "    <gs:InputPreferences>\r\n" +
                "        <gs:InputLanguageID Action=\"add\"    ID=\"0409:00000409\"/>\r\n" +
                "        <gs:InputLanguageID Action=\"remove\" ID=\"0409:00000409\"/>\r\n" +
                "    </gs:InputPreferences>\r\n" +
                "</gs:GlobalizationServices>";

            string tmpXml = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"svkbfix_{Guid.NewGuid():N}.xml");

            try
            {
                System.IO.File.WriteAllText(tmpXml, xmlContent, System.Text.Encoding.UTF8);
                Log($"Temporary XML written to '{tmpXml}'.", EventLogEntryType.Information, 2030);

                // control.exe is a thin launcher; the actual work is done inside intl.cpl.
                // The process must be waited on — intl.cpl is synchronous within control.exe.
                var psi = new ProcessStartInfo
                {
                    FileName        = "control.exe",
                    Arguments       = $"intl.cpl,, /f:\"{tmpXml}\"",
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                };

                Log($"Invoking: control.exe intl.cpl,, /f:\"{tmpXml}\"",
                    EventLogEntryType.Information, 2031);

                using var proc = Process.Start(psi)
                    ?? throw new InvalidOperationException("Failed to start control.exe.");

                bool exited = proc.WaitForExit(IntlCplTimeoutMs);
                if (!exited)
                {
                    proc.Kill();
                    Log($"control.exe did not exit within {IntlCplTimeoutMs} ms — process killed.",
                        EventLogEntryType.Warning, 8006);
                }
                else
                {
                    Log($"control.exe exited (code {proc.ExitCode}).",
                        EventLogEntryType.Information, 2032);
                }
            }
            catch (Exception ex)
            {
                Log($"RemoveRegisteredLayout (intl.cpl) failed: {ex}", EventLogEntryType.Error, 9003);
            }
            finally
            {
                // Always clean up the temporary XML file.
                try { System.IO.File.Delete(tmpXml); } catch { /* best-effort */ }
            }

            Log("Registered en-US layout removal completed.", EventLogEntryType.Information, 2001);
        }

        /// <summary>
        /// Maximum time (ms) to wait for control.exe / intl.cpl to finish.
        /// intl.cpl is normally near-instant; 15 s is a conservative upper bound.
        /// </summary>
        private const int IntlCplTimeoutMs = 15_000;

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
