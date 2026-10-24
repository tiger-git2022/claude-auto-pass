using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeAutoPass
{
    /// <summary>
    /// Which switches Claude Desktop really runs with, for the diagnostics export. The main process shows what it was started
    /// with; renderer processes inherit the final feature list, so they show whether an app setting replaced our
    /// --disable-features (Chromium keeps only the last value of a repeated switch).
    /// </summary>
    static class ProcessArgs
    {
        public static string Describe(ISet<int> processIds)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Claude 启动参数 / command lines:");
            try
            {
                var lines = new List<KeyValuePair<int, string>>();
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process"))
                    foreach (ManagementObject o in searcher.Get())
                        using (o)
                        {
                            int pid = Convert.ToInt32(o["ProcessId"]);
                            if (processIds.Contains(pid)) lines.Add(new KeyValuePair<int, string>(pid, o["CommandLine"] as string ?? ""));
                        }
                foreach (var main in lines.Where(l => !l.Value.Contains("--type=")))
                    sb.AppendLine($"  主进程 / main {main.Key}: {Shorten(main.Value)}");
                foreach (var group in lines.Where(l => l.Value.Contains("--type=")).GroupBy(l => Switch(l.Value, "type")))
                {
                    var first = group.First();
                    sb.AppendLine($"  {group.Key} ×{group.Count()}: disable-features=\"{Switch(first.Value, "disable-features")}\""
                        + (first.Value.Contains("--force-renderer-accessibility") ? " force-renderer-accessibility" : ""));
                }
                bool occlusionOff = lines.Any(l => !l.Value.Contains("--type=") && l.Value.Contains("--disable-backgrounding-occluded-windows"))
                    || lines.Any(l => Switch(l.Value, "type") == "renderer" && Switch(l.Value, "disable-features").Contains("CalculateNativeWinOcclusion"));
                sb.AppendLine(occlusionOff ? "  遮挡检测已关闭 / occlusion tracking off"
                    : "  遮挡检测未关闭：Claude 被完全挡住或藏到屏幕外后不会更新，请用「以辅助功能模式重启 Claude」重启 / occlusion tracking on: a covered or off-screen Claude stops updating");
            }
            catch (Exception ex) { sb.AppendLine("  无法读取 / cannot read: " + ex.Message); }
            return sb.ToString();
        }

        static string Switch(string commandLine, string name)
        {
            // Chromium keeps the last occurrence of a repeated switch.
            MatchCollection all = Regex.Matches(commandLine, "--" + Regex.Escape(name) + "=(\"[^\"]*\"|[^ ]*)");
            return all.Count == 0 ? "" : all[all.Count - 1].Groups[1].Value.Trim('"');
        }

        static string Shorten(string s) => s.Length > 300 ? s.Substring(0, 300) + "…" : s;
    }
}
