#Requires -RunAsAdministrator
<#
.SYNOPSIS
    SteamVRKeyboardFix Windows Service 설치 스크립트

.DESCRIPTION
    1. 이벤트 로그 소스 등록
    2. "서비스로 로그온(SeServiceLogonRight)" 권한 부여
    3. 서비스 등록 (sc.exe)
    4. 서비스 실행 계정 설정 (현재 로그온 사용자)
    5. 오류 복구 정책 설정 (1분 후 재시작, 최대 3회)
    6. 서비스 시작

.NOTES
    반드시 관리자 권한으로 실행하세요.
    $ExePath 경로를 빌드 결과물 경로로 수정하세요.
#>

param(
    [string]$ExePath = "$PSScriptRoot\bin\x64\Debug\net472\SteamVRKeyboardFix.exe",

    # 서비스를 실행할 사용자 계정 (비워두면 현재 로그온 사용자로 설정)
    [string]$ServiceUser = "",
    [string]$ServicePassword = ""
)

$ServiceName    = "SteamVRKeyboardFix"
$DisplayName    = "SteamVR Keyboard Layout Fix"
$Description    = "SteamVR 연결 시 자동으로 추가되는 en-US 키보드 레이아웃을 제거합니다."
$EventLogSource = "SteamVRKeyboardFix"

# ══════════════════════════════════════════════════════════════════════════════
# LSA P/Invoke 헬퍼: SeServiceLogonRight("서비스로 로그온") 권한 부여
# secpol.msc GUI 없이 프로그래밍 방식으로 처리합니다.
# ══════════════════════════════════════════════════════════════════════════════
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.ComponentModel;

public class LsaWrapper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_UNICODE_STRING {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_OBJECT_ATTRIBUTES {
        public uint   Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint   Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [DllImport("advapi32.dll", SetLastError=true)]
    private static extern uint LsaOpenPolicy(
        ref LSA_UNICODE_STRING SystemName,
        ref LSA_OBJECT_ATTRIBUTES ObjectAttributes,
        uint DesiredAccess, out IntPtr PolicyHandle);

    [DllImport("advapi32.dll", SetLastError=true)]
    private static extern uint LsaAddAccountRights(
        IntPtr PolicyHandle, IntPtr AccountSid,
        LSA_UNICODE_STRING[] UserRights, uint CountOfRights);

    [DllImport("advapi32.dll")]
    private static extern uint LsaClose(IntPtr ObjectHandle);

    [DllImport("advapi32.dll")]
    private static extern uint LsaNtStatusToWinError(uint Status);

