using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace SteamVRKeyboardFix
{
    internal static class Program
    {
        private const string DebugFlag = "--debug";
        private const string InstallFlag   = "--install";
        private const string UninstallFlag = "--uninstall";


        // Win32 API: Find console window and show/hide it for debug mode
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        const int SW_SHOW = 5;


        // Declare Win32 API delegate and P/Invoke for shutdown event handling
        public delegate bool ConsoleCtrlDelegate(int ctrlType);

        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

            // Windows control signal constants
        private const int CTRL_C_EVENT = 0;
        private const int CTRL_BREAK_EVENT = 1;
        private const int CTRL_CLOSE_EVENT = 2;
        private const int CTRL_LOGOFF_EVENT = 5;
        private const int CTRL_SHUTDOWN_EVENT = 6;

            // 🚨 [CRITICAL] Keep the delegate as a static variable!
            // If it's a local variable, the Garbage Collector (GC) might collect it,
            // causing a fatal "Attempted to read or write protected memory" crash during shutdown.
        private static ConsoleCtrlDelegate _ctrlHandler;

        static void Main(string[] args)
        {
            // --install / --uninstall: service registration (requires admin).
            // Handled before EnsureEventLogSource so the event log source is
            // created by ServiceManager.Install() itself.
            if (args.Contains(InstallFlag, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    ServiceManager.Install();
                }
                catch(InvalidOperationException ex)
                {
                    Console.WriteLine($"{ex.Message}");
                    Console.WriteLine("Press any key to continue");
                    Console.ReadKey(); // Prevent console from closing so log can be read during installation process via installer
                    throw ex;
                }
                return;
            }
            if (args.Contains(UninstallFlag, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    ServiceManager.Uninstall();
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"{ex.Message}");
                    Console.WriteLine("Press any key to continue");
                    Console.ReadKey(); // Prevent console from closing so log can be read during uninstallation process via uninstaller
                    throw ex;
                }
                return;
            }
            
            // 이벤트 로그 소스가 없으면 생성 (관리자 권한 필요, 설치 시 수행)
            EnsureEventLogSource();

            if (!args.Contains(DebugFlag, StringComparer.OrdinalIgnoreCase))
            {
                ShowWindow(GetConsoleWindow(), SW_HIDE);
            }
            else
            {
                ShowWindow(GetConsoleWindow(), SW_SHOW);
            }
            RunDebugRepl(args);
        }

        // ── Debug REPL ────────────────────────────────────────────────────────

        private static SteamVRKeyboardFixService? _serviceInstance;
        /// <summary>
        /// Interactive command-line REPL for testing without SteamVR.
        /// The service WMI watcher is also started so real events are captured.
        ///
        /// Commands:
        ///   install      — (Admin) Install as service with current exe file path
        ///   uninstall    — (Admin) Uninstall exisiting service
        ///   run          — RemoveEnUsKeyboardLayout()  (full auto-detect + remove)
        ///   registry     — IsEnUsInRegistry()
        ///   hkllist      — IsEnUsInHklList()
        ///   ghost        — RemoveGhostLayout()  (force Case A path)
        ///   registered   — RemoveRegisteredLayout()  (force Case B path)
        ///   load         — LoadKeyboardLayout("00000409", KLF_NOTELLSHELL | KLF_REPLACELANG)  (Reproduce ghost layout)
        ///   broadcast    — BroadcastSettingChange()
        ///   help         — show this list
        ///   exit / quit  — stop service and exit
        /// </summary>
        private static void RunDebugRepl(string[] args)
        {
            // Register the exit event handler
            _ctrlHandler = new ConsoleCtrlDelegate(CtrlHandlerCallback);
            SetConsoleCtrlHandler(_ctrlHandler, true);

            Console.WriteLine("=== SteamVRKeyboardFix [DEBUG MODE] ===");
            Console.WriteLine($"Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");

            Console.WriteLine();

            using var svc = new SteamVRKeyboardFixService();
            _serviceInstance = svc;
            svc.TestStart(args);
            Console.WriteLine("Type 'help' for commands.");

            while (true)
            {
                Console.Write("> ");
                string? input = Console.ReadLine()?.Trim().ToLowerInvariant();

                if (string.IsNullOrEmpty(input)) continue;

                switch (input)
                {
                    case "run":
                        Console.WriteLine("[REPL] Calling RemoveEnUsKeyboardLayout()...");
                        Console.WriteLine("Executing full auto-detect + remove");
                        svc.RemoveEnUsKeyboardLayout();
                        break;

                    case "registry":
                        bool inReg = svc.IsEnUsInRegistry();
                        Console.WriteLine($"[REPL] IsEnUsInRegistry() = {inReg}");
                        break;

                    case "hkllist":
                        bool inHkl = svc.IsEnUsInHklList(out nint foundHkl);
                        Console.WriteLine($"[REPL] IsEnUsInHklList() = {inHkl} (HKL: 0x{foundHkl:X8})");
                        break;

                    case "ghost":
                        Console.WriteLine("[REPL] Forcing Case A: RemoveGhostLayout()...");
                        if (svc.IsEnUsInHklList(out nint ghostHkl))
                            svc.RemoveGhostLayout(ghostHkl);
                        else
                            Console.WriteLine("[REPL] en-US not found in HKL list. Nothing to unload.");
                        break;

                    case "registered":
                        Console.WriteLine("[REPL] Forcing Case B: RemoveRegisteredLayout()...");
                        svc.RemoveRegisteredLayout();
                        break;

                    case "load":
                        Console.WriteLine("[REPL] Calling LoadKeyboardLayout(\"00000409\", KLF_NOTELLSHELL | KLF_REPLACELANG)...");
                        Console.WriteLine("Reproduce ghost layout");
                        nint loaded = SteamVRKeyboardFixService.LoadKeyboardLayout(
                            "00000409",
                            SteamVRKeyboardFixService.KLF_NOTELLSHELL |
                            SteamVRKeyboardFixService.KLF_REPLACELANG);
                        Console.WriteLine(loaded == (nint) 0
                            ? $"[REPL] LoadKeyboardLayout failed (error {Marshal.GetLastWin32Error()})"
                            : $"[REPL] LoadKeyboardLayout succeeded. HKL = 0x{loaded:X8}");
                        break;

                    case "broadcast":
                        Console.WriteLine("[REPL] Calling BroadcastSettingChange()...");
                        svc.BroadcastSettingChange();
                        break;

                    case "help":
                        PrintHelp();
                        break;

                    case "exit":
                    case "quit":
                        svc.TestStop();
                        Console.WriteLine("Service stopped. Goodbye.");
                        return;

                    case "install":
                        try
                        {
                            ServiceManager.Install();
                        }
                        catch(InvalidOperationException ex)
                        {
                            Console.WriteLine($"{ex.Message}");
                        }
                        break;

                    case "uninstall":
                        try
                        {
                            ServiceManager.Uninstall();
                        }
                        catch (InvalidOperationException ex)
                        {
                            Console.WriteLine($"{ex.Message}");
                        }
                        break;

                    default:
                        Console.WriteLine($"Unknown command: '{input}'. Type 'help' for a list.");
                        break;
                }

                Console.WriteLine();
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine();
            Console.WriteLine("  run          RemoveEnUsKeyboardLayout()  — auto-detect and remove");
            Console.WriteLine("  registry     IsEnUsInRegistry()           — check Preload registry key");
            Console.WriteLine("  hkllist      IsEnUsInHklList()            — check runtime HKL list");
            Console.WriteLine("  ghost        RemoveGhostLayout()          — force Case A removal path");
            Console.WriteLine("  registered   RemoveRegisteredLayout()     — force Case B removal path");
            Console.WriteLine("  load         LoadKeyboardLayout(\"00000409\", KLF_NOTELLSHELL|KLF_REPLACELANG)");
            Console.WriteLine("  broadcast    BroadcastSettingChange()     — send WM_SETTINGCHANGE(intl)");
            Console.WriteLine("  help         Show this help");
            Console.WriteLine("  exit/quit    Stop service and exit");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void EnsureEventLogSource()
        {
            const string source = "SteamVRKeyboardFix";
            try
            {
                if (!System.Diagnostics.EventLog.SourceExists(source))
                    System.Diagnostics.EventLog.CreateEventSource(source, "Application");
            }
            catch { /* requires admin rights; handled by installer */ }
        }

        private static bool CtrlHandlerCallback(int ctrlType)
        {
            switch (ctrlType)
            {
                case CTRL_C_EVENT:
                case CTRL_CLOSE_EVENT:
                case CTRL_LOGOFF_EVENT:
                case CTRL_SHUTDOWN_EVENT: // System shutdown signal!

                    // e.g., Unsubscribe from WMI events, close open FileStreams, etc.
                    Console.WriteLine($"\n[INFO] Received Windows exit signal ({ctrlType})! Cleaning up resources gracefully...");

                    _serviceInstance?.TestStop();

                    // Tell Windows: "I've handled the cleanup, proceed with termination!"
                    return true;

                default:
                    return false;
            }
        }
    }
}
