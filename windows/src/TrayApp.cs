using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

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
        readonly ToolStripMenuItem statusItem = new ToolStripMenuItem { Enabled = false };
        readonly ToolStripMenuItem lastItem = new ToolStripMenuItem { Enabled = false };
        readonly ToolStripMenuItem enabledItem = new ToolStripMenuItem("开启自动审批");
        readonly ToolStripMenuItem autostartItem = new ToolStripMenuItem("开机自动启动");
        readonly ToolStripMenuItem updateItem = new ToolStripMenuItem("检查更新…");
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

            enabledItem.Click += (s, e) => SetEnabled(!settings.Enabled);
            autostartItem.Click += (s, e) => ToggleAutostart();
            updateItem.Click += async (s, e) => await CheckUpdates();
            var menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[]
            {
                statusItem, lastItem, new ToolStripSeparator(),
                enabledItem, autostartItem, new ToolStripSeparator(),
                new ToolStripMenuItem("导出按钮文字（诊断）…", null, (s, e) => Diagnose()),
                new ToolStripMenuItem("以辅助功能模式重启 Claude…", null, (s, e) => RestartClaude()),
                updateItem,
                new ToolStripMenuItem("项目主页", null, (s, e) => Open("https://github.com/" + AppInfo.Repo)),
                new ToolStripSeparator(),
                new ToolStripMenuItem("退出", null, (s, e) => ExitThread()),
            });
            menu.Opening += (s, e) => Refresh();
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) SetEnabled(!settings.Enabled); };
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

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.ConsoleDisconnect || e.Reason == SessionSwitchReason.RemoteDisconnect) locked = true;
            else if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect || e.Reason == SessionSwitchReason.RemoteConnect) locked = false;
        }

        void ScanLoop()
        {
            Scanner scanner = null;
            while (running)
            {
                int wait = ScanIntervalMs;
                try
                {
                    if (settings.Enabled && !locked)
                    {
                        scanner = scanner ?? new Scanner();
                        ScanResult result = scanner.ScanOnce();
                        claudeWindows = result.ClaudeWindows;
                        scanError = null;
                        if (result.Clicked != Kind.None)
                        {
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
                    scanner = null;
                    wait = 3000;
                }
                Thread.Sleep(wait);
            }
        }

        void Refresh()
        {
            bool on = settings.Enabled;
            string state = !on ? "已关闭" : locked ? "已暂停（锁屏）" : claudeWindows == 0 ? "已开启 · 未找到 Claude" : scanError != null ? "已开启 · 重试中" : "已开启 · 监视中";
            statusItem.Text = "状态：" + state;
            string last = settings.LastAction;
            lastItem.Text = "上次：" + (string.IsNullOrEmpty(last) ? "暂无" : Shorten(last, 60));
            enabledItem.Checked = on;
            try { autostartItem.Checked = Autostart.Enabled; } catch (Exception) { autostartItem.Checked = false; }
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
            File.WriteAllText(path, report ?? "诊断超时 / Diagnostics timed out");
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
            updateItem.Enabled = false;
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
                updateItem.Text = "正在下载… 0%";
                var progress = new Progress<int>(p => updateItem.Text = "正在下载… " + p + "%");
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
                updateItem.Text = "检查更新…";
                updateItem.Enabled = true;
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
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
