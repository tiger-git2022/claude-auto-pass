# Windows 版更新记录 / Windows Changelog

## 1.2.6

### 中文

- 修复 1.2.5「单击任务栏不弹出 Claude 主窗口」：Claude 的托盘图标左键单击打开的是快速输入框，不是主窗口（作者电脑上的日志和 Claude 3.6556 的代码都证实了），所以 1.2.5 替你单击托盘图标没用。现在改成右键 Claude 的托盘图标，再点菜单第一项「Show App」，和你手动从托盘菜单打开完全一样。托盘菜单会在右下角闪一下。
- 扫描日志会记下点了菜单里哪一项、主窗口有没有打开。

### English

- Fixes 1.2.5 not opening Claude's main window on a taskbar click: a left click on Claude's tray icon opens its quick entry, not the main window (confirmed by the owner's log and Claude 3.6556's code). Now the tool right-clicks Claude's tray icon and picks the menu's first item, "Show App", exactly like opening it from the tray menu by hand. The tray menu flashes briefly in the corner.
- The scan log notes which menu item was picked and whether the main window opened.

## 1.2.5

### 中文

- 修复「单击任务栏打开 Claude 主窗口」打开后界面卡死、点不动：Claude 主窗口收到托盘（点 X）时，1.2.3 是从外面直接把窗口显示出来，窗口虽然出现了，但 Claude 内部还认为它是隐藏的，页面不刷新也不响应点击。现在改成替你单击 Claude 的托盘图标，让 Claude 自己打开主窗口，和从托盘菜单打开一样。最小化时的打开方式不变。
- 如果单击托盘图标后主窗口没打开，扫描日志会记一条，方便排查。

### English

- Fixes Claude's main window being frozen after “Click the taskbar to open Claude's main window” opened it: when the main window was closed to the tray (X), 1.2.3 showed it from outside, and the window appeared while Claude still treated it as hidden, so the page neither redrew nor took clicks. Now the tool clicks Claude's tray icon so Claude opens its main window itself, the same as opening it from the tray menu. Opening a minimized window is unchanged.
- If the tray click does not open the main window, the scan log notes it for troubleshooting.

## 1.2.4

### 中文

- 修复「检查更新失败：403 (rate limit exceeded)」：GitHub 接口对没登录的访问每个 IP 每小时只给 60 次，用代理或 VPN 时同一出口的人共用这个额度，很容易用完。现在改为先从 GitHub 网页读发布列表（没有这个限制），网页读不到才用接口；下载的 exe 照样用 SHA256SUMS.txt 校验。

### English

- Fixes “Check for updates failed: 403 (rate limit exceeded)”: GitHub's API allows 60 anonymous calls an hour per IP address, shared by everyone behind the same proxy or VPN exit, so it runs out easily. The release list is now read from the GitHub website first (no such limit), with the API only as a fallback; the downloaded exe is still checked against SHA256SUMS.txt.

## 1.2.3

### 中文

- 「单击任务栏打开 Claude 主窗口」现在也能处理 Claude 主窗口被隐藏的情况（点 X 收到托盘）：作者电脑上的日志显示，单击没反应时 Claude 主窗口是隐藏的，不是最小化，1.2.2 只处理了最小化。现在主窗口隐藏时，Claude 弹出快速输入框、某个窗口被激活，或者被再次启动（点任务栏上固定的图标），都会把主窗口显示到前台。
- 最小化或关掉后马上点也能打开：之前刚最小化 1.5 秒内的单击会被忽略（作者日志里第一次单击就是这样漏掉的），现在缩短到 0.6 秒；判断也更快，快速输入框只会闪一下。
- 修复「藏到屏幕外」刚藏就被拿回来：藏的时候 Claude 往往还是当前窗口，旧的扫描检查会把这当成「切到了 Claude」。现在只看真正的窗口切换。
- 扫描日志也会记下 Claude 主窗口被隐藏的事件，方便继续排查。
- Steam 年龄确认按得更快、电脑更不卡：之前每次确认要把整个页面读三遍（作者日志里每次 1.1 到 1.3 秒），而且一直在扫所有 Steam 和浏览器窗口，让它们忙个不停。现在只扫前台窗口，页面刚切换的 8 秒里连续扫，之后每 2.5 秒才扫一次；确认「取消」和 18+ 文字时只看按钮附近的小框，不再读整页。扫描超过 3 秒会记进扫描日志。
- 「单击任务栏打开 Claude 主窗口」占用更少：窗口显示、隐藏的事件只监听 Claude 自己的进程（之前是所有程序），检测 Claude 被再次启动也只在主窗口关着的时候才做。

