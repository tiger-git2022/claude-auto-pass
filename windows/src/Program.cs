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
            if (args.Length >= 2 && args[0] == "--self-test-form") return SelfTest.Form(args[1]);
            if (args.Length >= 4 && args[0] == "--self-test-process") return SelfTest.Process(args[1], int.Parse(args[2]), args[3]);

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
                code = result?.Clicked == Kind.Session ? 0 : result?.Clicked == Kind.Once ? 2 : 1;
                log = $"clicked={result?.Clicked} label=\"{result?.Label}\" windows={result?.ClaudeWindows} buttons={result?.Buttons} after={watch.Elapsed.TotalSeconds:F1}s\n\n"
                      + scanner.Diagnose();
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
