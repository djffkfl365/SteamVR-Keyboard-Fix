using System;
using System.Diagnostics;
using System.IO;
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

        // ─── Win32 SCM P/Invoke ───────────────────────────────────────────────

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint OpenSCManager(
            string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateService(
            nint hSCManager, string lpServiceName, string lpDisplayName,
            uint dwDesiredAccess, uint dwServiceType, uint dwStartType,
            uint dwErrorControl, string lpBinaryPathName,
            string? lpLoadOrderGroup, nint lpdwTagId,
            string? lpDependencies, string? lpServiceStartName, string? lpPassword);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint OpenService(
            nint hSCManager, string lpServiceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DeleteService(nint hService);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(nint hSCObject);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ControlService(
            nint hService, uint dwControl, ref SERVICE_STATUS lpServiceStatus);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool StartService(
            nint hService, uint dwNumServiceArgs, string[]? lpServiceArgVectors);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ChangeServiceConfig2(
            nint hService, uint dwInfoLevel, ref SERVICE_DESCRIPTION lpInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS
        {
            public uint dwServiceType, dwCurrentState, dwControlsAccepted;
            public uint dwWin32ExitCode, dwServiceSpecificExitCode;
            public uint dwCheckPoint, dwWaitHint;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SERVICE_DESCRIPTION
        {
            public string lpDescription;
        }

        private const uint SC_MANAGER_ALL_ACCESS  = 0xF003F;
        private const uint SERVICE_ALL_ACCESS      = 0xF01FF;
        private const uint SERVICE_WIN32_OWN_PROCESS = 0x10;
        private const uint SERVICE_AUTO_START      = 0x02;
        private const uint SERVICE_ERROR_NORMAL    = 0x01;
        private const uint SERVICE_CONTROL_STOP    = 0x01;
        private const uint SERVICE_CONFIG_DESCRIPTION = 0x01;
        private const uint SERVICE_NO_CHANGE       = 0xFFFFFFFF;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LogonUser(
            string lpszUsername,
            string? lpszDomain,
            string? lpszPassword,
            int dwLogonType,
            int dwLogonProvider,
            out nint phToken
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(nint hHandle);

        private const int LOGON32_LOGON_NETWORK = 2;
        private const int LOGON32_PROVIDER_DEFAULT = 0;


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
            }

            string exePath    = Process.GetCurrentProcess().MainModule!.FileName;
            string account    = DetectCurrentDesktopUser();
            string? password = null;

            PromptPasswordInstruction();
            // Ask user for password until it is validated successfully, to avoid installing the service with wrong credentials.
            while (true)
            {
                password = PromptPassword(account);
                try
                {
                    ValidatePassword(account, password);
                    break;
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"[ERROR] {ex.Message}");
                    Console.WriteLine($"[ERROR] Failed to logon {account}. Please try again");
                    Console.WriteLine("        (Please enter your actual Windows 'Password', NOT your PIN.)\n");
                }
            }

            Console.WriteLine($"[INFO] Service executable : {exePath}");
            Console.WriteLine($"[INFO] Service account    : {account}");

            // Ensure event log source exists
            EnsureEventLogSource();

            // Grant SeServiceLogonRight to the account
            GrantLogonAsService(account);

            // Remove existing service if present
            RemoveExistingService();

            // Register the service
            nint scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm == (nint) 0)
                Fail("OpenSCManager", Marshal.GetLastWin32Error());

            try
            {
                nint svc = CreateService(
                    scm, ServiceName, DisplayName,
                    SERVICE_ALL_ACCESS, SERVICE_WIN32_OWN_PROCESS,
                    SERVICE_AUTO_START, SERVICE_ERROR_NORMAL,
                    $"\"{exePath}\"",
                    null, (nint) 0, null,
                    account, password);

                if (svc == (nint) 0)
                    Fail("CreateService", Marshal.GetLastWin32Error());

                try
                {
                    var desc = new SERVICE_DESCRIPTION { lpDescription = $"{Description}\nVersion:  {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}" };
                    ChangeServiceConfig2(svc, SERVICE_CONFIG_DESCRIPTION, ref desc);

                    SetRecoveryActions(svc);

                    bool started = StartService(svc, 0, null);
                    if (!started)
                        Fail("StartService", Marshal.GetLastWin32Error());
                }
                finally { CloseServiceHandle(svc); }
            }
            finally { CloseServiceHandle(scm); }

            Console.WriteLine($"[OK] Service '{ServiceName}' installed and started successfully.");
            Console.WriteLine($"     Account : {account}");
            Console.WriteLine($"     Log     : Event Viewer > Application > {EventLogSource}");
            Console.WriteLine($"Press any key to continue");
            Console.ReadKey(); // Prevent console from closing so log can be read during installation process via installer
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

            RemoveExistingService();

            if (EventLog.SourceExists(EventLogSource))
            {
                EventLog.DeleteEventSource(EventLogSource);
                Console.WriteLine($"[OK] Event log source '{EventLogSource}' removed.");
            }

            Console.WriteLine($"[OK] Service '{ServiceName}' uninstalled.");
            Console.WriteLine($"Press any key to continue");
            Console.ReadKey(); // Prevent console from closing so log can be read during uninstallation process via uninstaller
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static void RemoveExistingService()
        {
            nint scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm == (nint) 0) return;
            try
            {
                nint svc = OpenService(scm, ServiceName, SERVICE_ALL_ACCESS);
                if (svc == (nint) 0) return;
                try
                {
                    var status = new SERVICE_STATUS();
                    ControlService(svc, SERVICE_CONTROL_STOP, ref status); // stop (ignore failure)
                    System.Threading.Thread.Sleep(1500);
                    DeleteService(svc);
                    Console.WriteLine($"[INFO] Existing service '{ServiceName}' removed.");
                }
                finally { CloseServiceHandle(svc); }
            }
            finally { CloseServiceHandle(scm); }
        }

        /// <summary>
        /// Sets failure recovery actions: restart after 60 s for the first two failures,
        /// no action on third and subsequent failures. Reset counter after 24 h.
        /// Uses sc.exe because the ChangeServiceConfig2 FAILURE_ACTIONS struct is complex
        /// to marshal and sc.exe is always available on Windows.
        /// </summary>
        private static void SetRecoveryActions(nint svc)
        {
            // sc.exe opens its own SCM handle; our svc handle must remain open
            // so the caller can call StartService() after this method returns.
            var psi = new ProcessStartInfo("sc.exe",
                $"failure {ServiceName} reset= 86400 actions= restart/60000/restart/60000/\"\"/0")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            // We intentionally ignore errors here — recovery policy is best-effort
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }

        /// <summary>
        /// Grants SeServiceLogonRight to the given account via secedit.
        /// Without this right, sc.exe / CreateService will fail with error 1385
        /// when a regular user account is used as the service logon identity.
        /// </summary>
        private static void GrantLogonAsService(string account)
        {
            Console.WriteLine($"[INFO] Granting SeServiceLogonRight to '{account}'...");

            // Resolve account to SID
            string sid;
            try
            {
                var nt  = new System.Security.Principal.NTAccount(account);
                sid     = nt.Translate(typeof(System.Security.Principal.SecurityIdentifier)).Value;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] Could not resolve SID for '{account}': {ex.Message}");
                Console.Error.WriteLine("       SeServiceLogonRight grant skipped.");
                return;
            }

            string tmpDir    = System.IO.Path.GetTempPath();
            string uid       = Guid.NewGuid().ToString("N");
            string exportInf = Path.Combine(tmpDir, $"svkbfix_exp_{uid}.inf");
            string applyInf  = Path.Combine(tmpDir, $"svkbfix_apl_{uid}.inf");
            string applyDb   = Path.Combine(tmpDir, $"svkbfix_{uid}.sdb");

            try
            {
                RunSecedit($"/export /cfg \"{exportInf}\" /areas USER_RIGHTS /quiet");

                string content = File.ReadAllText(exportInf, System.Text.Encoding.Unicode);

                if (content.Contains($"*{sid}") || content.Contains(account))
                {
                    Console.WriteLine("[SKIP] Account already has SeServiceLogonRight.");
                    return;
                }

                System.Text.RegularExpressions.Match m =
                    System.Text.RegularExpressions.Regex.Match(
                        content, @"(?m)^SeServiceLogonRight\s*=\s*(.+)$");

                if (m.Success)
                    content = content.Replace(m.Value, $"SeServiceLogonRight = {m.Groups[1].Value.Trim()},*{sid}");
                else if (content.Contains("[Privilege Rights]"))
                    content = content.Replace("[Privilege Rights]",
                        $"[Privilege Rights]\r\nSeServiceLogonRight = *{sid}");
                else
                    content += $"\r\n[Privilege Rights]\r\nSeServiceLogonRight = *{sid}\r\n";

                File.WriteAllText(applyInf, content, System.Text.Encoding.Unicode);
                RunSecedit($"/configure /db \"{applyDb}\" /cfg \"{applyInf}\" /areas USER_RIGHTS /quiet");
                Console.WriteLine($"[OK] SeServiceLogonRight granted to '{account}'.");
            }
            finally
            {
                foreach (var f in new[] { exportInf, applyInf, applyDb })
                    try { File.Delete(f); } catch { }
            }
        }

        private static void RunSecedit(string args)
        {
            var psi = new ProcessStartInfo("secedit.exe", args)
            {
                CreateNoWindow = true, UseShellExecute = false
            };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("secedit.exe failed to start.");
            p.WaitForExit(15_000);
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"secedit.exe exited with code {p.ExitCode}.");
        }

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

        private static void PromptPasswordInstruction()
        {
            Console.WriteLine();
            Console.WriteLine("============================================================");
            Console.WriteLine(" [SteamVR Keyboard Fix - Background Service Registration]");
            Console.WriteLine();
            Console.WriteLine(" To automatically detect and remove the en-US keyboard layout,");
            Console.WriteLine(" the background service needs permission to run under your");
            Console.WriteLine(" current Windows account.");
            Console.WriteLine();
            Console.WriteLine(" * Note 1: Please enter your actual Windows 'Password', NOT your PIN.");
            Console.WriteLine(" * Note 2: If your account has no password, simply press [Enter].");
            Console.WriteLine("           Your input will be masked with '*' and used securely only");
            Console.WriteLine("           to register the local service.");
            Console.WriteLine("============================================================");
            Console.WriteLine();
        }

        private static string? PromptPassword(string account)
        {
            const char MaskChar = '*';

            Console.Write($"Password for '{account}': ");
            // Read without echo
            var pwd = new System.Text.StringBuilder();
            ConsoleKeyInfo key;
            
            do
            {
                key = Console.ReadKey(intercept: true);
                if (!char.IsControl(key.KeyChar))
                {
                    pwd.Append(key.KeyChar);
                    Console.Write(MaskChar);
                }
                else if (key.Key == ConsoleKey.Backspace && pwd.Length > 0)
                {
                    pwd.Remove(pwd.Length - 1, 1);
                    Console.Write("\b \b");
                }
            } while (key.Key != ConsoleKey.Enter);
            Console.WriteLine();
            return pwd.ToString();
        }

        private static bool ValidatePassword(string account, string? password)
        {
            // Pass immediately if the password is empty.
            // (Blank passwords can be rejected by the network logon check due to Windows local security policy restrictions.)
            //if (string.IsNullOrEmpty(password)) return true;

            string domain = ".";
            string user = account;

            // Parse the account name, which is typically in the "DOMAIN\User" format.
            int slash = account.IndexOf('\\');
            if (slash >= 0)
            {
                domain = account.Substring(0, slash);
                user = account.Substring(slash + 1);
            }

            // Quickly validate the credentials using the Network Logon type (3).
            bool success = LogonUser(user, domain, password, LOGON32_LOGON_NETWORK, LOGON32_PROVIDER_DEFAULT, out nint token);

            if (success)
            {
                CloseHandle(token); // Close the handle to prevent memory leaks
                return true;
            }
            else
            {
                Fail("Credential validation", Marshal.GetLastWin32Error());
            }
            return false;
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