### English

- “Click the taskbar to open Claude's main window” now also handles a hidden main window (closed to the tray with X): the owner's log showed Claude's main window hidden, not minimized, when a click did nothing, and 1.2.2 only handled minimized. Now, while the main window is hidden, Claude showing its quick entry, activating one of its windows, or being launched again (its pinned taskbar icon) brings the main window to the front.
- A click right after minimizing or closing works too: clicks within 1.5 s of minimizing were ignored (the owner's first click was missed that way); now 0.6 s, and the decision is quicker so the quick entry only flashes.
- Fixes “Hide Claude off-screen” coming straight back: Claude is usually still the active window when hidden, and the old scan-loop check took that as switching to it. Only real window switches count now.
- The scan log also records Claude's main window being hidden, for further troubleshooting.
- The Steam age gate is confirmed faster and the PC stays smoother: each confirmation read the whole page three times (1.1 to 1.3 s per press in the owner's log), and every Steam and browser window was scanned all the time, keeping them busy. Now only the window in front is scanned, continuously for 8 s after its page changes and every 2.5 s after that; “取消” and the 18+ text are looked for in small boxes around the button instead of the whole page. Scans taking 3 s or more go to the scan log.
- “Click the taskbar to open Claude's main window” costs less: window show and hide events are watched only in Claude's own processes (all programs before), and Claude being launched again is only watched for while its main window is closed.

## 1.2.2

### 中文

- 新增右键菜单开关「单击任务栏打开 Claude 主窗口」（默认开启）：Claude 自己在 Windows 上单击任务栏图标只会弹快速输入框，不打开主窗口（[anthropics/claude-code#22420](https://github.com/anthropics/claude-code/issues/22420)），最小化后只能右键打开。现在 Claude 最小化时，只要它的某个窗口被激活或弹出，本工具就把主窗口还原到前台。刚最小化 1.5 秒内不会触发，免得点任务栏最小化后又弹回来。最小化状态下用快捷键呼出快速输入框也会带出主窗口，不想要可以关掉这个开关。
- 扫描日志会记下 Claude 最小化期间的窗口事件，「导出按钮文字（诊断）」会列出 Claude 的所有顶层窗口，如果单击还是打不开，把诊断和日志发来就能看出 Claude 实际做了什么。
- 修复「藏到屏幕外」实际没把窗口移出去：Windows 会把完全在屏幕外的位置自动拉回屏幕内（CI 新测试发现），现在改用另一种方式移动窗口。
- 修复「藏到屏幕外」之后在任务栏单击、双击 Claude 都打不开：Claude 还是当前窗口时，单击任务栏会被 Windows 当成最小化，再点又回到屏幕外。现在本工具实时监听窗口切换和最小化，单击任务栏上的 Claude 就立刻回到原位并切到前台，不用再等下一轮扫描。
- 按 Win+M 之类和其他窗口一起最小化时，Claude 保持最小化，但位置已放回屏幕内，之后在任务栏点它就正常打开。
- 藏起来的 Claude 会排到所有窗口最后，关掉或最小化别的程序时焦点不会落到它身上，也就不会被误拿回来。
- CI 新增测试：藏起来后模拟任务栏单击、Win+M、再点任务栏还原（Edge）；最小化时弹出另一个窗口会带出主窗口。

### English

- New tray switch “Click the taskbar to open Claude's main window” (on by default): on Windows, Claude itself answers a click on its taskbar icon with the quick entry bar instead of the main window ([anthropics/claude-code#22420](https://github.com/anthropics/claude-code/issues/22420)), so a minimized Claude only opens from the right-click menu. Now, while Claude is minimized, any of its windows being activated or shown restores the main window in front. Not within 1.5 s of minimizing, so minimizing from the taskbar doesn't bounce back. Calling up the quick entry with its shortcut while minimized also brings the main window; switch this off if that's unwanted.
- The scan log records Claude's window events while it is minimized, and “Export button labels (diagnostics)” lists all of Claude's top-level windows, so if a click still doesn't open it, the diagnostics and log show what Claude really does.
- Fixes “Hide Claude off-screen” not actually moving the window: Windows pulls a placement that is completely off-screen back onto a monitor (found by the new CI test); the window is now moved another way.
- Fixes a click or double-click on Claude in the taskbar not opening it after “Hide Claude off-screen”: while Claude was still the active window, Windows took the click as minimize, and the next click restored it off-screen again. This tool now listens for window switches and minimizing, so one click on Claude in the taskbar brings it back right away, in front, without waiting for the next scan.
- Minimized together with other windows (Win+M and the like), Claude stays minimized but with its place back on-screen, so a later taskbar click opens it normally.
- A hidden Claude goes to the back of the window order, so closing or minimizing another program doesn't hand it the focus and bring it back by mistake.
- New CI tests: hide, then a simulated taskbar click, Win+M and restoring from the taskbar (Edge); another window shown while minimized brings the main window.

## 1.2.1

### 中文

- 去掉「Claude 最小化时改放到最底层」：实际使用中 Claude 被还原时会自己跳到最前面，没法用。设置里留下的开关不再起作用。
- Steam 创意工坊年龄提示改为单独扫描，每 0.3 秒一次，不再跟 Claude 的扫描排队，出现后基本马上就点。
- 修复 Claude 被完全挡住时仍不自动审批：Claude 自己的设置会覆盖 1.2.0 加的 `--disable-features`（CI 已复现）。「以辅助功能模式重启 Claude」现在再加上不会被覆盖的 `--disable-backgrounding-occluded-windows`，需要用它重启一次 Claude。
- 新增右键菜单「把 Claude 藏到屏幕外（代替最小化）」：把 Claude 窗口移到屏幕外面（不抢焦点），页面照常更新、照常自动审批；在任务栏点 Claude 或 Alt+Tab 切过去就会回到原位，菜单里也可以「把 Claude 拿回来」，退出本工具时也会自动拿回。真正最小化后依然读不到新卡片。
- 「导出按钮文字（诊断）」新增 Claude 实际的启动参数，能看出上面的参数是否生效。

### English

- Removes “Put minimized Claude windows at the back”: in real use Claude brings itself to the front when restored, so it was unusable. A saved setting no longer has any effect.
- The Steam Workshop age gate is scanned on its own every 0.3 s instead of queuing behind Claude's scan, so it is pressed almost as soon as it appears.
- Fixes a fully covered Claude still not being approved: Claude's own settings replace the `--disable-features` added in 1.2.0 (reproduced in CI). “Restart Claude with accessibility” now also passes `--disable-backgrounding-occluded-windows`, which cannot be replaced that way; restart Claude with it once.
- New tray item “Hide Claude off-screen (instead of minimizing)”: moves Claude's windows past the edge of the screen without activating them, where the page keeps updating and cards are approved as usual. Clicking Claude in the taskbar or Alt+Tab brings it back, as does “Bring Claude back” in the menu or exiting this tool. A truly minimized Claude still can't be read.
- “Export button labels (diagnostics)” now shows the switches Claude really runs with, to check that they took effect.

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
