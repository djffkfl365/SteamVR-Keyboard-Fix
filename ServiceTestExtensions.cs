using System.ServiceProcess;

namespace SteamVRKeyboardFix
{
    /// <summary>
    /// ServiceBase.OnStart/OnStop은 protected이므로 디버그용 래퍼 메서드를 제공합니다.
    /// 배포 빌드에는 영향이 없습니다.
    /// </summary>
    internal static class ServiceTestExtensions
    {
        public static void TestStart(this SteamVRKeyboardFixService service, string[] args)
        {
            // Reflection을 통해 OnStart 호출
            var method = typeof(SteamVRKeyboardFixService)
                .GetMethod("OnStart",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
            method?.Invoke(service, new object[] { args });
        }

        public static void TestStop(this SteamVRKeyboardFixService service)
        {
            var method = typeof(SteamVRKeyboardFixService)
                .GetMethod("OnStop",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
            method?.Invoke(service, null);
        }
    }
}
