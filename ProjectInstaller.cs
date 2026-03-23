using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace SteamVRKeyboardFix
{
    /// <summary>
    /// InstallUtil.exe 또는 sc.exe 로 서비스를 등록할 때 사용되는 Installer 클래스.
    ///
    /// 설치 명령 (관리자 권한 cmd):
    ///   installutil.exe SteamVRKeyboardFix.exe
    ///
    /// 제거 명령:
    ///   installutil.exe /u SteamVRKeyboardFix.exe
    /// </summary>
    [RunInstaller(true)]
    public class ProjectInstaller : Installer
    {
        private readonly ServiceProcessInstaller _processInstaller;
        private readonly ServiceInstaller _serviceInstaller;

        public ProjectInstaller()
        {
            _processInstaller = new ServiceProcessInstaller
            {
                // ★ 중요: 대상 사용자의 언어 설정을 변경하려면
                //   해당 사용자 계정으로 서비스를 실행해야 합니다.
                //   Account = ServiceAccount.User 로 설정하고
                //   설치 시 사용자 이름/비밀번호를 입력하세요.
                //   (또는 sc.exe config 명령으로 나중에 변경 가능)
                Account = ServiceAccount.User
            };

            _serviceInstaller = new ServiceInstaller
            {
                ServiceName = "SteamVRKeyboardFix",
                DisplayName = "SteamVR Keyboard Layout Fix",
                Description = "SteamVR 연결 시 자동으로 추가되는 en-US 키보드 레이아웃을 제거합니다.",
                StartType = ServiceStartMode.Automatic,

                // 서비스 재시작 정책: 오류 시 1분 후 재시작 (최대 3회)
                // (레지스트리로 직접 설정하거나 sc.exe failure 명령 사용)
            };

            Installers.Add(_processInstaller);
            Installers.Add(_serviceInstaller);
        }
    }
}
