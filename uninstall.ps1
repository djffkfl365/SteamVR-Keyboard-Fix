#Requires -RunAsAdministrator
<#
.SYNOPSIS
    SteamVRKeyboardFix Windows Service 제거 스크립트
#>

$ServiceName    = "SteamVRKeyboardFix"
$EventLogSource = "SteamVRKeyboardFix"

# 서비스 중지 및 제거
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    Write-Host "[INFO] 서비스 중지 중..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    sc.exe delete $ServiceName | Out-Null
    Write-Host "[OK] 서비스 제거 완료"
} else {
    Write-Host "[SKIP] 서비스가 설치되어 있지 않습니다: $ServiceName"
}

# 이벤트 로그 소스 제거
if ([System.Diagnostics.EventLog]::SourceExists($EventLogSource)) {
    [System.Diagnostics.EventLog]::DeleteEventSource($EventLogSource)
    Write-Host "[OK] 이벤트 로그 소스 제거: $EventLogSource"
}

Write-Host "제거가 완료되었습니다."
