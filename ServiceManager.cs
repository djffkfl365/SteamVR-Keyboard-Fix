using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace SteamVRKeyboardFix
{
    /// <summary>
    /// Handles service installation and uninstallation without PowerShell or InstallUtil.
    /// Uses the Win32 Service Control Manager (SCM) API directly via P/Invoke.
    ///
    /// Usage:
    ///   SteamVRKeyboardFix.exe --install    (requires administrator)
    ///   SteamVRKeyboardFix.exe --uninstall  (requires administrator)
    /// </summary>
    internal static class ServiceManager
    {
        private const string ServiceName    = "SteamVRKeyboardFix";
        private const string DisplayName    = "SteamVR Keyboard Layout Fix";
        private const string Description    = "Removes the en-US keyboard layout automatically added by SteamVR.";
        private const string EventLogSource = "SteamVRKeyboardFix";

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Installs and starts the service under the current user account.
        /// Automatically grants SeServiceLogonRight to the account via secedit.
        /// Requires administrator privileges.
        /// </summary>
        public static void Install()
        {
            if (!IsAdministrator())
            {
                Console.Error.WriteLine("ERROR: Administrator privileges are required for installation.");
                Console.Error.WriteLine("       Right-click the executable and choose 'Run as administrator'.");
                Console.Error.WriteLine("Press any key to exit");
                Console.ReadKey();
                return;
            }

            // Ensure event log source exists
            EnsureEventLogSource();

            string exePath    = Process.GetCurrentProcess().MainModule!.FileName;
            string account    = DetectCurrentDesktopUser();

            // 🌟 Building a robust PowerShell command to configure all specific options
            // 1. -Action: What to execute
            // 2. -Trigger: Run at logon for the specific user
            // 3. -Settings: 
            //    - AllowStartIfOnBatteries, -DontStopIfGoingOnBatteries (Power options)
            //    - RestartCount, -RestartInterval (Failure recovery)
            //    - ExecutionTimeLimit 0 (Indefinite execution)
            // 4. -Principal: Run with Highest privileges (UAC bypass) and interactive session (Session 1)

            string psCommand = $@"
                $action = New-ScheduledTaskAction -Execute '{exePath}';
                $trigger = New-ScheduledTaskTrigger -AtLogOn -User '{account}';
                $principal = New-ScheduledTaskPrincipal -UserId '{account}' -LogonType Interactive -RunLevel Highest;
                $settings = New-ScheduledTaskSettingsSet `
                    -AllowStartIfOnBatteries `
                    -DontStopIfGoingOnBatteries `
                    -RestartCount 5 `
                    -RestartInterval (New-TimeSpan -Minutes 1) `
                    -ExecutionTimeLimit 0;
        
                Register-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings -TaskName '{ServiceName}' -Force;
            ";

            // Standardizing the PowerShell script into a single line for ProcessStartInfo
            string compressedCommand = psCommand.Replace("\r\n", " ").Replace("\n", " ");

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{compressedCommand}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using (Process process = Process.Start(psi))
                {
                    process?.WaitForExit();
                    
                    if (process?.ExitCode == 0)
                    {
                        Console.WriteLine(process?.StandardOutput.ReadToEnd());
                        Console.WriteLine($"[OK] Task '{ServiceName}' registered successfully");

                        // Immediate execution after registration
                        Process.Start(new ProcessStartInfo("schtasks.exe", $"/run /tn \"{ServiceName}\"") { CreateNoWindow = true });
                    }
                    else
                    {
                        var error = process?.StandardError.ReadToEnd();
                        if (!string.IsNullOrEmpty(error))
                            Console.WriteLine($"[PS Error Details]\n{error.Trim()}");
                        Console.WriteLine($"[ERROR] PowerShell failed with exit code: {process?.ExitCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL] Failed to register task: {ex.Message}");
            }
            return;
        }

        /// <summary>
        /// Stops and removes the service, and cleans up the event log source.
        /// Requires administrator privileges.
        /// </summary>
        public static void Uninstall()
        {
            if (!IsAdministrator())
            {
                Console.Error.WriteLine("ERROR: Administrator privileges are required for uninstallation.");
                Console.Error.WriteLine("Press any key to exit");
                Console.ReadKey();
            }

            StopScheduledTask();

            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/delete /tn \"{ServiceName}\" /f",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using (Process process = Process.Start(psi))
                {
                    process?.WaitForExit();

                    // We don't necessarily need to fail the whole uninstall 
                    // even if this fails (e.g., if the task was already not running)
                    if (process?.ExitCode == 0)
                    {
                        Console.WriteLine("[OK] Task Scheduler removed via schtasks successfully!");
                    }
                    else
                    {
                        // This often happens if the task is not currently running (Error 0x1)
                        Console.WriteLine($"[NOTE] Task was not removed (Exit Code: {process?.ExitCode}).");
                    }
                }                
            }
            catch (Win32Exception)
            {
                Fail("TaskSchedulerUnregistration", Marshal.GetLastWin32Error());
            }
        }

        private static void StopScheduledTask()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/end /tn \"{ServiceName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                Console.WriteLine($"[INFO] Stopping task instance: {ServiceName}...");

                using (Process process = Process.Start(psi))
                {
                    process?.WaitForExit();

                    // We don't necessarily need to fail the whole uninstall 
                    // even if this fails (e.g., if the task was already not running)
                    if (process?.ExitCode == 0)
                    {
                        Console.WriteLine("[OK] Task instance stopped successfully.");
                    }
                    else
                    {
                        // This often happens if the task is not currently running (Error 0x1)
                        Console.WriteLine($"[NOTE] Task was not running or could not be stopped (Exit Code: {process?.ExitCode}).");
                    }
                }
            }
            catch (Win32Exception)
            {
                Fail("StopScheduledTask", Marshal.GetLastWin32Error());
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static void EnsureEventLogSource()
        {
            if (!EventLog.SourceExists(EventLogSource))
            {
                EventLog.CreateEventSource(EventLogSource, "Application");
                Console.WriteLine($"[OK] Event log source '{EventLogSource}' created.");
            }
        }

        /// <summary>
        /// Detects the currently logged-on desktop user by inspecting the explorer.exe
        /// process owner, which is more reliable than Environment.UserName when
        /// the installer is run elevated via UAC (where UserName returns the admin account).
        /// Falls back to COMPUTERNAME\UserName if explorer.exe cannot be queried.
        /// </summary>
        private static string DetectCurrentDesktopUser()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT * FROM Win32_Process WHERE Name='explorer.exe'");
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    var outParams = obj.InvokeMethod("GetOwner", null, null) as System.Management.ManagementBaseObject;
                    if (outParams != null &&
                        (uint)outParams["ReturnValue"] == 0)
                    {
                        string user   = outParams["User"]?.ToString() ?? "";
                        string domain = outParams["Domain"]?.ToString() ?? Environment.MachineName;
                        return $"{domain}\\{user}";
                    }
                }
            }
            catch { }

            return $"{Environment.MachineName}\\{Environment.UserName}";
        }

        private static bool IsAdministrator()
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void Fail(string operation, int error)
        {
            throw new InvalidOperationException(
                $"{operation} failed with Win32 error {error}: " +
                $"{new System.ComponentModel.Win32Exception(error).Message}");
        }
    }
}
