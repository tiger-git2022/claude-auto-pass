# 更新记录 / Changelog

## 1.0.0

### 中文

- 缩短通知栏标题，完整显示自动审批的开启、关闭和重连状态。

- 修复后台稳定性：新增可退出的前台保活服务，保留反馈中断与系统重连前的开关状态。
- 区分无障碍权限与服务连接状态，增加后台运行设置入口。

- 首次发布：Claude 官方 Android App 自动审批。
- 优先“本次会话中始终允许”，其次“允许一次”。
- 通知栏支持开启、关闭和退出；退出保留无障碍权限。
- Material 3 界面、深浅色和动态配色，简单权限引导、运行状态、最近审批和可选快捷设置开关。
- 支持检查 GitHub 更新、显示更新说明、后台下载和下载进度，下载完成后手动确认安装。
- 更新前校验包名、版本、签名及可用的文件摘要。
- GitHub Actions 自动测试、编译签名 APK，每次推送 main 自动发布新的 -tN 编号版本。

### English

- Shorten notification titles so enable, disable, and reconnect states fit on screen.

- Improve background stability with a user-stoppable foreground service; preserve controls across feedback interruptions and system reconnections.
- Distinguish accessibility authorization from service connection and add background settings guidance.

- Initial release: automatic approval for the official Claude Android app.
- Prefers session-wide approval, falling back to allow once.
- Notification controls to enable, disable, and exit; exit preserves accessibility access.
- Material 3 UI with light/dark themes and dynamic color, minimal permission setup, status, last approval, and an optional Quick Settings tile.
- GitHub update checks, release notes, background APK downloads with progress, and user-confirmed installation.
- Package, version, signing certificate, and available digest checks before installing an update.
- GitHub Actions tests and signed APK builds, publishing a new numbered -tN release on each code push to main.
