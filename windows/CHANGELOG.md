# Windows 版更新记录 / Windows Changelog

## 1.2.0

### 中文

- 新增：自动确认 Steam 创意工坊的成人内容提示（「该物品包含您要求屏蔽的内容 … 查看物品 / 取消」），在 Steam 客户端和浏览器（Edge、Chrome、Firefox 等）里都能用，同样不移动鼠标。
- 只点提示框里的「查看物品」：它旁边必须紧挨着「取消」，正上方要有「年满 18 周岁 / 裸露 / 色情」这类文字，页面上别处的「查看」不会点。浏览器只在标题含 Steam 的窗口里找，最小化的窗口不扫描。
- 右键菜单新增开关「自动确认 Steam 创意工坊年龄提示」，默认开启；关闭自动审批时也一起暂停。
- 「导出按钮文字（诊断）」会附上 Steam 窗口和找到的「查看物品」按钮，不起作用时用它排查。
- Claude 窗口最小化后不自动审批的原因：Claude 没用辅助功能模式启动时，窗口在被读取前就最小化，页面内容不会提供出来，只剩标题栏按钮（CI 用 Edge 复现）。现在发现这种情况会在托盘弹一次提示，点提示即可「以辅助功能模式重启 Claude」，之后最小化也能自动审批。诊断里会标出窗口是否最小化。
- 不过即使用了辅助功能模式，Claude 最小化后页面也不再更新，最小化之后才弹出的新卡片读不到；被其他窗口完全挡住时也一样（Chromium 的遮挡检测会冻结页面，CI 用 Edge 复现）。现在「以辅助功能模式重启 Claude」会同时加上 `--disable-features=CalculateNativeWinOcclusion`，被挡住的 Claude 会继续更新、照常自动审批。
- 新增开关「Claude 最小化时改放到最底层」（默认关闭，打开时会提示以辅助功能模式重启 Claude）：最小化的 Claude 只要被其他窗口完全挡住，程序就把它还原并压到所有窗口最底下，不抢焦点、不弹到前面，新卡片也能自动审批；没有窗口挡着时保持最小化，挡着的窗口移开或最小化后它会自动缩回去，你自己把它切到前面就不再管它。

### English

- New: confirms Steam Workshop's adult-content gate (“This item contains content you have asked to hide … View Item / Cancel”) automatically, in the Steam client and in browsers (Edge, Chrome, Firefox…), without moving the mouse.
- Only “View Item” on the gate is pressed: “Cancel” must sit right beside it and the 18+ / nudity text right above, so other “View” buttons are left alone. Browser windows are only scanned while their title mentions Steam; minimized windows are skipped.
- A new tray menu switch, “Auto-confirm Steam Workshop age gate”, is on by default and pauses together with auto-approval.
- “Export button labels (diagnostics)” now lists Steam windows and the “View Item” buttons found, for troubleshooting.
- Approvals stopping while a Claude window is minimized: unless Claude runs with accessibility forced on, a window minimized before anything read it exposes only its title bar (reproduced with Edge in CI). This is now detected and a tray balloon offers to restart Claude with accessibility, after which minimized windows are approved too. Diagnostics show whether a window is minimized.
- Even with accessibility on, a minimized Claude window stops updating, so cards that appear after minimizing are never seen, and so does a window covered completely by others (Chromium’s occlusion tracking freezes it; reproduced with Edge in CI). “Restart Claude with accessibility” now also passes `--disable-features=CalculateNativeWinOcclusion`, so a covered Claude keeps updating and is approved as usual.
- New switch “Put minimized Claude windows at the back” (off by default; turning it on offers that restart): a minimized Claude window is restored behind all other windows, without activating it, whenever other windows cover it completely, so new cards are approved too; with nothing covering it, it stays minimized, and it is minimized again once uncovered (bringing it to the front yourself ends this).

## 1.1.2

### 中文

- 修复代码页卡片上「始终允许 , 仅适用于本次会话」不点：这个选项只在本次会话有效，现在按「本次会话」处理并优先点它。「保存到用户设置」「保存到项目设置」依然不点。

### English

- Fixes Code cards whose button reads “Always allow, only for this session”: it is session-scoped, so it is now treated as a session approval and preferred. “Save to user settings” and “save to project settings” are still never pressed.

## 1.1.1

### 中文

- 新增后台扫描记录 `%AppData%\ClaudeAutoPass\scan-log.txt`：看到「始终允许」「允许一次」这类按钮却没有点时，记下原因（不像审批卡片、按钮不可用、按下失败等），也记录很慢的扫描和扫描出错，方便排查偶尔不点的问题。同一条一分钟内只记一次，文件超过 512 KB 自动换新。
- 「导出按钮文字（诊断）」会列出审批按钮每一层上级里的按钮，并附上最近的扫描记录。
- 修复更新后退出时偶尔崩溃（托盘图标已释放后又刷新）。

### English

- Adds a background scan log at `%AppData%\ClaudeAutoPass\scan-log.txt`: when an “Always allow” or “Allow once” style button is seen but not pressed, it records why (not a prompt, disabled, press failed…), plus slow scans and scan errors, to track down prompts that are occasionally skipped. Repeats are logged once a minute; the file rolls over at 512 KB.
- “Export button labels (diagnostics)” now shows the buttons at each parent level of approval buttons and appends the recent scan log.
- Fixes an occasional crash on exit after an update (the tray icon was refreshed after it was disposed).

## 1.1.0

### 中文

- 支持 Claude Desktop 代码页的权限卡片：卡片上只有「拒绝」和「始终允许，保存到本地项目设置」时，点后者（只写入该项目的 `.claude/settings.local.json`）。有「允许一次」「本次会话中允许」时仍优先点它们；「保存到用户设置」「保存到项目设置」这类更大范围的授权依然不点。
- 右键菜单改成和 EH 上传助手一致的 WPF-UI Fluent 风格：圆角、Fluent 图标、跟随系统深浅色；Windows 11 上是亚克力半透明背景，Windows 10 为普通背景。
- 依赖库压缩进 exe，仍然是单个文件。
- 右键菜单文字按像素对齐显示更清晰，去掉「退出」下方多余的空白。
- CI 新增：模拟代码页卡片的 Edge 实测，以及右键菜单深浅色截图。

### English

- Supports Claude Desktop's Code permission cards: when a card offers only “Deny” and “Always allow, save to local project settings”, the latter is pressed (it only writes that project's `.claude/settings.local.json`). “Allow once” and session-wide choices still come first; wider grants such as “save to user settings” or “save to project settings” are never pressed.
- The right-click menu now uses the same WPF-UI Fluent style as EH Uploader: rounded corners, Fluent icons, light or dark with Windows, and an acrylic backdrop on Windows 11 (plain background on Windows 10).
- Libraries are compressed into the exe, so it is still a single file.
- Sharper menu text (pixel-snapped) and no empty space below “Exit”.
- CI adds an Edge test for the Code permission card and light/dark screenshots of the menu.

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
