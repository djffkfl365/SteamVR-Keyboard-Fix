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

비밀번호 변경 후에는 프로그램을 제거한 뒤 다시 설치하세요.

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

# 開発者向け / For Developers / 開発者向け

---

## ビルド方法 / How to Build

### 必要なもの

- Visual Studio 2019 / 2022（または .NET SDK）
- .NET Framework 4.7.2 Developer Pack

### ビルドコマンド

```bash
# Release / x64
dotnet build -c Release -p:Platform=x64 SteamVRKeyboardFix.sln
```

出力先: `bin\x64\Release\net472\SteamVRKeyboardFix.exe`

### デバッグモード / Debug Mode

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

## 動作の仕組み / Mechanism

### 1. 検出（Detection）

`Win32_ProcessStartTrace` WMI extrinsic イベントを使用します。`WITHIN` 句（ポーリング）なしでカーネルが直接イベントを push するため、**待機中の CPU 使用率は 0%** です。

```
vrserver.exe 起動
    ↓ カーネルが即座に通知 (ポーリングなし)
WMI イベント受信
    ↓ 10秒待機 (SteamVR がレイアウトを追加するのを待つ)
RemoveEnUsKeyboardLayout() 実行
```

10 秒の遅延は `RemovalDelay` 定数で変更可能です（`SteamVRKeyboardFixService.cs`）。

### 2. 診断（Diagnosis）

en-US レイアウトの状態を 2 か所で確認します。

| 確認場所 | 方法 | 意味 |
|----------|------|------|
| レジストリ | `HKCU\Keyboard Layout\Preload` に `00000409` があるか | OS に登録されているか |
| ランタイム | `GetKeyboardLayoutList()` に `0x04090409` があるか | 現在セッションに読み込まれているか |

### 3. 削除（Removal）

診断結果により 3 つのケースに分岐します。

**Case A — Ghost レイアウト**

レジストリにはないが、ランタイム HKL リストにのみ存在する状態です。SteamVR が `LoadKeyboardLayout` を直接呼び出した場合に発生します。

```
LoadKeyboardLayout("00000409", KLF_NOTELLSHELL | KLF_REPLACELANG)
    → SteamVR のハンドルを自分のハンドルに置き換え
UnloadKeyboardLayout(hkl)
    → HKL リストから削除
BroadcastSettingChange("intl")
    → Shell に変更を通知
```

**Case B — 登録済みレイアウト**

`HKCU\Keyboard Layout\Preload` に `00000409` が存在する状態です。

`control.exe intl.cpl` を XML アンサーファイル経由で呼び出す "Add → Remove" トリックを使用します。これは OS の Globalization API を通じた公式な削除経路であり、コントロールパネルの追加/削除ボタンと同じコードパスを使用します。

```xml
<!-- 渡す XML の内容 -->
<gs:InputLanguageID Action="add"    ID="0409:00000409"/>  ← ロック解除
<gs:InputLanguageID Action="remove" ID="0409:00000409"/>  ← 削除
```

**Case C — 正常（存在しない）**

何もしません。

---

## 監視対象プロセスの変更 / Watching a Different Process

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

## イベント ID 一覧 / Event IDs

| ID | 内容 |
|----|------|
| 1000 | サービス起動 |
| 1001 | WMI 監視開始 |
| 1002 | サービス停止 |
| 2000 | vrserver.exe 検出 |
| 2001 | レイアウト削除完了 |
| 2002 | en-US なし（正常） |
| 2003 | 診断結果（Preload / HKL 状態） |
| 2004 | Case A (Ghost) 検出 |
| 2005 | Case B (Registered) 検出 |
| 2006 | LoadKeyboardLayout 成功 |
| 2018 | GetKeyboardLayoutList 結果 |
| 2030–2032 | intl.cpl 実行ログ |
| 8001–8004 | 警告（非致命的） |
| 9000–9003 | エラー |

---

## ライセンス / License

MIT License

---

*This program was developed with assistance from Claude (Anthropic).*
