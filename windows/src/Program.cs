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
            if (args.Length >= 4 && args[0] == "--self-test-process") return SelfTest.Process(args[1], int.Parse(args[2]), args[3]);
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
        public static int Process(string processName, int seconds, string output)
        {
            int code = 1;
            string log = "";
            RunMta(() =>
            {
                var scanner = new Scanner(name => string.Equals(name, processName, StringComparison.OrdinalIgnoreCase));
                var watch = Stopwatch.StartNew();
                ScanResult result = null;
                while (watch.Elapsed.TotalSeconds < seconds)
                {
                    result = scanner.ScanOnce();
                    if (result.Clicked != Kind.None) break;
                    Thread.Sleep(500);
                }
                code = result?.Clicked == Kind.Session ? 0 : result?.Clicked == Kind.Once ? 2 : result?.Clicked == Kind.LocalProject ? 3 : 1;
                log = $"clicked={result?.Clicked} label=\"{result?.Label}\" windows={result?.ClaudeWindows} buttons={result?.Buttons} after={watch.Elapsed.TotalSeconds:F1}s\n\n"
                      + scanner.Diagnose();
            });
            File.WriteAllText(output, log);
            return code;
        }

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
                string label = null;
                while (watch.Elapsed.TotalSeconds < seconds && label == null)
                {
                    label = scanner.ScanOnce(notes);
                    if (label == null) Thread.Sleep(500);
                }
                code = label != null ? 0 : 1;
                log = $"pressed=\"{label}\" after={watch.Elapsed.TotalSeconds:F1}s\n" + string.Join("\n", notes.Distinct()) + "\n\n" + scanner.Diagnose();
            });
            File.WriteAllText(output, log);
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
