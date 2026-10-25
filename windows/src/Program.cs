using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeAutoPass
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            EmbeddedAssemblies.Register();
            // Self-tests write crashes next to their output so CI can print them; the tray app uses %AppData%.
            string crashLog = args.Length >= 2 && args[0].StartsWith("--self-test", StringComparison.Ordinal)
                ? args[args.Length - 1] + ".crash.txt" : null;
            AppDomain.CurrentDomain.UnhandledException += (s, e) => WriteCrash(crashLog, e.ExceptionObject as Exception);
            Application.ThreadException += (s, e) => WriteCrash(crashLog, e.Exception);
            if (args.Length >= 2 && args[0] == "--self-test-form") return SelfTest.Form(args[1]);
            if (args.Length >= 2 && args[0] == "--self-test-menu") return SelfTest.Menu(args[1]);
            if (args.Length >= 4 && args[0] == "--self-test-process") return SelfTest.Process(args[1], int.Parse(args[2]), args[3], args.Length >= 5 && args[4] == "--hide");
            if (args.Length >= 2 && args[0] == "--self-test-taskbar") return SelfTest.Taskbar(args[1]);
            if (args.Length >= 3 && args[0] == "--self-test-hider") return SelfTest.Hider(args[1], args[2]);
            if (args.Length >= 2 && args[0] == "--self-test-update") return SelfTest.Update(args[1]);
            if (args.Length >= 4 && args[0] == "--self-test-steam") return SelfTest.Steam(args[1], int.Parse(args[2]), args[3]);

            bool updated = args.Contains("--updated");
            using (var mutex = new Mutex(false, @"Local\ClaudeAutoPass"))
            {
                bool owned;
                try { owned = mutex.WaitOne(updated ? TimeSpan.FromSeconds(15) : TimeSpan.Zero); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned)
                {
                    if (!args.Contains("--autostart"))
                        MessageBox.Show("Claude 自动审批已在运行，请在右下角托盘找到它。", "Claude 自动审批", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                try
                {
                    Updater.CleanUp();
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new TrayApp());
                }
                finally { mutex.ReleaseMutex(); }
            }
            return 0;
        }

        static void WriteCrash(string path, Exception ex)
        {
            try
            {
                path = path ?? Path.Combine(AppInfo.DataDir, "crash.txt");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + AppInfo.Version + "\r\n" + ex + "\r\n\r\n");
            }
            catch (Exception) { }
        }
    }

    /// <summary>Checks used by CI on a real Windows desktop; they exercise the same Scanner as the tray app.</summary>
    static class SelfTest
    {
        /// <summary>Scans windows of another process (a Chromium browser showing a test page) until it presses a button.</summary>
        public static int Process(string processName, int seconds, string output, bool hide = false)
        {
            int code = 1;
            string log = "";
            RunMta(() =>
            {
                var scanner = new Scanner(name => string.Equals(name, processName, StringComparison.OrdinalIgnoreCase));
                var watch = Stopwatch.StartNew();
                ScanResult result = null;
                // "把 Claude 藏到屏幕外": the page must keep updating off-screen.
                int hidden = hide ? ClaudeHider.Hide(scanner.TargetWindows()) : 0;
                while (watch.Elapsed.TotalSeconds < seconds)
                {
                    result = scanner.ScanOnce();
                    if (result.Clicked != Kind.None) break;
                    Thread.Sleep(500);
                }
                code = result?.Clicked == Kind.Session ? 0 : result?.Clicked == Kind.Once ? 2 : result?.Clicked == Kind.LocalProject ? 3 : 1;
                log = $"clicked={result?.Clicked} label=\"{result?.Label}\" windows={result?.ClaudeWindows} buttons={result?.Buttons} after={watch.Elapsed.TotalSeconds:F1}s hidden={hidden}\n\n"
                      + scanner.Diagnose();
            });
            File.WriteAllText(output, log);
            return code;
        }

        /// <summary>
        /// "把 Claude 藏到屏幕外" and the taskbar, on a browser window: a click on the button of a hidden Claude that is the active
        /// window (Windows minimizes it) brings it back on-screen; minimized together with other windows (Win+M) it stays
        /// minimized but opens on-screen; minimizing another program leaves it hidden.
        /// </summary>
        public static int Hider(string processName, string output)
        {
            var log = new System.Text.StringBuilder();
            int code = 1;
            var ids = new System.Collections.Generic.HashSet<int>(System.Diagnostics.Process.GetProcessesByName(processName).Select(p => p.Id));
            var form = new Form { Text = "hider self-test", Width = 320, Height = 200, StartPosition = FormStartPosition.Manual, Location = new Point(60, 60) };
            ClaudeHider.Start(note => log.AppendLine("  note: " + note));
            form.Shown += async (s, e) =>
            {
                try
                {
                    Native.Window window = Native.TopLevelWindows(ids).FirstOrDefault(w => w.Visible && w.Title.Length > 0);
                    if (window == null) { log.AppendLine("no window"); return; }
                    IntPtr h = window.Handle;
                    bool onScreen = false, iconic = false;
                    void Check(string step) => log.AppendLine(step + ": " + ClaudeHider.Describe(h, out onScreen, out iconic));

                    // 1. Taskbar click while the hidden window is the active one: Windows sends it "minimize".
                    log.AppendLine("hid " + ClaudeHider.Hide(new[] { window }));
                    await System.Threading.Tasks.Task.Delay(2500);
                    Check("hidden");
                    bool wasAway = !onScreen;
                    PostMessage(h, 0x0112, new IntPtr(0xF020), IntPtr.Zero); // WM_SYSCOMMAND, SC_MINIMIZE
                    await System.Threading.Tasks.Task.Delay(1500);
                    Check("taskbar click");
                    bool clickOk = wasAway && onScreen && !iconic && !ClaudeHider.Any;

                    // 2. Minimizing another program leaves Claude hidden (informational: with no other window on the desktop,
                    // Windows may still hand the focus to Claude).
                    ClaudeHider.Hide(new[] { window });
                    await System.Threading.Tasks.Task.Delay(2500);
                    form.WindowState = FormWindowState.Minimized;
                    await System.Threading.Tasks.Task.Delay(1500);
                    Check("other minimized (informational)");
                    form.WindowState = FormWindowState.Normal;
                    ClaudeHider.ShowAll();
                    await System.Threading.Tasks.Task.Delay(1000);

                    // 3. Win+M: minimized together with another window, it stays minimized at its old place...
                    ClaudeHider.Hide(new[] { window });
                    await System.Threading.Tasks.Task.Delay(2500);
                    ShowWindow(h, 6);
                    form.WindowState = FormWindowState.Minimized;
                    await System.Threading.Tasks.Task.Delay(1500);
                    Check("minimized with others");
                    bool togetherOk = onScreen && iconic && !ClaudeHider.Any;
                    // ...so the next taskbar click opens it on-screen.
                    ShowWindow(h, 9); // SW_RESTORE
                    await System.Threading.Tasks.Task.Delay(1000);
                    Check("restored");
                    bool restoreOk = onScreen && !iconic;

                    log.AppendLine($"click={clickOk} together={togetherOk} restore={restoreOk}");
                    code = clickOk && togetherOk && restoreOk ? 0 : 1;
                }
                catch (Exception ex) { log.AppendLine(ex.ToString()); }
                finally
                {
                    ClaudeHider.ShowAll();
                    ClaudeHider.Stop();
                    form.Close();
                }
            };
            Application.Run(form);
            File.WriteAllText(output, log.ToString());
            return code;
        }

        /// <summary>
        /// "单击任务栏打开 Claude", with this process standing in for Claude: another window of it shown (Claude's quick entry
        /// after a taskbar click) while the main window is minimized restores the main window; not right after minimizing, and
        /// not when switched off.
        /// </summary>
        public static int Taskbar(string output)
        {
            var log = new System.Text.StringBuilder();
            int code = 1;
            int self = System.Diagnostics.Process.GetCurrentProcess().Id;
            bool enabled = true;
            var main = new Form { Text = "Claude", Width = 640, Height = 420, StartPosition = FormStartPosition.Manual, Location = new Point(80, 80) };
            // Like Claude's quick entry: always on top, not resizable.
            var quick = new Form { Text = "Quick entry", Width = 360, Height = 90, StartPosition = FormStartPosition.Manual, Location = new Point(400, 600), ShowInTaskbar = false, TopMost = true, FormBorderStyle = FormBorderStyle.FixedSingle };
            ClaudeTaskbar.Start(() => enabled, note => log.AppendLine("  note: " + note), pid => pid == self, chromiumWindowsOnly: false);
            main.Shown += async (s, e) =>
            {
                try
                {
                    // hide: closed to the tray (Claude's X), the window is hidden rather than minimized.
                    async System.Threading.Tasks.Task<bool> Case(string name, int waitBeforeShow, bool on, bool hide = false)
                    {
                        enabled = on;
                        quick.Hide();
                        if (hide) ShowWindow(main.Handle, 0);
                        else main.WindowState = FormWindowState.Minimized;
                        await System.Threading.Tasks.Task.Delay(waitBeforeShow);
                        quick.Show();
                        await System.Threading.Tasks.Task.Delay(1500);
                        bool opened = IsWindowVisible(main.Handle) && main.WindowState != FormWindowState.Minimized;
                        log.AppendLine($"{name}: opened={opened}");
                        ShowWindow(main.Handle, 5);
                        main.WindowState = FormWindowState.Normal;
                        await System.Threading.Tasks.Task.Delay(1000);
                        return opened;
                    }
                    bool click = await Case("click while minimized", 2500, true);
                    bool justMinimized = await Case("right after minimizing", 150, true);
                    bool off = await Case("switched off", 2500, false);
                    bool hidden = await Case("click while closed to the tray", 2500, true, hide: true);
                    bool justHidden = await Case("right after closing to the tray", 150, true, hide: true);
                    log.AppendLine($"click={click} justMinimized={justMinimized} off={off} hidden={hidden} justHidden={justHidden}");
                    code = click && !justMinimized && !off && hidden && !justHidden ? 0 : 1;
                }
                catch (Exception ex) { log.AppendLine(ex.ToString()); }
                finally
                {
                    ClaudeTaskbar.Stop();
                    quick.Close();
                    main.Close();
                }
            };
            Application.Run(main);
            File.WriteAllText(output, log.ToString());
            return code;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);

        /// <summary>Scans a browser showing a mock Steam Workshop gate until it presses "查看物品" (exit 0).</summary>
        public static int Steam(string processName, int seconds, string output)
        {
            int code = 1;
            string log = "";
            RunMta(() =>
            {
                var scanner = new SteamGateScanner(name => string.Equals(name, processName, StringComparison.OrdinalIgnoreCase));
                var notes = new System.Collections.Generic.List<string>();
                var watch = Stopwatch.StartNew();
                string label = null, via = null;
                int fronts = 0;
                while (watch.Elapsed.TotalSeconds < seconds && label == null)
                {
                    // The tray app's way first (only the window in front); the whole-desktop scan if the page isn't in front.
                    label = scanner.ScanIfDue(notes, out bool scanned);
                    if (scanned) fronts++;
                    if (label != null) { via = "front"; break; }
                    if (!scanned) { label = scanner.ScanOnce(notes); if (label != null) via = "all"; }
                    if (label == null) Thread.Sleep(300);
                }
                code = label != null ? 0 : 1;
                log = $"pressed=\"{label}\" via={via} frontScans={fronts} after={watch.Elapsed.TotalSeconds:F1}s\n" + string.Join("\n", notes.Distinct()) + "\n\n" + scanner.Diagnose();
            });
            File.WriteAllText(output, log);
            return code;
        }

        /// <summary>Finds the newest Windows release through the website (no API rate limit) and checks its checksum file downloads.</summary>
        public static int Update(string output)
        {
            var log = new System.Text.StringBuilder();
            int code = 1;
            try
            {
                Release web = Updater.NewestFromWebsiteAsync().GetAwaiter().GetResult();
                log.AppendLine($"website: tag={web?.Tag} exe={web?.ExeUrl}");
                log.AppendLine("notes: " + (web?.Notes ?? "").Replace("\n", " | "));
                string sums = null;
                if (web != null)
                {
                    sums = Updater.GetForTest(web.SumsUrl);
                    log.AppendLine("sums: " + sums.Trim());
                }
                Release tags = Updater.NewestFromTagsAsync(null).GetAwaiter().GetResult();
                log.AppendLine($"tags: tag={tags?.Tag} digest={tags?.ExeDigest}");
                log.AppendLine($"website digest={web?.ExeDigest}");
                Release api = null;
                try { api = Updater.NewestFromApiAsync().GetAwaiter().GetResult(); log.AppendLine($"api: tag={api?.Tag}"); }
                catch (Exception ex) { log.AppendLine("api failed (allowed): " + ex.Message); }
                bool ok = web != null && Versioning.Parse(web.Tag, true) != null && sums != null && sums.Contains(AppInfo.AssetName)
                    && !string.IsNullOrEmpty(web.Notes) && web.ExeDigest != null
                    && tags != null && tags.Tag == web.Tag && tags.ExeDigest == web.ExeDigest
                    && (api == null || (api.Tag == web.Tag && (api.ExeDigest == null || api.ExeDigest == web.ExeDigest)));
                code = ok ? 0 : 1;
            }
            catch (Exception ex) { log.AppendLine("error: " + ex); }
            File.WriteAllText(output, log.ToString());
            return code;
        }

        /// <summary>Shows WinForms prompts in this process and checks which button the scanner presses.</summary>
        public static int Form(string output)
        {
            string self = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            var log = new System.Text.StringBuilder();
            string pressed = null;
            var form = new Form { Text = "Claude Auto Pass self-test", Width = 640, Height = 320, StartPosition = FormStartPosition.CenterScreen };
            var prompt = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 60 };
            var other = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60 };
            foreach (string text in new[] { "拒绝", "允许一次", "本次会话中始终允许" })
                prompt.Controls.Add(Button(text, () => pressed = text));
            other.Controls.Add(Button("始终允许", () => pressed = "始终允许"));
            form.Controls.Add(prompt);
            form.Controls.Add(other);

            var isolated = new Form { Text = "Claude Auto Pass self-test 2", Width = 400, Height = 200, StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) };
            var lone = new FlowLayoutPanel { Dock = DockStyle.Fill };
            lone.Controls.Add(Button("允许", () => pressed = "允许 (isolated)"));
            lone.Controls.Add(Button("继续", () => pressed = "继续"));
            isolated.Controls.Add(lone);

            int code = 1;
            form.Shown += (s, e) =>
            {
                var thread = new Thread(() =>
                {
                    var scanner = new Scanner(name => name == self);
                    // 1. A lone "允许" without a deny or second approval choice must be ignored.
                    form.Invoke((Action)(() => { form.Hide(); isolated.Show(); }));
                    for (int i = 0; i < 6; i++) { scanner.ScanOnce(); Thread.Sleep(300); }
                    log.AppendLine("isolated: pressed=" + (pressed ?? "none"));
                    bool isolatedOk = pressed == null;
                    // 2. A real prompt: session-wide approval wins over allow once; "始终允许" is never chosen.
                    form.Invoke((Action)(() => { isolated.Hide(); form.Show(); }));
                    ScanResult result = null;
                    for (int i = 0; i < 40 && pressed == null; i++) { result = scanner.ScanOnce(); Thread.Sleep(250); }
                    Thread.Sleep(500);
                    log.AppendLine($"prompt: pressed={pressed ?? "none"} result={result?.Clicked} buttons={result?.Buttons}");
                    log.AppendLine();
                    log.AppendLine(scanner.Diagnose());
                    code = isolatedOk && pressed == "本次会话中始终允许" ? 0 : 1;
                    form.BeginInvoke((Action)(() => { isolated.Close(); form.Close(); }));
                });
                thread.SetApartmentState(ApartmentState.MTA);
                thread.IsBackground = true;
                thread.Start();
            };
            Application.Run(form);
            File.WriteAllText(output, log.ToString());
            return code;
        }

        /// <summary>Opens the tray menu in light and dark theme and saves screenshots, so CI shows how it looks.</summary>
        public static int Menu(string outputPrefix)
        {
            Application.EnableVisualStyles();
            int code = 1;
            var log = new System.Text.StringBuilder();
            var context = new ApplicationContext();
            var timer = new System.Windows.Forms.Timer { Interval = 1500 };
            TrayMenu.InitTheme();
            var menu = new TrayMenu();
            var entries = SampleEntries();
            int step = 0, shots = 0;
            Point at = new Point(40, 620);
            timer.Tick += (s, e) =>
            {
                try
                {
                    if (step == 0 || step == 2)
                    {
                        TrayMenu.UseTheme(dark: step == 2);
                        menu.Show(entries, at);
                        // The dark pass also checks that a window made too tall (as WPF did at 150%) shrinks back.
                        if (step == 2) menu.StretchForTest(60);
                    }
                    else
                    {
                        string name = step == 1 ? "light" : "dark";
                        string misfit = menu.CheckFit();
                        log.Append(name + ": open=" + menu.IsOpen + " fit=" + (misfit ?? "ok") + "\n" + menu.Layout);
                        using (var bmp = new Bitmap(460, 600))
                        {
                            CaptureScreen(bmp, at.X - 20, at.Y - 580);
                            bmp.Save(outputPrefix + "-" + name + ".png", System.Drawing.Imaging.ImageFormat.Png);
                        }
                        if (menu.IsOpen && misfit == null) shots++;
                        menu.Close();
                    }
                    step++;
                    if (step == 4)
                    {
                        timer.Stop();
                        code = shots == 2 ? 0 : 1;
                        context.ExitThread();
                    }
                }
                catch (Exception ex)
                {
                    log.AppendLine(ex.ToString());
                    timer.Stop();
                    context.ExitThread();
                }
            };
            timer.Start();
            Application.Run(context);
            File.WriteAllText(outputPrefix + ".txt", log.ToString());
            return code;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int rop);

        /// <summary>Screen copy that includes layered and DWM-composited windows (SRCCOPY | CAPTUREBLT).</summary>
        static void CaptureScreen(Bitmap bmp, int x, int y)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            try
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try { BitBlt(hdc, 0, 0, bmp.Width, bmp.Height, screen, x, y, 0x00CC0020 | 0x40000000); }
                    finally { g.ReleaseHdc(hdc); }
                }
            }
            finally { ReleaseDC(IntPtr.Zero, screen); }
        }

        static System.Collections.Generic.List<TrayMenu.Entry> SampleEntries()
        {
            Action none = () => { };
            return new System.Collections.Generic.List<TrayMenu.Entry>
            {
                new TrayMenu.Entry { Text = "状态：已开启 · 监视中", Icon = Wpf.Ui.Controls.SymbolRegular.ShieldCheckmark24, Enabled = false },
                new TrayMenu.Entry { Text = "上次：本次会话中始终允许 · 19:30", Icon = Wpf.Ui.Controls.SymbolRegular.History24, Enabled = false },
                new TrayMenu.Entry { Separator = true },
                new TrayMenu.Entry { Text = "关闭自动审批", Bold = true, Icon = Wpf.Ui.Controls.SymbolRegular.Pause24, Click = none },
                new TrayMenu.Entry { Text = "自动确认 Steam 创意工坊年龄提示", Icon = Wpf.Ui.Controls.SymbolRegular.CheckboxChecked24, Click = none },
                new TrayMenu.Entry { Text = "把 Claude 藏到屏幕外（代替最小化）", Icon = Wpf.Ui.Controls.SymbolRegular.EyeOff24, Click = none },
                new TrayMenu.Entry { Text = "单击任务栏打开 Claude 主窗口", Icon = Wpf.Ui.Controls.SymbolRegular.CheckboxChecked24, Click = none },
                new TrayMenu.Entry { Text = "开机自动启动", Icon = Wpf.Ui.Controls.SymbolRegular.CheckboxChecked24, Click = none },
                new TrayMenu.Entry { Separator = true },
                new TrayMenu.Entry { Text = "导出按钮文字（诊断）…", Icon = Wpf.Ui.Controls.SymbolRegular.DocumentSearch24, Click = none },
                new TrayMenu.Entry { Text = "以辅助功能模式重启 Claude…", Icon = Wpf.Ui.Controls.SymbolRegular.ArrowClockwise24, Click = none },
                new TrayMenu.Entry { Text = "检查更新…", Icon = Wpf.Ui.Controls.SymbolRegular.ArrowSync24, Click = none },
                new TrayMenu.Entry { Text = "项目主页", Icon = Wpf.Ui.Controls.SymbolRegular.Open24, Click = none },
                new TrayMenu.Entry { Separator = true },
                new TrayMenu.Entry { Text = "退出", Icon = Wpf.Ui.Controls.SymbolRegular.Power24, Click = none },
            };
        }

        static Button Button(string text, Action onClick)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += (s, e) => onClick();
            return b;
        }

        static void RunMta(Action action)
        {
            Exception error = null;
            var t = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            t.Join();
            if (error != null) throw new Exception("Self-test failed", error);
        }
    }
}
