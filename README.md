# Claude 自动审批 / Claude Auto Pass

[![Build APK](https://github.com/tiger-git2022/claude-auto-pass/actions/workflows/android.yml/badge.svg)](https://github.com/tiger-git2022/claude-auto-pass/actions/workflows/android.yml) [![Release](https://img.shields.io/github/v/release/tiger-git2022/claude-auto-pass)](https://github.com/tiger-git2022/claude-auto-pass/releases/latest) [![Build Windows EXE](https://github.com/tiger-git2022/claude-auto-pass/actions/workflows/windows.yml/badge.svg)](https://github.com/tiger-git2022/claude-auto-pass/actions/workflows/windows.yml)

**下载 / Download：** [Android APK](https://github.com/tiger-git2022/claude-auto-pass/releases/latest) · [Windows EXE](https://github.com/tiger-git2022/claude-auto-pass/releases?q=win-v&expanded=true)

## 中文

一个简单的 Android 自用工具，只处理 **Claude 官方 App**（`com.anthropic.claude`）中的审批按钮。

### 功能

- 优先点击“本次会话中始终允许”，没有该选项时点击“允许一次”。支持已列入规则的简体中文、繁体中文和英文按钮文字。
- 通知栏提供“开启 / 关闭”和“退出”；也可在系统快捷设置中添加开关。
- 退出只停止自动审批、移除通知，**保留无障碍权限**；再次打开可恢复。
- Material 3 界面，支持深浅色、动态配色，帮助开启无障碍和通知权限，显示状态及最近一次审批。
- 检查 GitHub 正式版本，展示更新说明，下载 APK 并显示进度。支持后台下载，完成后由用户确认安装。
- 更新安装前校验包名、递增版本号、相同签名，以及 GitHub 提供的 SHA-256（如有）。

### 安装与使用

1. 在 [Releases](https://github.com/tiger-git2022/claude-auto-pass/releases/latest) 下载 `claude-auto-pass.apk` 并安装。需要 Android 9 或更高版本。
2. 打开 App，允许通知，在系统无障碍设置中开启“Claude 自动审批”。首次安装默认关闭，系统重连保留开关状态。
3. 在 App 或通知栏点击“开启”，再进入 Claude 的审批界面。手机需解锁，Claude 需位于前台。
4. 通知栏点击“关闭”暂停；点击“退出”停止并移除通知，无障碍设置保持开启。
5. 在 App 点击“检查更新”；有新版时可查看更新说明、下载并手动安装。安装更新权限仅在安装时引导开启。

APK 侧载后，部分手机需先到应用详情页的菜单中选择“允许受限设置”，才能开启无障碍。不同系统的入口可能不同。

### 范围和隐私

此工具会代你批准 Claude 请求的操作，开启前请确认这符合你的使用意图。只接受明确列出的按钮文字，并要求附近出现另一审批选项；不会按坐标盲点，也不会点击 Android 的系统授权弹窗。未知按钮文字会跳过，需根据实际 Claude 版本调整规则。

运行期间使用带通知的前台服务维持后台运行，退出会停止前台服务，但保留无障碍权限。如果手机仍限制后台运行，可使用“后台运行设置”入口调整自启动和电池管理。权限授权与服务连接状态分别显示。

无障碍在本机读取前台界面的按钮文字，不保存对话、截图或屏幕内容；只保存开关、上次操作和下载状态。不需要 Claude 登录凭据。网络仅用于 GitHub 版本查询和 APK 下载。

### 自动编译与发布

- 推送 `main`、提交 PR 或手动运行 [Android APK](https://github.com/tiger-git2022/claude-auto-pass/actions/workflows/android.yml) 会执行规则测试、编译和 Android lint。
- 参考 EhViewer 仓库：`main` 每次代码更新会自动生成签名 APK 并发布新的 `-tN` 编号版本（如 `1.0.0-t1`、`1.0.0-t2`）。仅修改 Markdown 文档时不触发构建。PR 和其他分支仅编译检查，不使用签名密钥。
- 发布无需手动打标签。工作流自动计算编号、创建标签并发布正式 Release；同一提交重跑默认跳过发布，手动运行时可勾选强制重新发布。升级基础版本时，同时增加 `app/build.gradle.kts` 中的 `baseVersionName` 和 `baseVersionCode`，并更新 `CHANGELOG.md`。
- Release 中 APK 文件名固定为 `claude-auto-pass.apk`，App 从最新正式 Release 获取更新。
- README 和版本说明均先中文、后英文；发布流程会检查版本说明的顺序。
- 签名密钥通过仓库 Actions Secrets `APP_KEYSTORE_BASE64`、`APP_KEYSTORE_PASSWORD` 提供，不提交到源码。后续更新须保持相同密钥，维护者应妥善备份本机密钥。

### Windows 版（Claude Desktop）

`windows/` 目录是给 **Claude Desktop 桌面端** 用的托盘小程序，规则与 Android 版相同，另外认识桌面端的“Allow for this chat / 在此聊天中始终允许”。

1. 在 [Releases](https://github.com/tiger-git2022/claude-auto-pass/releases?q=win-v&expanded=true) 下载最新 `win-v…` 版本里的 `claude-auto-pass.exe`，放到任意文件夹，双击运行。无需安装，Windows 10/11 自带所需的 .NET Framework 4.8。首次运行时 SmartScreen 可能提示“未知发布者”，点“更多信息 → 仍要运行”。
2. 程序只在右下角托盘显示橙色盾牌图标，**不需要保持前台**。左键单击图标开启/关闭（关闭时图标变灰），右键菜单（Fluent 风格，Windows 11 上为亚克力半透明背景）可设置开机自启、检查更新、退出。首次运行默认开启。
3. **Claude 也不需要在前台**：通过 Windows UI 自动化读取 Claude 窗口里的按钮并直接触发，不移动鼠标；Claude 在后台时也能工作；被其他窗口完全挡住或最小化时，Claude 会停止更新页面，需要用「以辅助功能模式重启 Claude」重启一次（挡住就能用）；最小化后新弹出的卡片读不到，最好别最小化。电脑锁屏时暂停。
4. 如果审批框出现了但没有被自动点击，右键托盘图标选“导出按钮文字（诊断）”，会打开一个文本，列出程序看到的 Claude 按钮文字和分类，按它调整规则即可。若诊断显示“元素很少”，选“以辅助功能模式重启 Claude”：会关闭 Claude 并加上 `--force-renderer-accessibility --disable-features=CalculateNativeWinOcclusion` 重新打开，让 Chromium 生成按钮信息，并在被挡住时继续更新。
5. 只处理进程名为 `claude.exe` 的窗口；只点“本次会话/本次聊天中始终允许”或“允许一次”，而且附近必须有“拒绝”等其他审批选项；Claude Desktop 代码页的卡片若只有“拒绝”和“始终允许，保存到本地项目设置”，会点后者（只写入该项目的 `.claude/settings.local.json`）；除此之外不点“始终允许”、“保存到用户设置”这类永久授权，也不点 Windows 的系统授权（UAC）。设置保存在 `%AppData%\ClaudeAutoPass`，不保存对话或截图；网络只用于检查 GitHub 更新。
6. **Steam 创意工坊年龄提示**：右键菜单「自动确认 Steam 创意工坊年龄提示」（默认开启）会在 Steam 客户端和浏览器里自动点成人内容提示框的「查看物品」。只在框里同时有「取消」和「年满 18 周岁」等文字时才点；浏览器只看标题含 Steam 的窗口。不起作用时导出诊断，里面有 Steam 窗口和按钮的情况。
7. 检查更新会读取 `win-v*-tN` 版本，下载后先校验 SHA-256，再原地替换 exe 并重启。exe 放在需要管理员权限的文件夹时会改为打开下载页面。

发布：推送 `main` 且改动了 `windows/` 时，[Windows EXE](https://github.com/tiger-git2022/claude-auto-pass/actions/workflows/windows.yml) 工作流在 Windows 上跑规则测试、编译，并用 WinForms 窗口和 Edge（与 Claude Desktop 同为 Chromium 内核）实测自动点击，然后发布 `win-v<BaseVersion>-tN` 版本。这些 Release 不标记为“最新”，不影响 App 的更新检查；APK 工作流也会忽略 `windows/` 的改动。升级基础版本时修改 `windows/ClaudeAutoPass.csproj` 的 `BaseVersion` 并更新 `windows/CHANGELOG.md`。本地构建：`dotnet run --project windows/tests/RulesTests.csproj`，`dotnet build windows/ClaudeAutoPass.csproj -c Release`。

### 本地构建

安装 Android SDK 35、JDK 17 或更高版本，配置本机 `local.properties` 或 `ANDROID_HOME`。

```powershell
.\scripts\test-rules.ps1
.\gradlew.bat assembleDebug lintDebug
```

构建可覆盖更新的发行 APK 时，还需配置 `APP_KEYSTORE_PATH` 和 `APP_KEYSTORE_PASSWORD`，然后运行 `assembleRelease`。密钥别名为 `claude-auto-pass`。调试 APK 与正式 APK 使用不同签名，不能相互覆盖。

## English

A small personal Android utility that handles approval buttons in the **official Claude app** (`com.anthropic.claude`).

### Features

- Prefers “Always allow for this session”; falls back to “Allow once.” Recognizes an explicit list of English, Simplified Chinese, and Traditional Chinese labels.
- Notification actions: Enable / Disable and Exit. Includes an optional Quick Settings tile.
- Exit stops approval and removes the notification while **keeping accessibility enabled**.
- A minimal Material 3 screen supports light/dark themes and dynamic color, helps enable accessibility and notifications, and shows service status and the last approval.
- Checks stable GitHub releases, displays release notes, downloads APK updates with progress and background download support, and opens the installer on request.
- Validates package name, increasing version code, matching signing certificate, and the GitHub asset SHA-256 when available.

### Usage

1. Install `claude-auto-pass.apk` from [Releases](https://github.com/tiger-git2022/claude-auto-pass/releases/latest). Requires Android 9 or later.
2. Open the app, allow notifications, and enable “Claude 自动审批” in accessibility settings. A new installation starts disabled; system reconnections preserve your switch setting.
3. Enable approval in the app or notification, then open Claude. The phone must be unlocked and Claude must be in the foreground.
4. Disable pauses approval. Exit stops it and removes the notification without revoking accessibility access.
5. Tap Check for updates to view release notes, download an update, and confirm installation. The app guides you to the installation permission only when needed.

Some devices require allowing restricted settings in app details before enabling accessibility for a sideloaded APK.

### Scope and privacy

This utility approves Claude requests on your behalf. Enable it only when that matches your intent. It requires explicit button labels and another nearby approval choice. It does not tap fixed coordinates or approve Android system permission dialogs. Unrecognized UI labels are skipped and may require a rules update.

A foreground service with a visible notification keeps the helper running after leaving the screen. Exit stops that service and preserves accessibility authorization. If the device still restricts it, use Background settings to review autostart and battery settings. The UI distinguishes authorization from a live service connection.

Button text is processed locally. No conversations, screenshots, or screen content are stored or uploaded. Local storage contains controls, the last action, and download state. No Claude credentials are needed. Networking is used only for GitHub release checks and APK downloads.

### Windows (Claude Desktop)

`windows/` contains a tray app for **Claude Desktop** with the same rules, plus desktop labels such as “Allow for this chat”. Download `claude-auto-pass.exe` from the latest `win-v…` [release](https://github.com/tiger-git2022/claude-auto-pass/releases?q=win-v&expanded=true) and run it; no installer is needed (Windows 10/11 include .NET Framework 4.8). Neither the exe nor Claude needs to stay in the foreground: it reads Claude's buttons through Windows UI Automation and invokes them without moving the mouse, even when Claude is in the background (a fully covered or minimized Claude stops updating its page: “restart Claude with accessibility” fixes the covered case; cards that appear while Claude is minimized are not seen). It pauses while the PC is locked. Left-click the tray icon to enable or disable; the right-click menu (Fluent style, acrylic on Windows 11) has start with Windows, button-label diagnostics, restart Claude with accessibility (`--force-renderer-accessibility --disable-features=CalculateNativeWinOcclusion`), update checks (SHA-256 verified, in-place replacement), and exit. It only acts on `claude.exe` windows, never presses permanent grants such as “Always allow” or “save to user settings” (one exception: on Claude Desktop's Code cards that offer only “Deny” and “Always allow, save to local project settings”, it presses the latter, which only writes that project's `.claude/settings.local.json`), and stores settings in `%AppData%\ClaudeAutoPass`. A tray switch (on by default) also presses “View Item” on Steam Workshop's adult-content gate in the Steam client or a browser, only when the same box shows “Cancel” and the 18+ text.

The [Windows EXE](https://github.com/tiger-git2022/claude-auto-pass/actions/workflows/windows.yml) workflow runs rule tests, builds, checks real clicks against WinForms and Edge (Chromium, like Claude Desktop), and publishes `win-v1.0.0-tN` releases that are not marked latest, so the Android app's update check is unaffected.

### Build and release

Pushes to `main`, PRs, and manual workflow runs execute rule checks, compilation, and Android lint. Following the EhViewer repository, every code push to main builds a signed APK and publishes the next numbered release (`1.0.0-t1`, `1.0.0-t2`, etc.). Markdown-only changes skip builds. PRs and other branches only build and check without signing secrets.

No manual tags are needed. The workflow computes the next build number, creates a tag, and publishes a stable release with `claude-auto-pass.apk` and its checksum. A previously released commit is skipped unless a manual run requests a forced release. When changing the base version, increase both `baseVersionName` and `baseVersionCode` and update the Chinese-first / English-second `CHANGELOG.md`. The app checks the latest stable release and compares numbered versions.

Signing uses repository Actions secrets `APP_KEYSTORE_BASE64` and `APP_KEYSTORE_PASSWORD`; keys are excluded from source control. Keep the same key for future updates and back it up securely.

Local builds require Android SDK 35 and JDK 17+. Run `scripts/test-rules.ps1` (Windows) or `bash scripts/test-rules.sh`, followed by `gradlew assembleDebug lintDebug`. Signed release builds additionally require `APP_KEYSTORE_PATH`, `APP_KEYSTORE_PASSWORD`, and key alias `claude-auto-pass`. Debug and release builds cannot replace each other because they use different signatures.