    [DllImport("advapi32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
    private static extern bool LookupAccountName(
        string lpSystemName, string lpAccountName,
        IntPtr Sid, ref uint cbSid,
        StringBuilder ReferencedDomainName, ref uint cchReferencedDomainName,
        out int peUse);

    private const uint POLICY_ALL_ACCESS = 0x00F0FFF;

    public static void GrantLogonAsService(string accountName)
    {
        uint sidSize = 0, domainSize = 256;
        int sidUse;
        var domain = new StringBuilder(256);
        LookupAccountName(null, accountName, IntPtr.Zero, ref sidSize, domain, ref domainSize, out sidUse);
        if (sidSize == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "계정을 찾을 수 없습니다: " + accountName);

        IntPtr sid = Marshal.AllocHGlobal((int)sidSize);
        try {
            domainSize = 256; domain = new StringBuilder(256);
            if (!LookupAccountName(null, accountName, sid, ref sidSize, domain, ref domainSize, out sidUse))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "LookupAccountName 실패: " + accountName);

            var sysName = new LSA_UNICODE_STRING();
            var objAttr = new LSA_OBJECT_ATTRIBUTES();
            objAttr.Length = (uint)Marshal.SizeOf(typeof(LSA_OBJECT_ATTRIBUTES));

            IntPtr policy;
            uint st = LsaOpenPolicy(ref sysName, ref objAttr, POLICY_ALL_ACCESS, out policy);
            if (st != 0) throw new Win32Exception((int)LsaNtStatusToWinError(st),
                "LsaOpenPolicy 실패 (0x" + st.ToString("X") + ")");
            try {
                const string right = "SeServiceLogonRight";
                IntPtr buf = Marshal.StringToHGlobalUni(right);
                try {
                    var r = new LSA_UNICODE_STRING {
                        Buffer = buf,
                        Length = (ushort)(right.Length * 2),
                        MaximumLength = (ushort)((right.Length + 1) * 2)
                    };
                    uint ast = LsaAddAccountRights(policy, sid, new[]{r}, 1);
                    if (ast != 0) throw new Win32Exception((int)LsaNtStatusToWinError(ast),
                        "LsaAddAccountRights 실패 (0x" + ast.ToString("X") + ")");
                } finally { Marshal.FreeHGlobal(buf); }
            } finally { LsaClose(policy); }
        } finally { Marshal.FreeHGlobal(sid); }
    }
}
'@ -Language CSharp

# ── 1. 경로 확인 ────────────────────────────────────────────────────────────
if (-not (Test-Path $ExePath)) {
    Write-Error "실행 파일을 찾을 수 없습니다: $ExePath"
    $ExePath = Read-Host "SteamVRKeyboardFix.exe 파일의 경로를 입력하세요"
    if (-not (Test-Path $ExePath)) {
        Write-Error "@실행 파일을 찾을 수 없습니다: $ExePath"
        Write-Error "설치를 취소합니다."
        exit 1
    }
}

# ── 2. 이벤트 로그 소스 등록 ──────────────────────────────────────────────
if (-not [System.Diagnostics.EventLog]::SourceExists($EventLogSource)) {
    [System.Diagnostics.EventLog]::CreateEventSource($EventLogSource, "Application")
    Write-Host "[OK] 이벤트 로그 소스 등록: $EventLogSource"
} else {
    Write-Host "[SKIP] 이벤트 로그 소스 이미 존재: $EventLogSource"
}

# ── 3. 기존 서비스 제거 (재설치 대비) ─────────────────────────────────────
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "[INFO] 기존 서비스 발견 — 중지 후 제거 중..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# ── 4. 서비스 실행 계정 결정 ──────────────────────────────────────────────
#   Set-WinUserLanguageList는 현재 로그온 사용자의 언어 프로필을 수정합니다.
#   서비스가 SYSTEM 계정으로 실행되면 해당 사용자의 세션에 접근하지 못합니다.
#   → 반드시 대상 사용자 계정으로 서비스를 실행해야 합니다.

if ([string]::IsNullOrEmpty($ServiceUser)) {
    $ServiceUser = "$env:COMPUTERNAME\$env:USERNAME"
}

if ([string]::IsNullOrEmpty($ServicePassword)) {
    $secPwd = Read-Host "서비스 계정 '$ServiceUser' 의 비밀번호를 입력하세요" -AsSecureString
    $cred   = New-Object System.Management.Automation.PSCredential($ServiceUser, $secPwd)
    $ServicePassword = $cred.GetNetworkCredential().Password
}

# ── 5. SeServiceLogonRight ("서비스로 로그온") 권한 부여 ───────────────────
Write-Host "[INFO] '$ServiceUser' 에게 'SeServiceLogonRight' 권한 부여 중..."
try {
    [LsaWrapper]::GrantLogonAsService($ServiceUser)
    Write-Host "[OK] SeServiceLogonRight 권한 부여 완료"
}
catch {
    Write-Error "권한 부여 실패: $_"
    Write-Error "수동 처리: secpol.msc → 로컬 정책 → 사용자 권한 할당 → '서비스로 로그온' 에 '$ServiceUser' 추가"
    exit 1
}

# ── 5. 서비스 등록 ────────────────────────────────────────────────────────
Write-Host "`[INFO`] 서비스 등록 중: $ServiceName"
sc.exe create $ServiceName `
    binPath= "`"$ExePath`"" `
    start=   auto `
    obj=     "$ServiceUser" `
    password= "$ServicePassword" `
    DisplayName= "$DisplayName" | Out-Null

if ($LASTEXITCODE -ne 0) {
    Write-Error "sc.exe create 실패 (exit code: $LASTEXITCODE)"
    exit 1
}

# 서비스 설명 설정
sc.exe description $ServiceName "$Description" | Out-Null

# ── 6. 오류 복구 정책 ─────────────────────────────────────────────────────
#   첫 번째 오류: 60초 후 재시작
#   두 번째 오류: 60초 후 재시작
#   세 번째 이후: 재시작 없음 (이벤트 로그 확인 필요)
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/""/0 | Out-Null
Write-Host "[OK] 오류 복구 정책 설정 완료"

# ── 7. 서비스 시작 ────────────────────────────────────────────────────────
Write-Host "[INFO] 서비스 시작 중..."
Start-Service -Name $ServiceName

$svc = Get-Service -Name $ServiceName
if ($svc.Status -eq 'Running') {
    Write-Host "[OK] 서비스가 정상적으로 실행되었습니다."
    Write-Host ""
    Write-Host "설치 완료 요약:"
    Write-Host "  서비스 이름  : $ServiceName"
    Write-Host "  실행 계정    : $ServiceUser"
    Write-Host "  시작 유형    : 자동 (Automatic)"
    Write-Host "  이벤트 로그  : 응용 프로그램 > $EventLogSource"
    Write-Host ""
    Write-Host "※ Group Policy 환경에서는 GP 재적용 시 SeServiceLogonRight 권한이"
    Write-Host "  제거될 수 있습니다. 그 경우 이 스크립트를 다시 실행하세요."
} else {
    Write-Warning "서비스 상태: $($svc.Status) — 이벤트 뷰어를 확인하세요."
    Write-Warning "eventvwr.msc → Windows 로그 → 응용 프로그램 → 소스: SteamVRKeyboardFix"
}
