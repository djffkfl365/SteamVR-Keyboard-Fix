# SteamVR Keyboard Fix

> **언어 / Language / 言語**
> [한국어](#한국어) · [English](#english) · [日本語](#日本語)

---

# 한국어

## 개요

Meta Quest 등의 VR 헤드셋을 **SteamVR 경유**로 Windows에 연결하면, 사용자의 동작 없이 **en-US(영어 미국) 키보드 레이아웃이 자동으로 추가**됩니다. 이 레이아웃은 제어판에서 일반적인 방법으로는 삭제되지 않습니다.

**SteamVR Keyboard Fix**는 이 문제를 백그라운드에서 자동으로 감지하고 제거하는 Windows 서비스입니다.

---

## 요구 사항

| 항목 | 내용 |
|------|------|
| OS | Windows 10 64-bit |
| .NET | .NET Framework 4.7.2 이상 (Windows 10에 기본 포함) |
| 권한 | 설치 및 서비스 등록 시 관리자 권한 필요 |

---

## 설치

`Installer.msi`를 실행합니다. UAC(사용자 계정 컨트롤) 창이 뜨면 **예**를 클릭합니다.

설치 중 콘솔 창이 열리고 **현재 로그온된 계정의 Windows 비밀번호 입력 프롬프트**가 표시됩니다. 비밀번호를 입력하면 파일 복사와 서비스 등록이 자동으로 완료됩니다.

> ⚠️ **중요:** 반드시 **SteamVR을 실행하는 사용자 계정의 비밀번호**를 입력해야 합니다.
> 서비스는 해당 계정으로 실행되어야 키보드 레이아웃 설정에 접근할 수 있습니다.
>
> 🔒 입력한 비밀번호는 Windows 서비스 등록(SCM)에만 사용되며, 외부로 전송되거나 저장되지 않습니다.

---

## 제거

제어판 → **프로그램 추가/제거**에서 **SteamVR Keyboard Fix**를 제거합니다.

서비스 해제도 자동으로 함께 처리됩니다.

---

## 동작 확인

서비스가 정상 실행 중인지 확인하려면 **Windows 이벤트 뷰어**를 열어 아래 경로를 확인합니다.

```
이벤트 뷰어 → Windows 로그 → 응용 프로그램 → 원본: SteamVRKeyboardFix
```

SteamVR을 실행한 뒤 약 10초 후에 이벤트 ID **2001** (제거 완료) 또는 **2002** (en-US 없음) 항목이 기록되면 정상입니다.

---

## 자주 묻는 질문

**Q. 키보드 레이아웃이 여전히 추가됩니다.**

SteamVR이 레이아웃을 추가하는 데 시간이 걸리므로, 서비스는 vrserver.exe 감지 후 10초 뒤에 제거를 시도합니다. 이벤트 뷰어에서 오류가 없다면 정상입니다.

**Q. 비밀번호를 바꾼 후 서비스가 시작되지 않습니다.**

비밀번호 변경 후에는 MSI를 제거한 뒤 다시 설치하세요.

---

# English

## Overview

When connecting a VR headset (e.g. Meta Quest) to Windows via **SteamVR**, the **en-US keyboard layout is silently added** without any user interaction. This layout cannot be removed through the Control Panel by normal means.

**SteamVR Keyboard Fix** is a Windows background service that automatically detects and removes this layout every time SteamVR starts.

---

## Requirements

| Item | Detail |
|------|--------|
| OS | Windows 10 64-bit |
| .NET | .NET Framework 4.7.2 or later (included in Windows 10 by default) |
| Permissions | Administrator rights required for installation |

---

## Installation

Run `Installer.msi`. Click **Yes** when the UAC prompt appears.

During installation a console window will open and prompt you for your **Windows password**. Enter it and press Enter — file copy and service registration are handled automatically.

> ⚠️ **Important:** Enter the password of the **user account that runs SteamVR**.
> The service must run as that account to access keyboard layout settings.
>
> 🔒 Your password is used solely to register the Windows service (SCM) and is never transmitted or stored externally.

---

## Uninstallation

Go to Control Panel → **Add or Remove Programs** and remove **SteamVR Keyboard Fix**.

Service unregistration is handled automatically.

---

## Verifying It Works

To confirm the service is running correctly, open **Windows Event Viewer**:

```
Event Viewer → Windows Logs → Application → Source: SteamVRKeyboardFix
```

After launching SteamVR, wait about 10 seconds. You should see event ID **2001** (removal completed) or **2002** (en-US not present). Either means the service is working correctly.

---

## FAQ

**Q. The layout is still being added after installation.**

SteamVR adds the layout gradually as it connects to the HMD. The service waits 10 seconds after detecting vrserver.exe before attempting removal. Check Event Viewer for any error entries.

**Q. The service fails to start after I changed my Windows password.**

Uninstall via Add or Remove Programs, then reinstall using the MSI.

---

# 日本語

## 概要

Meta Questなどの VR ヘッドセットを **SteamVR 経由**で Windows に接続すると、ユーザーの操作なしに **en-US（英語・米国）キーボードレイアウトが自動的に追加**されます。このレイアウトはコントロールパネルから通常の方法では削除できません。

**SteamVR Keyboard Fix** は、この問題をバックグラウンドで自動的に検出・削除する Windows サービスです。

---

## 要件

| 項目 | 内容 |
|------|------|
| OS | Windows 10 64-bit |
| .NET | .NET Framework 4.7.2 以上（Windows 10 に標準搭載） |
| 権限 | インストールおよびサービス登録時に管理者権限が必要 |

---

## インストール

`Installer.msi` を実行します。UAC（ユーザーアカウント制御）のプロンプトが表示されたら **はい** をクリックします。

インストール中にコンソールウィンドウが開き、**現在ログオン中のアカウントの Windows パスワードの入力**を求められます。入力して Enter を押すと、ファイルのコピーとサービスの登録が自動的に完了します。

> ⚠️ **重要:** **SteamVR を実行するユーザーアカウントのパスワード**を入力してください。
> サービスはそのアカウントで実行される必要があります。
>
> 🔒 入力したパスワードは Windows サービス登録（SCM）にのみ使用され、外部への送信や保存は一切行われません。

---

## アンインストール

コントロールパネル → **プログラムの追加と削除** から **SteamVR Keyboard Fix** を削除します。

サービスの登録解除も自動的に処理されます。

---

## 動作確認

**Windows イベントビューアー**を開き、以下のパスを確認します。

```
イベントビューアー → Windows ログ → アプリケーション → ソース: SteamVRKeyboardFix
```

SteamVR 起動後、約 10 秒後にイベント ID **2001**（削除完了）または **2002**（en-US なし）が記録されていれば正常です。

---

## よくある質問

**Q. レイアウトがまだ追加されます。**

SteamVR が HMD に接続しながらレイアウトを追加するため、サービスは vrserver.exe 検出から 10 秒後に削除を試みます。イベントビューアーにエラーがなければ正常動作しています。

**Q. パスワード変更後にサービスが起動しなくなりました。**

プログラムの追加と削除からアンインストールした後、MSI を使って再インストールしてください。

---

---

# 개발자 가이드

---

## 빌드 방법

### 필요 사항

- Visual Studio 2022 / 2026 (또는 .NET SDK)
- .NET Framework 4.7.2 Developer Pack

### 빌드 명령어

```bash
# Release / x64
dotnet build -c Release -p:Platform=x64 SteamVRKeyboardFix.sln
```

출력 경로: `bin\x64\Release\net472\SteamVRKeyboardFix.exe`

### 디버그 모드

동작을 확인할 수 있는 대화형 REPL이 실행됩니다.

```cmd
SteamVRKeyboardFix.exe --debug
```

WMI 감시도 동시에 시작되므로, 실제 SteamVR을 실행하여 이벤트를 수신하는 것도 가능합니다.

| 명령어 | 내용 |
|--------|------|
| `run` | `RemoveEnUsKeyboardLayout()` — 자동 감지 후 제거 |
| `registry` | `IsEnUsInRegistry()` — Preload 레지스트리 확인 |
| `hkllist` | `IsEnUsInHklList()` — 런타임 HKL 목록 확인 |
| `ghost` | `RemoveGhostLayout()` — Case A 강제 실행 |
| `registered` | `RemoveRegisteredLayout()` — Case B 강제 실행 |
| `load` | `LoadKeyboardLayout("00000409", ...)` — ghost 재현 |
| `broadcast` | `BroadcastSettingChange()` — WM_SETTINGCHANGE 전송 |
| `install` | 서비스 등록 |
| `uninstall` | 서비스 삭제 |
| `help` | 명령어 목록 |
| `exit` / `quit` | 종료 |

---

## 동작 원리

### 1. 감지

`Win32_ProcessStartTrace` WMI extrinsic 이벤트를 사용합니다. `WITHIN` 절(폴링) 없이 커널이 직접 이벤트를 push하므로 **대기 중 CPU 사용률은 0%** 입니다.

```
vrserver.exe 시작
    ↓ 커널이 즉시 알림 (폴링 없음)
WMI 이벤트 수신
    ↓ 10초 대기 (SteamVR이 레이아웃을 추가할 때까지)
RemoveEnUsKeyboardLayout() 실행
```

10초 대기는 `SteamVRKeyboardFixService.cs`의 `RemovalDelay` 상수로 변경할 수 있습니다.

### 2. 진단

en-US 레이아웃의 상태를 두 곳에서 확인합니다.

| 확인 위치 | 방법 | 의미 |
|-----------|------|------|
| 레지스트리 | `HKCU\Keyboard Layout\Preload` 에 `00000409` 포함 여부 | OS에 등록됐는지 |
| 런타임 | `GetKeyboardLayoutList()` 에 `0x04090409` 포함 여부 | 현재 세션에 로드됐는지 |

### 3. 제거

진단 결과에 따라 세 가지 케이스로 분기합니다.

**Case A — Ghost 레이아웃**

레지스트리에는 없지만 런타임 HKL 목록에만 존재하는 상태. SteamVR이 `LoadKeyboardLayout`을 직접 호출한 경우에 발생합니다.

```
LoadKeyboardLayout("00000409", KLF_NOTELLSHELL | KLF_REPLACELANG)
    → SteamVR의 핸들을 자신의 핸들로 교체
UnloadKeyboardLayout(hkl)
    → HKL 목록에서 제거
BroadcastSettingChange("intl")
    → Shell에 변경 알림
```

**Case B — 등록된 레이아웃**

`HKCU\Keyboard Layout\Preload`에 `00000409`가 존재하는 상태.

`control.exe intl.cpl`을 XML 응답 파일 경유로 호출하는 "Add → Remove" 트릭을 사용합니다. OS의 Globalization API를 통한 공식 삭제 경로이며, 제어판의 추가/제거 버튼과 동일한 코드 경로입니다.

```xml
<gs:InputLanguageID Action="add"    ID="0409:00000409"/>  <!-- 잠금 해제 -->
<gs:InputLanguageID Action="remove" ID="0409:00000409"/>  <!-- 제거 -->
```

**Case C — 정상 (없음)**

아무 작업도 하지 않습니다.

---

## 감시 대상 프로세스 변경

`SteamVRKeyboardFixService.cs`의 아래 두 곳을 수정합니다.

```csharp
// 1. 감시할 프로세스 이름
private const string TargetProcessName = "vrserver.exe";  // ← 여기를 변경

// 2. WMI 쿼리
private const string WmiQuery =
    "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName = 'vrserver.exe'";
    //                                                          ↑ 여기도 동일하게 변경
```

여러 프로세스를 감시하려면 WMI 쿼리를 수정합니다.

```sql
-- 예: vrserver.exe 또는 vrdashboard.exe
SELECT * FROM Win32_ProcessStartTrace
WHERE ProcessName = 'vrserver.exe' OR ProcessName = 'vrdashboard.exe'
```

---

# For Developers

---

## How to Build

### Requirements

- Visual Studio 2022 / 2026 (or .NET SDK)
- .NET Framework 4.7.2 Developer Pack

### Build Command

```bash
# Release / x64
dotnet build -c Release -p:Platform=x64 SteamVRKeyboardFix.sln
```

Output: `bin\x64\Release\net472\SteamVRKeyboardFix.exe`

### Debug Mode

Launches an interactive REPL for testing without administrator rights.

```cmd
SteamVRKeyboardFix.exe --debug
```

The WMI watcher also starts, so real SteamVR events are captured too.

| Command | Description |
|---------|-------------|
| `run` | `RemoveEnUsKeyboardLayout()` — auto-detect and remove |
| `registry` | `IsEnUsInRegistry()` — check Preload registry |
| `hkllist` | `IsEnUsInHklList()` — check runtime HKL list |
| `ghost` | `RemoveGhostLayout()` — force Case A |
| `registered` | `RemoveRegisteredLayout()` — force Case B |
| `load` | `LoadKeyboardLayout("00000409", ...)` — reproduce ghost |
| `broadcast` | `BroadcastSettingChange()` — send WM_SETTINGCHANGE |
| `install` | Register service |
| `uninstall` | Remove service |
| `help` | Show command list |
| `exit` / `quit` | Exit |

---

## Mechanism

### 1. Detection

Uses the `Win32_ProcessStartTrace` WMI extrinsic event. The kernel pushes the notification directly with no `WITHIN` polling clause — **CPU usage is 0% while idle**.

```
vrserver.exe starts
    ↓ kernel push notification (no polling)
WMI event received
    ↓ 10-second delay (wait for SteamVR to add the layout)
RemoveEnUsKeyboardLayout() executed
```

The 10-second delay can be adjusted via the `RemovalDelay` constant in `SteamVRKeyboardFixService.cs`.

### 2. Diagnosis

The state of the en-US layout is checked in two places.

| Location | Method | Meaning |
|----------|--------|---------|
| Registry | `00000409` in `HKCU\Keyboard Layout\Preload` | Registered in OS |
| Runtime | `0x04090409` in `GetKeyboardLayoutList()` | Loaded in current session |

### 3. Removal

Dispatches to one of three cases based on the diagnosis result.

**Case A — Ghost Layout**

Not in the registry but present in the runtime HKL list. Occurs when SteamVR calls `LoadKeyboardLayout` directly.

```
LoadKeyboardLayout("00000409", KLF_NOTELLSHELL | KLF_REPLACELANG)
    → replace SteamVR's handle with ours
UnloadKeyboardLayout(hkl)
    → evict from HKL list
BroadcastSettingChange("intl")
    → notify shell
```

**Case B — Registered Layout**

`00000409` exists in `HKCU\Keyboard Layout\Preload`.

Uses the "Add → Remove" trick via `control.exe intl.cpl` with an XML answer file. This is the official removal path through the OS Globalization API — the same code path as the Control Panel Add/Remove button.

```xml
<gs:InputLanguageID Action="add"    ID="0409:00000409"/>  <!-- unlock -->
<gs:InputLanguageID Action="remove" ID="0409:00000409"/>  <!-- remove -->
```

**Case C — Absent (Normal)**

No action taken.

---

## Watching a Different Process

Change the following two locations in `SteamVRKeyboardFixService.cs`.

```csharp
// 1. process name to watch
private const string TargetProcessName = "vrserver.exe";  // ← change here

// 2. WMI query
private const string WmiQuery =
    "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName = 'vrserver.exe'";
    //                                                          ↑ same name here
```

To watch multiple processes, modify the WMI query.

```sql
-- Example: vrserver.exe or vrdashboard.exe
SELECT * FROM Win32_ProcessStartTrace
WHERE ProcessName = 'vrserver.exe' OR ProcessName = 'vrdashboard.exe'
```

---

# 開発者向け

---

## ビルド方法

### 必要なもの

- Visual Studio 2019 / 2022（または .NET SDK）
- .NET Framework 4.7.2 Developer Pack

### ビルドコマンド

```bash
# Release / x64
dotnet build -c Release -p:Platform=x64 SteamVRKeyboardFix.sln
```

出力先: `bin\x64\Release\net472\SteamVRKeyboardFix.exe`

### デバッグモード

管理者権限なしでも動作確認できる対話型 REPL が起動します。

```cmd
SteamVRKeyboardFix.exe --debug
```

WMI 監視も同時に起動するため、実際に SteamVR を起動してイベントを受信することも可能です。

| コマンド | 内容 |
|----------|------|
| `run` | `RemoveEnUsKeyboardLayout()` — 自動判定して削除 |
| `registry` | `IsEnUsInRegistry()` — Preload レジストリを確認 |
| `hkllist` | `IsEnUsInHklList()` — ランタイム HKL リストを確認 |
| `ghost` | `RemoveGhostLayout()` — Case A を強制実行 |
| `registered` | `RemoveRegisteredLayout()` — Case B を強制実行 |
| `load` | `LoadKeyboardLayout("00000409", ...)` — ghost 再現用 |
| `broadcast` | `BroadcastSettingChange()` — WM_SETTINGCHANGE 送信 |
| `install` | サービス登録 |
| `uninstall` | サービス削除 |
| `help` | コマンド一覧 |
| `exit` / `quit` | 終了 |

---

## 動作の仕組み

### 1. 検出

`Win32_ProcessStartTrace` WMI extrinsic イベントを使用します。`WITHIN` 句（ポーリング）なしでカーネルが直接イベントを push するため、**待機中の CPU 使用率は 0%** です。

```
vrserver.exe 起動
    ↓ カーネルが即座に通知（ポーリングなし）
WMI イベント受信
    ↓ 10秒待機（SteamVR がレイアウトを追加するのを待つ）
RemoveEnUsKeyboardLayout() 実行
```

10 秒の遅延は `SteamVRKeyboardFixService.cs` の `RemovalDelay` 定数で変更可能です。

### 2. 診断

en-US レイアウトの状態を 2 か所で確認します。

| 確認場所 | 方法 | 意味 |
|----------|------|------|
| レジストリ | `HKCU\Keyboard Layout\Preload` に `00000409` があるか | OS に登録されているか |
| ランタイム | `GetKeyboardLayoutList()` に `0x04090409` があるか | 現在セッションに読み込まれているか |

### 3. 削除

診断結果により 3 つのケースに分岐します。

**Case A — Ghost レイアウト**

レジストリにはないが、ランタイム HKL リストにのみ存在する状態。SteamVR が `LoadKeyboardLayout` を直接呼び出した場合に発生します。

```
LoadKeyboardLayout("00000409", KLF_NOTELLSHELL | KLF_REPLACELANG)
    → SteamVR のハンドルを自分のハンドルに置き換え
UnloadKeyboardLayout(hkl)
    → HKL リストから削除
BroadcastSettingChange("intl")
    → Shell に変更を通知
```

**Case B — 登録済みレイアウト**

`HKCU\Keyboard Layout\Preload` に `00000409` が存在する状態。

`control.exe intl.cpl` を XML アンサーファイル経由で呼び出す "Add → Remove" トリックを使用します。OS の Globalization API を通じた公式な削除経路であり、コントロールパネルの追加/削除ボタンと同じコードパスです。

```xml
<gs:InputLanguageID Action="add"    ID="0409:00000409"/>  <!-- ロック解除 -->
<gs:InputLanguageID Action="remove" ID="0409:00000409"/>  <!-- 削除 -->
```

**Case C — 正常（存在しない）**

何もしません。

---

## 監視対象プロセスの変更

`SteamVRKeyboardFixService.cs` の以下の 2 箇所を変更します。

```csharp
// 1. 監視するプロセス名
private const string TargetProcessName = "vrserver.exe";  // ← ここを変更

// 2. WMI クエリ
private const string WmiQuery =
    "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName = 'vrserver.exe'";
    //                                                          ↑ ここも同じ名前に変更
```

複数のプロセスを監視したい場合は WMI クエリを変更します。

```sql
-- 例: vrserver.exe または vrdashboard.exe
SELECT * FROM Win32_ProcessStartTrace
WHERE ProcessName = 'vrserver.exe' OR ProcessName = 'vrdashboard.exe'
```

---

## イベント ID 一覧 / Event IDs / 이벤트 ID 목록

| ID | 한국어 | English | 日本語 |
|----|--------|---------|--------|
| 1000 | 서비스 시작 | Service starting | サービス起動 |
| 1001 | WMI 감시 시작 | WMI watcher started | WMI 監視開始 |
| 1002 | 서비스 중지 | Service stopping | サービス停止 |
| 2000 | vrserver.exe 감지 | vrserver.exe detected | vrserver.exe 検出 |
| 2001 | 레이아웃 제거 완료 | Layout removal completed | レイアウト削除完了 |
| 2002 | en-US 없음 (정상) | en-US absent (normal) | en-US なし（正常） |
| 2003 | 진단 결과 (Preload / HKL 상태) | Diagnosis result (Preload / HKL state) | 診断結果（Preload / HKL 状態） |
| 2004 | Case A (Ghost) 감지 | Case A (Ghost) detected | Case A (Ghost) 検出 |
| 2005 | Case B (Registered) 감지 | Case B (Registered) detected | Case B (Registered) 検出 |
| 2006 | LoadKeyboardLayout 성공 | LoadKeyboardLayout succeeded | LoadKeyboardLayout 成功 |
| 2018 | GetKeyboardLayoutList 결과 | GetKeyboardLayoutList result | GetKeyboardLayoutList 結果 |
| 2030–2032 | intl.cpl 실행 로그 | intl.cpl execution log | intl.cpl 実行ログ |
| 8001–8004 | 경고 (비치명적) | Warning (non-fatal) | 警告（非致命的） |
| 9000–9003 | 오류 | Error | エラー |

---

## ライセンス / License

MIT License

---

*This program was developed with assistance from Claude (Anthropic).*
