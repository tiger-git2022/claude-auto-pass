using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;

namespace ClaudeAutoPass
{
    /// <summary>
    /// Claude Desktop's own (browser) process and whether it runs with <see cref="AppInfo.ClaudeSwitches"/>. Started the normal
    /// way (the Start menu, the taskbar, after an update) it doesn't, and then Chromium stops updating the page whenever the
    /// window is fully covered or the display is off: the tool keeps pressing the card it read last and never sees the next one
    /// until the owner brings Claude up and scrolls (owner's log, 11-16: one button pressed 7360 times in three hours).
    /// </summary>
    static class ClaudeLaunch
    {
        /// <summary>The switch that keeps a covered window updating; its presence means Claude was started with our switches.</summary>
        public const string Marker = "--disable-backgrounding-occluded-windows";

        public sealed class Main
        {
            public int Pid;
            public string Exe;
            public string CommandLine;
            public DateTime Started;
            public bool HasSwitches => CommandLine != null && CommandLine.Contains(Marker);
            public double AgeSeconds => (DateTime.Now - Started).TotalSeconds;
        }

        /// <summary>Claude Desktop's windows (Claude Code's own claude.exe processes have none).</summary>
        public static List<Native.Window> Windows()
        {
            Func<string, bool> isTarget = ApprovalRules.IsClaudeProcess;
            var ids = new HashSet<int>();
            foreach (Process p in Process.GetProcesses())
            {
                try { if (isTarget(p.ProcessName)) ids.Add(p.Id); }
                catch (InvalidOperationException) { }
                finally { p.Dispose(); }
            }
            return ids.Count == 0 ? new List<Native.Window>() : Native.TopLevelWindows(ids);
        }

        /// <summary>The process that owns Claude's titled Chromium windows (the browser process), or null.</summary>
        public static Main Find(IEnumerable<Native.Window> windows)
        {
            int pid = MainPid(windows);
            if (pid == 0) return null;
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ExecutablePath, CommandLine, CreationDate FROM Win32_Process WHERE ProcessId=" + pid))
                    foreach (ManagementObject o in searcher.Get())
                        using (o)
                        {
                            string created = o["CreationDate"] as string;
                            return new Main
                            {
                                Pid = pid,
                                Exe = o["ExecutablePath"] as string,
                                CommandLine = o["CommandLine"] as string ?? "",
                                Started = string.IsNullOrEmpty(created) ? DateTime.MinValue : ManagementDateTimeConverter.ToDateTime(created),
                            };
                        }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>The owner of most titled Chromium windows (0 when none).</summary>
        public static int MainPid(IEnumerable<Native.Window> windows) =>
            windows.Where(w => w.ClassName.StartsWith("Chrome_WidgetWin_1", StringComparison.Ordinal) && w.Title.Length > 0)
                .GroupBy(w => w.ProcessId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

        /// <summary>
        /// Closes every process of Claude Desktop's own exe and starts it again with our switches. Claude Code's claude.exe
        /// (another exe, also in a terminal) is left alone: the ones Claude Desktop ran stop with it.
        /// </summary>
        public static void Restart(Main main)
        {
            var pids = new List<int> { main.Pid };
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, ExecutablePath FROM Win32_Process"))
                    foreach (ManagementObject o in searcher.Get())
                        using (o)
                        {
                            int pid = Convert.ToInt32(o["ProcessId"]);
                            if (pid != main.Pid && string.Equals(o["ExecutablePath"] as string, main.Exe, StringComparison.OrdinalIgnoreCase)) pids.Add(pid);
                        }
            }
            catch (Exception) { }
            foreach (int pid in pids)
            {
                try { using (Process p = Process.GetProcessById(pid)) { p.Kill(); p.WaitForExit(5000); } }
                catch (Exception) { }
            }
            Thread.Sleep(500);
            Process.Start(new ProcessStartInfo(main.Exe, LaunchArgs.Relaunch(main.CommandLine, AppInfo.ClaudeSwitches)) { UseShellExecute = false })?.Dispose();
        }
    }

    /// <summary>What <see cref="SwitchWatch.Tick"/> found.</summary>
    enum SwitchState { Unchanged, Ok, Restarted, Missing, RestartFailed }

    /// <summary>
    /// Watches Claude Desktop's main process: when it was just started without our switches (Claude was opened the normal way
    /// or updated), it is restarted with them at once, before there is anything to lose. An older one is only reported.
    /// </summary>
    sealed class SwitchWatch
    {
        /// <summary>A Claude this young is restarted without asking: it has only just opened.</summary>
        public double YoungSeconds = 90;
        /// <summary>Never restart more often than this, so a launch that loses the switches can't loop.</summary>
        public double MinRestartGapSeconds = 300;

        int lastPid;
        DateTime lastRestart = DateTime.MinValue;

        /// <summary>The main process last looked at (null before one was found).</summary>
        public ClaudeLaunch.Main Current { get; private set; }

        /// <summary>False while Claude runs without our switches (a covered Claude then stops updating).</summary>
        public bool UpdatesWhenCovered => Current == null || Current.HasSwitches;

        public SwitchState Tick(List<Native.Window> windows, bool autoRestart, out string note)
        {
            note = null;
            int pid = ClaudeLaunch.MainPid(windows);
            if (pid == 0 || pid == lastPid) return SwitchState.Unchanged;
            ClaudeLaunch.Main main = ClaudeLaunch.Find(windows);
            if (main == null) return SwitchState.Unchanged;
            lastPid = main.Pid;
            Current = main;
            if (main.HasSwitches)
            {
                note = $"Claude 已带辅助功能参数 / Claude runs with the accessibility switches: pid {main.Pid}";
                return SwitchState.Ok;
            }
            bool restartedLately = (DateTime.UtcNow - lastRestart).TotalSeconds < MinRestartGapSeconds;
            string what = $"pid {main.Pid}，启动 {main.AgeSeconds:F0} 秒 / started {main.AgeSeconds:F0} s ago";
            if (restartedLately)
            {
                note = $"重启后 Claude 仍没带参数，不再自动重启 / still no switches after the restart, not retrying: {what}";
                return SwitchState.RestartFailed;
            }
            if (!autoRestart || main.AgeSeconds > YoungSeconds || string.IsNullOrEmpty(main.Exe))
            {
                note = $"Claude 没带辅助功能参数，被挡住或关屏后不会更新 / Claude runs without the switches: {what}";
                return SwitchState.Missing;
            }
            try
            {
                lastRestart = DateTime.UtcNow;
                ClaudeLaunch.Restart(main);
                lastPid = 0;
                note = $"Claude 刚以普通方式打开，已自动以辅助功能模式重启 / restarted Claude with the accessibility switches: {what}";
                return SwitchState.Restarted;
            }
            catch (Exception ex)
            {
                note = $"自动重启 Claude 失败 / restarting Claude failed: {ex.Message} ({what})";
                return SwitchState.RestartFailed;
            }
        }
    }
}
