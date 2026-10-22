# Windows 版更新记录 / Windows Changelog

## 1.0.0

### 中文

- 首次发布：Claude Desktop（Windows 桌面端）自动审批，托盘程序，单个 exe，无需安装。
- 规则沿用 Android 版：优先“本次会话/本次聊天中始终允许”，其次“允许一次”；只认明确的按钮文字，并要求旁边有“拒绝”等其他审批选项；不点“始终允许”这类永久授权。
- 通过 Windows UI 自动化点击按钮，不移动鼠标，Claude 在后台或最小化时也能工作；锁屏时暂停。
- 托盘菜单：开启/关闭、开机自启、按钮文字诊断、以辅助功能模式重启 Claude、检查更新（校验 SHA-256 后原地替换 exe）。
- GitHub Actions 在 Windows 上测试规则、用 WinForms 和 Edge 实测点击，每次推送 main 自动发布新的 win-v1.0.0-tN 版本。

### English

- Initial release: automatic approval for Claude Desktop on Windows, as a tray app in a single exe with no installer.
- Same rules as the Android app: prefers “Always allow for this session/chat”, falls back to “Allow once”; exact labels only, with another approval choice such as “Deny” nearby; never presses permanent grants like “Always allow”.
- Presses buttons through Windows UI Automation without moving the mouse, so Claude can stay in the background or minimized; pauses while the PC is locked.
- Tray menu: enable/disable, start with Windows, button-label diagnostics, restart Claude with accessibility, and update checks that verify SHA-256 and replace the exe in place.
- GitHub Actions tests the rules, checks real clicks against WinForms and Edge on Windows, and publishes a new win-v1.0.0-tN release on each push to main.
