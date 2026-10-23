using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace ClaudeAutoPass
{
    static class AppInfo
    {
        public const string Repo = "tiger-git2022/claude-auto-pass";
        public const string ReleasesUrl = "https://github.com/" + Repo + "/releases";
        public const string AssetName = "claude-auto-pass.exe";

        /// <summary>"1.0.0-t5" for CI builds, "1.0.0-t0" for local builds.</summary>
        public static string Version =>
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0-t0";

        public static string DataDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeAutoPass");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
    }

    /// <summary>Tiny key=value store in %AppData%\ClaudeAutoPass\settings.txt.</summary>
    sealed class Settings
    {
        readonly string path = Path.Combine(AppInfo.DataDir, "settings.txt");
        readonly Dictionary<string, string> values = new Dictionary<string, string>();
        readonly object gate = new object();

        public Settings()
        {
            try
            {
                if (!File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) values[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch (IOException) { }
        }

        public bool FirstRun => !values.ContainsKey("enabled");

        public bool Enabled
        {
            get => Get("enabled") == "1";
            set => Set("enabled", value ? "1" : "0");
        }

        public string LastAction
        {
            get => Get("last_action");
            set => Set("last_action", value);
        }

        string Get(string key)
        {
            lock (gate) return values.TryGetValue(key, out string v) ? v : "";
        }

        void Set(string key, string value)
        {
            lock (gate)
            {
                values[key] = (value ?? "").Replace('\n', ' ').Replace('\r', ' ');
                try
                {
                    var lines = new List<string>();
                    foreach (var kv in values) lines.Add(kv.Key + "=" + kv.Value);
                    File.WriteAllLines(path, lines);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Start with Windows through the per-user Run key (no admin rights needed).</summary>
    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "ClaudeAutoPass";

        static string Command => "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --autostart";

        public static bool Enabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return string.Equals(key?.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase);
            }
            set
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) key.SetValue(Name, Command);
                    else key.DeleteValue(Name, false);
                }
            }
        }

        /// <summary>Keeps an existing autostart entry pointing at this exe after it was moved or renamed.</summary>
        public static void Refresh()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key?.GetValue(Name) is string current && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
                    key.SetValue(Name, Command);
            }
        }

        public static string Stamp() => DateTime.Now.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}

namespace ClaudeAutoPass
{
    /// <summary>
    /// Background log in %AppData%\ClaudeAutoPass\scan-log.txt: clicks, approval buttons that were seen but not
    /// pressed (and why), slow scans and scan errors. Repeats of the same line are dropped for a minute.
    /// </summary>
    static class ScanLog
    {
        const long MaxBytes = 512 * 1024;
        static readonly System.Collections.Generic.Dictionary<string, DateTime> recent = new System.Collections.Generic.Dictionary<string, DateTime>();
        static readonly object gate = new object();

        public static string Path => System.IO.Path.Combine(AppInfo.DataDir, "scan-log.txt");

        public static void Write(string line, bool dedupe = true)
        {
            lock (gate)
            {
                DateTime now = DateTime.Now;
                if (dedupe)
                {
                    if (recent.TryGetValue(line, out DateTime last) && (now - last).TotalSeconds < 60) return;
                    recent[line] = now;
                    if (recent.Count > 200) recent.Clear();
                }
                try
                {
                    string path = Path;
                    var info = new System.IO.FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        string old = System.IO.Path.Combine(AppInfo.DataDir, "scan-log.old.txt");
                        System.IO.File.Delete(old);
                        System.IO.File.Move(path, old);
                    }
                    System.IO.File.AppendAllText(path, now.ToString("MM-dd HH:mm:ss") + " " + line + "\r\n");
                }
                catch (Exception) { }
            }
        }
    }
}
