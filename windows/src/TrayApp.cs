using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace ClaudeAutoPass
{
    /// <summary>Tray icon, menu and the background scan loop.</summary>
    sealed class TrayApp : ApplicationContext
    {
        const int ScanIntervalMs = 800;
        const int CooldownMs = 1500;

        readonly Settings settings = new Settings();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Icon iconOn, iconOff;
        readonly TrayMenu menu = new TrayMenu();
        string updateProgress;   // non-null while an update downloads
        readonly SynchronizationContext ui;
        readonly Thread worker, steamWorker;
        const int SteamIntervalMs = 300;
        /// <summary>Hide / show request for the scan thread (it owns the scanner that finds Claude's windows): 1 hide, 2 show.</summary>
        volatile int hideRequest;
        volatile bool running = true, locked;
        volatile int claudeWindows = -1;
        string scanError;
        bool blindHintShown;
        /// <summary>What a click on the balloon shown last does (null: nothing).</summary>
        Action balloonAction;
        /// <summary>Claude Desktop's main process and whether it has the accessibility switches (scan thread only).</summary>
        readonly SwitchWatch switches = new SwitchWatch();
        DateTime nextSwitchCheck = DateTime.MinValue, lastStuckBalloon = DateTime.MinValue;
        /// <summary>The same button pressed again and again: Claude's page stopped updating (scan thread only).</summary>
        string lastPressLabel;
        DateTime lastPressAt = DateTime.MinValue;
        int repeatPresses;
        /// <summary>A card normally needs two presses (the page shows it once more after the first), rarely three.</summary>
        const int StuckPresses = 4;

        public TrayApp()
        {
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            iconOn = LoadIcon();
            iconOff = Grayscale(iconOn);
            bool firstRun = settings.FirstRun;
            if (firstRun) settings.Enabled = true;
            try { Autostart.Refresh(); } catch (Exception) { }

            TrayMenu.InitTheme();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) SetEnabled(!settings.Enabled); };
            tray.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowMenu(); };
            // Clicking the "minimized window can't be read" balloon offers the fix.
            tray.BalloonTipClicked += (s, e) => { Action action = balloonAction; balloonAction = null; action?.Invoke(); };
            tray.BalloonTipClosed += (s, e) => balloonAction = null;
            tray.Visible = true;
            Refresh();

            SystemEvents.SessionSwitch += OnSessionSwitch;
            ClaudeHider.Start(note => ScanLog.Write(note, false));
            ClaudeTaskbar.Start(() => settings.TaskbarOpen, note => ScanLog.Write(note, false));
            worker = new Thread(ScanLoop) { IsBackground = true, Name = "scan" };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
            steamWorker = new Thread(SteamLoop) { IsBackground = true, Name = "steam" };
            steamWorker.SetApartmentState(ApartmentState.MTA);
            steamWorker.Start();

            if (firstRun || Environment.GetCommandLineArgs().Contains("--updated"))
                tray.ShowBalloonTip(5000, "Claude 自动审批 " + AppInfo.Version,
                    "已在托盘运行。左键单击图标开启/关闭，右键查看更多选项。", ToolTipIcon.Info);
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color || e.Category == UserPreferenceCategory.VisualStyle)
                ui.Post(_ => TrayMenu.FollowSystemTheme(), null);
        }

        /// <summary>Right-click menu: WPF-UI Fluent style shared with EHUploader and ThaiTranslator, acrylic on Windows 11.</summary>
        void ShowMenu()
        {
            Refresh();
            menu.Show(MenuEntries(), Cursor.Position);
        }

        internal List<TrayMenu.Entry> MenuEntries()
        {
            bool on = settings.Enabled;
            bool autostart;
            try { autostart = Autostart.Enabled; } catch (Exception) { autostart = false; }
            string last = settings.LastAction;
            SymbolRegular stateIcon = !on ? SymbolRegular.ShieldDismiss24 : locked ? SymbolRegular.LockClosed24 : claudeWindows == 0 ? SymbolRegular.Search24 : SymbolRegular.ShieldCheckmark24;
            return new List<TrayMenu.Entry>
            {
                new TrayMenu.Entry { Text = "状态：" + StateText(), Icon = stateIcon, Enabled = false },
                new TrayMenu.Entry { Text = "上次：" + (string.IsNullOrEmpty(last) ? "暂无" : Shorten(last, 48)), Icon = SymbolRegular.History24, Enabled = false },
                new TrayMenu.Entry { Separator = true },
                new TrayMenu.Entry { Text = on ? "关闭自动审批" : "开启自动审批", Bold = true, Icon = on ? SymbolRegular.Pause24 : SymbolRegular.Play24, Click = Later(() => SetEnabled(!on)) },
                new TrayMenu.Entry { Text = "自动确认 Steam 创意工坊年龄提示", Icon = settings.SteamGate ? SymbolRegular.CheckboxChecked24 : SymbolRegular.CheckboxUnchecked24, Click = Later(() => { settings.SteamGate = !settings.SteamGate; Refresh(); }) },
                new TrayMenu.Entry { Text = ClaudeHider.Any ? "把 Claude 拿回来" : "把 Claude 藏到屏幕外（代替最小化）", Icon = ClaudeHider.Any ? SymbolRegular.Eye24 : SymbolRegular.EyeOff24, Click = Later(() => { hideRequest = ClaudeHider.Any ? 2 : 1; }) },
                new TrayMenu.Entry { Text = "单击任务栏打开 Claude 主窗口", Icon = settings.TaskbarOpen ? SymbolRegular.CheckboxChecked24 : SymbolRegular.CheckboxUnchecked24, Click = Later(() => { settings.TaskbarOpen = !settings.TaskbarOpen; Refresh(); }) },
                new TrayMenu.Entry { Text = "Claude 普通方式打开时自动以辅助功能模式重启", Icon = settings.AutoRestartClaude ? SymbolRegular.CheckboxChecked24 : SymbolRegular.CheckboxUnchecked24, Click = Later(() => { settings.AutoRestartClaude = !settings.AutoRestartClaude; Refresh(); }) },
                new TrayMenu.Entry { Text = "开机自动启动", Icon = autostart ? SymbolRegular.CheckboxChecked24 : SymbolRegular.CheckboxUnchecked24, Click = Later(ToggleAutostart) },
                new TrayMenu.Entry { Separator = true },
                new TrayMenu.Entry { Text = "导出按钮文字（诊断）…", Icon = SymbolRegular.DocumentSearch24, Click = Later(Diagnose) },
                new TrayMenu.Entry { Text = "以辅助功能模式重启 Claude…", Icon = SymbolRegular.ArrowClockwise24, Click = Later(RestartClaude) },
                new TrayMenu.Entry { Text = updateProgress ?? "检查更新…", Icon = SymbolRegular.ArrowSync24, Enabled = updateProgress == null, Click = Later(async () => await CheckUpdates()) },
                new TrayMenu.Entry { Text = "项目主页", Icon = SymbolRegular.Open24, Click = Later(() => Open("https://github.com/" + AppInfo.Repo)) },
                new TrayMenu.Entry { Separator = true },
                new TrayMenu.Entry { Text = "退出", Icon = SymbolRegular.Power24, Click = Later(ExitThread) },
            };
        }

        /// <summary>Runs a menu action after the menu has closed, so dialogs and exit don't fight the open popup.</summary>
        Action Later(Action action) => () => ui.Post(_ => action(), null);

        string StateText() => !settings.Enabled ? "已关闭" : locked ? "已暂停（锁屏）" : claudeWindows == 0 ? "已开启 · 未找到 Claude" : scanError != null ? "已开启 · 重试中" : "已开启 · 监视中";

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.ConsoleDisconnect || e.Reason == SessionSwitchReason.RemoteDisconnect) locked = true;
            else if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect || e.Reason == SessionSwitchReason.RemoteConnect) locked = false;
        }

        void ScanLoop()
        {
            ScanLog.Write("启动 / started " + AppInfo.Version, false);
            Scanner scanner = null;
            while (running)
            {
                int wait = ScanIntervalMs;
                try
                {
                    // Hiding and bringing back work even while approval is paused.
                    int request = hideRequest;
                    hideRequest = 0;
                    if (request == 1)
                    {
                        scanner = scanner ?? new Scanner();
                        int n = ClaudeHider.Hide(scanner.TargetWindows());
                        ScanLog.Write($"已把 {n} 个 Claude 窗口藏到屏幕外 / hid {n} Claude windows off-screen", false);
                    }
                    else if (request == 2) ClaudeHider.ShowAll();
                    foreach (string note in ClaudeHider.Tick()) ScanLog.Write(note, false);
                    if (settings.Enabled && !locked)
                    {
                        scanner = scanner ?? new Scanner();
                        if (DateTime.UtcNow >= nextSwitchCheck)
                        {
                            nextSwitchCheck = DateTime.UtcNow.AddSeconds(2);
                            CheckSwitches(scanner.TargetWindows());
                        }
                        if (repeatPresses > StuckPresses && (DateTime.UtcNow - lastPressAt).TotalSeconds > 4) EndRepeats();
                        var watch = Stopwatch.StartNew();
                        ScanResult result = scanner.ScanOnce();
                        watch.Stop();
                        if (result.ClaudeWindows != claudeWindows) ScanLog.Write($"Claude 窗口 / windows: {result.ClaudeWindows}，按钮 / buttons: {result.Buttons}", false);
                        claudeWindows = result.ClaudeWindows;
                        scanError = null;
                        if (watch.Elapsed.TotalSeconds >= 3) ScanLog.Write($"扫描很慢 / slow scan: {watch.Elapsed.TotalSeconds:F1}s，按钮 / buttons: {result.Buttons}", false);
                        foreach (string note in result.Notes) ScanLog.Write(note);
                        if (result.Blind != null)
                        {
                            ScanLog.Write($"读不到最小化窗口的内容 / minimized window has no page content \"{result.Blind}\"");
                            if (!blindHintShown)
                            {
                                blindHintShown = true;
                                string title = Shorten(result.Blind, 30);
                                Balloon("Claude 最小化后读不到审批卡片",
                                    $"「{title}」最小化时 Claude 没有提供页面内容，卡片不会被自动点。点这里以辅助功能模式重启 Claude；另外 Claude 最小化后不会更新新弹出的卡片，可以用右键菜单「把 Claude 藏到屏幕外」代替最小化。", ToolTipIcon.Warning, RestartClaude);
                            }
                        }
                        if (result.Clicked != Kind.None)
                        {
                            NotePress(result.Label);
                            if (repeatPresses <= StuckPresses) ScanLog.Write($"已点 / pressed \"{result.Label}\" ({watch.Elapsed.TotalSeconds:F1}s)", false);
                            lock (settings) settings.LastAction = ApprovalRules.Describe(result.Clicked) + " · " + Autostart.Stamp() + " · " + result.Label;
                            wait = CooldownMs;
                            ui.Post(_ => Refresh(), null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // UI Automation calls can fail while windows close; start over with a fresh client.
                    scanError = ex.GetType().Name;
                    ScanLog.Write("扫描出错 / scan error: " + ex.GetType().Name + " " + ex.Message);
                    scanner = null;
                    wait = 3000;
                }
                Thread.Sleep(wait);
            }
        }

        /// <summary>Shows a tray balloon from any thread; a click on it runs <paramref name="onClick"/> on the UI thread.</summary>
        void Balloon(string title, string text, ToolTipIcon icon, Action onClick = null)
        {
            ui.Post(_ => { if (!running) return; balloonAction = onClick; tray.ShowBalloonTip(15000, title, text, icon); }, null);
        }

        /// <summary>Claude opened the normal way lacks the switches that keep a covered Claude updating: restart it or say so.</summary>
        void CheckSwitches(List<Native.Window> windows)
        {
            SwitchState state = switches.Tick(windows, settings.AutoRestartClaude, out string note);
            if (note != null) ScanLog.Write(note, false);
            if (state == SwitchState.Restarted)
                Balloon("已以辅助功能模式重启 Claude",
                    "Claude 刚以普通方式打开（比如更新后），没带让它被挡住时继续更新的参数，已自动重启一次。不想这样可以在右键菜单关掉「Claude 普通方式打开时自动以辅助功能模式重启」。", ToolTipIcon.Info);
            else if (state == SwitchState.Missing || state == SwitchState.RestartFailed)
                WarnNoSwitches();
        }

        void WarnNoSwitches()
        {
            lastStuckBalloon = DateTime.UtcNow;
            Balloon("Claude 被挡住时不会自动审批",
                "Claude 是以普通方式打开的：被其他窗口完全挡住或屏幕关掉后页面不再更新，新卡片要等你切回去滚动一下才会被点。点这里以辅助功能模式重启 Claude。", ToolTipIcon.Warning, RestartClaude);
        }

        /// <summary>Counts presses of the same button in a row; past <see cref="StuckPresses"/> Claude's page isn't updating.</summary>
        void NotePress(string label)
        {
            DateTime now = DateTime.UtcNow;
            bool same = label == lastPressLabel && (now - lastPressAt).TotalSeconds < 4;
            if (!same) EndRepeats();
            repeatPresses = same ? repeatPresses + 1 : 1;
            lastPressLabel = label;
            lastPressAt = now;
            if (repeatPresses != StuckPresses) return;
            bool covered = !switches.UpdatesWhenCovered;
            ScanLog.Write($"同一个按钮按了 {StuckPresses} 次还在，Claude 页面没有更新" + (covered ? "（Claude 没带辅助功能参数，可能被挡住或屏幕关了）" : "")
                + $"，之后的重复不再逐条记录 / pressed \"{label}\" {StuckPresses} times, Claude's page isn't updating", false);
            if (covered && (now - lastStuckBalloon).TotalMinutes >= 30) WarnNoSwitches();
        }

        void EndRepeats()
        {
            if (repeatPresses > StuckPresses) ScanLog.Write($"连按结束：\"{lastPressLabel}\" 一共按了 {repeatPresses} 次 / stopped after {repeatPresses} presses", false);
            repeatPresses = 0;
        }

        /// <summary>Steam Workshop's gate, on its own thread so it is pressed quickly whatever Claude's scan is doing.</summary>
        void SteamLoop()
        {
            SteamGateScanner steam = null;
            while (running)
            {
                int wait = SteamIntervalMs;
                try
                {
                    if (settings.Enabled && settings.SteamGate && !locked)
                    {
                        steam = steam ?? new SteamGateScanner();
                        var notes = new List<string>();
                        var watch = Stopwatch.StartNew();
                        string label = steam.ScanIfDue(notes, out bool scanned);
                        foreach (string note in notes) ScanLog.Write(note);
                        if (scanned && watch.Elapsed.TotalSeconds >= 3) ScanLog.Write($"Steam 扫描很慢 / slow Steam scan: {watch.Elapsed.TotalSeconds:F1}s");
                        if (label != null)
                        {
                            ScanLog.Write($"Steam 已点 / pressed \"{label}\" ({watch.Elapsed.TotalSeconds:F1}s)", false);
                            lock (settings) settings.LastAction = "已确认 Steam 创意工坊 · " + Autostart.Stamp() + " · " + label;
                            wait = CooldownMs;
                            ui.Post(_ => Refresh(), null);
                        }
                    }
                    else wait = 1000;
                }
                catch (Exception ex)
                {
                    ScanLog.Write("Steam 扫描出错 / Steam scan error: " + ex.GetType().Name + " " + ex.Message);
                    steam = null;
                    wait = 3000;
                }
                Thread.Sleep(wait);
            }
        }

        void Refresh()
        {
            // A refresh posted by the scan loop can arrive after exit (e.g. right after an update) when the icon is disposed.
            if (!running) return;
            bool on = settings.Enabled;
            string state = StateText();
            tray.Icon = on ? iconOn : iconOff;
            tray.Text = Shorten("Claude 自动审批：" + state, 63);
        }

        static string Shorten(string text, int max) => text.Length <= max ? text : text.Substring(0, max - 1) + "…";

        void SetEnabled(bool value)
        {
            settings.Enabled = value;
            Refresh();
        }

        void ToggleAutostart()
        {
            try { Autostart.Enabled = !Autostart.Enabled; }
            catch (Exception ex) { MessageBox.Show("无法修改开机启动：" + ex.Message, "Claude 自动审批", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            Refresh();
        }

        void Diagnose()
        {
            string report = null;
            var thread = new Thread(() =>
            {
                try { report = new Scanner().Diagnose(); }
                catch (Exception ex) { report = "诊断失败 / Diagnostics failed: " + ex; }
                try { report += "\r\n\r\n== Steam 创意工坊年龄提示 / Steam Workshop gate（" + (settings.SteamGate ? "已开启 / on" : "已关闭 / off") + "）\r\n" + new SteamGateScanner().Diagnose(); }
                catch (Exception ex) { report += "\r\n\r\nSteam 诊断失败 / Steam diagnostics failed: " + ex; }
            });
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            thread.Join(TimeSpan.FromSeconds(60));
            string path = Path.Combine(AppInfo.DataDir, "diagnostics.txt");
            report = (report ?? "诊断超时 / Diagnostics timed out")
                + "\r\n\r\n== 右键菜单尺寸 / Tray menu layout\r\n" + (menu.Layout ?? "").Replace("\n", "\r\n");
            try
            {
                if (File.Exists(ScanLog.Path))
                {
                    string[] lines = File.ReadAllLines(ScanLog.Path);
                    report += "\r\n\r\n== 最近扫描记录 / Recent scan log (" + ScanLog.Path + ")\r\n" + string.Join("\r\n", lines.Skip(Math.Max(0, lines.Length - 80)));
                }
            }
            catch (Exception) { }
            File.WriteAllText(path, report);
            Open(path);
        }

        void RestartClaude()
        {
            ClaudeLaunch.Main main = ClaudeLaunch.Find(ClaudeLaunch.Windows());
            if (main == null || string.IsNullOrEmpty(main.Exe))
            {
                MessageBox.Show("没有找到正在运行的 Claude Desktop。请先打开 Claude。", "Claude 自动审批", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show("将关闭 Claude Desktop，并加上 " + AppInfo.ClaudeSwitches + " 重新打开：让本工具能读到按钮，" +
                    "并让 Claude 被其他窗口完全挡住时也继续更新（否则挡住后新弹的卡片读不到）。\n\n" +
                    "在「诊断」显示元素很少，或 Claude 被挡住、最小化后不自动审批时需要；" +
                    "之后以普通方式重新打开 Claude（包括 Claude 更新后）会失效，开着「Claude 普通方式打开时自动以辅助功能模式重启」时本工具会自动再重启一次。未发送的输入可能丢失。继续吗？",
                    "以辅助功能模式重启 Claude", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            try { ClaudeLaunch.Restart(main); }
            catch (Exception ex) { MessageBox.Show("无法重新打开 Claude：" + ex.Message + "\n" + main.Exe, "Claude 自动审批", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        async System.Threading.Tasks.Task CheckUpdates()
        {
            updateProgress = "正在检查更新…";
            try
            {
                Release release = await Updater.CheckAsync();
                if (release == null)
                {
                    MessageBox.Show("已是最新版本：" + AppInfo.Version, "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                string notes = Shorten(release.Notes ?? "", 1500);
                if (MessageBox.Show($"发现新版本 {release.Tag}（当前 {AppInfo.Version}）。\n\n{notes}\n\n现在下载并更新吗？",
                        "检查更新", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                updateProgress = "正在下载… 0%";
                var progress = new Progress<int>(p => updateProgress = "正在下载… " + p + "%");
                string file = await Updater.DownloadAsync(release, progress);
                Updater.Install(file);
                ExitThread();
            }
            catch (UnauthorizedAccessException)
            {
                if (MessageBox.Show("没有权限替换当前 exe（可能放在受保护的文件夹）。要打开下载页面手动更新吗？", "检查更新",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) Open(AppInfo.ReleasesUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show("检查更新失败：" + ex.Message, "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                updateProgress = null;
            }
        }

        static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception) { }
        }

        static Icon LoadIcon()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                return s != null ? new Icon(s, SystemInformation.SmallIconSize) : SystemIcons.Application;
        }

        static Icon Grayscale(Icon source)
        {
            using (Bitmap color = source.ToBitmap())
            using (var gray = new Bitmap(color.Width, color.Height))
            {
                using (Graphics g = Graphics.FromImage(gray))
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(new ColorMatrix(new[]
                    {
                        new[] { .3f, .3f, .3f, 0, 0 },
                        new[] { .59f, .59f, .59f, 0, 0 },
                        new[] { .11f, .11f, .11f, 0, 0 },
                        new[] { 0, 0, 0, .6f, 0 },
                        new[] { 0, 0, 0, 0, 1f },
                    }));
                    g.DrawImage(color, new Rectangle(0, 0, color.Width, color.Height), 0, 0, color.Width, color.Height, GraphicsUnit.Pixel, attributes);
                }
                return Icon.FromHandle(gray.GetHicon());
            }
        }

        protected override void ExitThreadCore()
        {
            running = false;
            try { ClaudeHider.ShowAll(); } catch (Exception) { } // never leave Claude off-screen
            ClaudeHider.Stop();
            ClaudeTaskbar.Stop();
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            menu.Close();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
