using System;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;

namespace SteamVRKeyboardFix
{
    internal static class Program
    {
        private const string DebugFlag = "--debug";
        private static bool DebugMode = false;

        static void Main(string[] args)
        {
            // 이벤트 로그 소스가 없으면 생성 (관리자 권한 필요, 설치 시 수행)
            EnsureEventLogSource();

            if (args.Contains(DebugFlag, StringComparer.Ordinal))
            {
                DebugMode = true;
            }
           else if (Environment.UserInteractive)
            {
                args = args.Append("--debug").ToArray();
                DebugMode = true;
            }

            if (DebugMode)
            {
                // 디버그 목적: 콘솔에서 직접 실행하거나 --debug flag 붙여서 실행한 경우
                Console.WriteLine("[SteamVRKeyboardFix] Running in interactive(debug) mode.");
                Console.WriteLine("Press any key to stop...");
                using var svc = new SteamVRKeyboardFixService();
                svc.TestStart(args);
                Console.ReadKey();
                svc.TestStop();
            }
            else
            {
                // 실제 Windows Service로 실행
                ServiceBase.Run(new SteamVRKeyboardFixService());
            }
        }

        private static void EnsureEventLogSource()
        {
            const string source = "SteamVRKeyboardFix";
            try
            {
                if (!EventLog.SourceExists(source))
                    EventLog.CreateEventSource(source, "Application");
            }
            catch
            {
                // 권한 부족 시 무시 (설치 스크립트에서 별도 처리)
            }
        }
    }
}
