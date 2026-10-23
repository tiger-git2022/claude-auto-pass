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
        readonly Thread worker;
        volatile bool running = true, locked;
        volatile int claudeWindows = -1;
        string scanError;

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
            tray.Visible = true;
            Refresh();

            SystemEvents.SessionSwitch += OnSessionSwitch;
            worker = new Thread(ScanLoop) { IsBackground = true, Name = "scan" };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();

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
                    if (settings.Enabled && !locked)
                    {
                        scanner = scanner ?? new Scanner();
                        var watch = Stopwatch.StartNew();
                        ScanResult result = scanner.ScanOnce();
                        watch.Stop();
                        if (result.ClaudeWindows != claudeWindows) ScanLog.Write($"Claude 窗口 / windows: {result.ClaudeWindows}，按钮 / buttons: {result.Buttons}", false);
                        claudeWindows = result.ClaudeWindows;
                        scanError = null;
                        if (watch.Elapsed.TotalSeconds >= 3) ScanLog.Write($"扫描很慢 / slow scan: {watch.Elapsed.TotalSeconds:F1}s，按钮 / buttons: {result.Buttons}", false);
                        foreach (string note in result.Notes) ScanLog.Write(note);
                        if (result.Clicked != Kind.None)
                        {
                            ScanLog.Write($"已点 / pressed \"{result.Label}\" ({watch.Elapsed.TotalSeconds:F1}s)", false);
                            settings.LastAction = ApprovalRules.Describe(result.Clicked) + " · " + Autostart.Stamp() + " · " + result.Label;
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
            Process[] processes = Process.GetProcesses().Where(p => { try { return ApprovalRules.IsClaudeProcess(p.ProcessName); } catch (Exception) { return false; } }).ToArray();
            string exe = null;
            foreach (Process p in processes)
            {
                try { exe = exe ?? p.MainModule?.FileName; } catch (Exception) { }
            }
            if (exe == null)
            {
                MessageBox.Show("没有找到正在运行的 Claude Desktop。请先打开 Claude。", "Claude 自动审批", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show("将关闭 Claude Desktop，并加上 --force-renderer-accessibility 重新打开，让本工具能读到按钮。\n\n" +
                    "只在「诊断」显示元素很少、自动审批不起作用时需要。未发送的输入可能丢失。继续吗？",
                    "以辅助功能模式重启 Claude", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            foreach (Process p in processes)
            {
                try { p.Kill(); p.WaitForExit(5000); } catch (Exception) { }
            }
            try { Process.Start(new ProcessStartInfo(exe, "--force-renderer-accessibility") { UseShellExecute = false }); }
            catch (Exception ex) { MessageBox.Show("无法重新打开 Claude：" + ex.Message + "\n" + exe, "Claude 自动审批", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            menu.Close();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
