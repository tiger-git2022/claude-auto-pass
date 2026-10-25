using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ClaudeAutoPass
{
    sealed class Release
    {
        public string Tag;
        public string Notes;
        public string HtmlUrl;
        public string ExeUrl;
        public string ExeDigest;   // "sha256:<hex>" from GitHub, when provided
        public string SumsUrl;     // SHA256SUMS.txt fallback
    }

    /// <summary>Checks GitHub for newer win-v*-tN releases and replaces this exe in place.</summary>
    static class Updater
    {
        static readonly HttpClient Http = CreateClient();

        static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ClaudeAutoPass-Windows/" + AppInfo.Version);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        /// <summary>Newest Windows release, or null when none is newer than this build.</summary>
        public static async Task<Release> CheckAsync()
        {
            // The website's release feed and tag list first: GitHub's API allows only 60 anonymous calls an hour per IP
            // address, shared by everyone behind the same proxy or VPN exit, so "403 rate limit exceeded" is common.
            // The API is the fallback, in case the website's feed is ever unavailable.
            Release best;
            try { best = await NewestFromWebsiteAsync().ConfigureAwait(false); }
            catch (Exception webError) when (webError is HttpRequestException || webError is TaskCanceledException || webError is System.Xml.XmlException)
            {
                try { best = await NewestFromApiAsync().ConfigureAwait(false); }
                catch (Exception apiError) when (apiError is HttpRequestException || apiError is TaskCanceledException)
                {
                    throw new HttpRequestException($"网页：{webError.Message}；GitHub 接口：{apiError.Message}");
                }
            }
            return best != null && Versioning.IsNewer(best.Tag, AppInfo.Version) ? best : null;
        }

        static async Task<string> GetAsync(string url, string accept)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (accept != null)
                {
                    request.Headers.Accept.Clear();
                    request.Headers.Accept.ParseAdd(accept);
                }
                using (HttpResponseMessage response = await Http.SendAsync(request).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        string reason = response.ReasonPhrase;
                        if ((int)response.StatusCode == 403 && body.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0) reason = "rate limit exceeded";
                        throw new HttpRequestException($"{(int)response.StatusCode} ({reason})");
                    }
                    return body;
                }
            }
        }

        /// <summary>Newest Windows release from the GitHub API (has the exe's SHA-256), or null.</summary>
        internal static async Task<Release> NewestFromApiAsync()
        {
            // Android and Windows releases share the list, so read every page instead of trusting the first one.
            var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            var releases = new List<Dictionary<string, object>>();
            for (int page = 1; page <= 20; page++)
            {
                string json = await GetAsync("https://api.github.com/repos/" + AppInfo.Repo + "/releases?per_page=100&page=" + page, null).ConfigureAwait(false);
                List<Dictionary<string, object>> batch = serializer.Deserialize<ArrayList>(json).OfType<Dictionary<string, object>>().ToList();
                releases.AddRange(batch);
                if (batch.Count < 100) break;
            }
            Release best = null;
            int[] bestVersion = null;
            foreach (Dictionary<string, object> r in releases)
            {
                if (True(r, "draft") || True(r, "prerelease")) continue;
                string tag = Str(r, "tag_name");
                int[] version = Versioning.Parse(tag, true);
                if (version == null || (bestVersion != null && Versioning.Compare(version, bestVersion) <= 0)) continue;
                var release = new Release { Tag = tag, Notes = Str(r, "body"), HtmlUrl = Str(r, "html_url") };
                if (r.TryGetValue("assets", out object assets) && assets is ArrayList list)
                {
                    foreach (Dictionary<string, object> a in list.OfType<Dictionary<string, object>>())
                    {
                        string name = Str(a, "name");
                        if (name == AppInfo.AssetName) { release.ExeUrl = Str(a, "browser_download_url"); release.ExeDigest = Str(a, "digest"); }
                        else if (name == "SHA256SUMS.txt") release.SumsUrl = Str(a, "browser_download_url");
                    }
                }
                if (release.ExeUrl == null) continue;
                best = release;
                bestVersion = version;
            }
            return best;
        }

        /// <summary>
        /// Newest complete Windows release from the website instead of the API: the release feed (newest 10 releases, with
        /// notes), or, if none of those is a complete Windows release, the repository's tag list. A release counts only once
        /// its SHA256SUMS.txt downloads and names the exe (a tag can exist before its release has files, or without one when
        /// a build failed); that checksum then verifies the download.
        /// </summary>
        internal static async Task<Release> NewestFromWebsiteAsync()
        {
            string site = "https://github.com/" + AppInfo.Repo;
            var notesByTag = new Dictionary<string, string>();
            string feed = await GetAsync(site + "/releases.atom", "application/atom+xml").ConfigureAwait(false);
            System.Xml.Linq.XNamespace atom = "http://www.w3.org/2005/Atom";
            foreach (System.Xml.Linq.XElement entry in System.Xml.Linq.XDocument.Parse(feed).Root.Elements(atom + "entry"))
            {
                string link = (string)entry.Element(atom + "link")?.Attribute("href") ?? "";
                int at = link.LastIndexOf("/releases/tag/", StringComparison.Ordinal);
                if (at < 0) continue;
                string tag = Uri.UnescapeDataString(link.Substring(at + "/releases/tag/".Length));
                if (Versioning.Parse(tag, true) != null) notesByTag[tag] = HtmlToText((string)entry.Element(atom + "content") ?? "");
            }
            Release best = await NewestCompleteAsync(site, notesByTag.Keys, notesByTag).ConfigureAwait(false);
            if (best != null) return best;

            // Nothing complete among the newest tags (say, several failed builds in a row): report it as a failure so
            // CheckAsync asks the API instead of saying "up to date".
            return await NewestFromTagsAsync(notesByTag).ConfigureAwait(false)
                ?? throw new HttpRequestException("网页上没找到带安装文件的 Windows 版本 / no complete Windows release on the website");
        }

        /// <summary>Newest complete Windows release from the repository's tag list (no notes unless given).</summary>
        internal static async Task<Release> NewestFromTagsAsync(Dictionary<string, string> notesByTag)
        {
            string site = "https://github.com/" + AppInfo.Repo;
            // Smart-HTTP ref advertisement: lines like "<sha> refs/tags/win-v1.2.3-t1".
            string refs = await GetAsync(site + ".git/info/refs?service=git-upload-pack", null).ConfigureAwait(false);
            var tags = System.Text.RegularExpressions.Regex.Matches(refs, @"refs/tags/(win-v[0-9.]+-t[0-9]+)(?![0-9^])")
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value);
            return await NewestCompleteAsync(site, tags, notesByTag ?? new Dictionary<string, string>()).ConfigureAwait(false);
        }

        /// <summary>Tries the newest few tags in turn and returns the first whose checksum file names the exe.</summary>
        static async Task<Release> NewestCompleteAsync(string site, IEnumerable<string> tags, Dictionary<string, string> notesByTag)
        {
            var newestFirst = tags.Distinct().Where(t => Versioning.Parse(t, true) != null)
                .OrderByDescending(t => t, Comparer<string>.Create((x, y) => Versioning.Compare(Versioning.Parse(x, true), Versioning.Parse(y, true))))
                .Take(5);
            foreach (string tag in newestFirst)
            {
                string escaped = Uri.EscapeDataString(tag);
                var release = new Release
                {
                    Tag = tag,
                    Notes = notesByTag.TryGetValue(tag, out string notes) ? notes : null,
                    HtmlUrl = site + "/releases/tag/" + escaped,
                    ExeUrl = site + "/releases/download/" + escaped + "/" + AppInfo.AssetName,
                    SumsUrl = site + "/releases/download/" + escaped + "/SHA256SUMS.txt",
                };
                string sums;
                try { sums = await GetAsync(release.SumsUrl, null).ConfigureAwait(false); }
                catch (HttpRequestException) { continue; } // no files (yet) for this tag
                string hex = ChecksumFor(sums);
                if (hex == null) continue;
                release.ExeDigest = "sha256:" + hex;
                return release;
            }
            return null;
        }

        static string ChecksumFor(string sums) =>
            sums.Split('\n').Select(l => l.Trim().Split(new[] { ' ', '*' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length == 2 && p[1] == AppInfo.AssetName).Select(p => p[0].ToLowerInvariant()).FirstOrDefault();

        internal static string GetForTest(string url) => GetAsync(url, null).GetAwaiter().GetResult();

        /// <summary>Plain text from the feed's HTML release notes.</summary>
        internal static string HtmlToText(string html)
        {
            string text = System.Text.RegularExpressions.Regex.Replace(html, @"<li[^>]*>", "- ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<br\s*/?>|</(p|li|h[1-6]|ul|ol|div)>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
            text = WebUtility.HtmlDecode(text);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t]+\n", "\n");
            return System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
        }

        static bool True(Dictionary<string, object> d, string key) => d.TryGetValue(key, out object v) && v is bool b && b;
        static string Str(Dictionary<string, object> d, string key) => d.TryGetValue(key, out object v) ? v as string : null;

        static void RequireRepoUrl(string url)
        {
            var uri = new Uri(url);
            if (uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/" + AppInfo.Repo + "/releases/download/", StringComparison.Ordinal))
                throw new InvalidDataException("更新地址不属于项目仓库 / Update URL is outside the project repository");
        }

        /// <summary>Downloads and verifies the new exe, returning its temporary path.</summary>
        public static async Task<string> DownloadAsync(Release release, IProgress<int> progress)
        {
            RequireRepoUrl(release.ExeUrl);
            string expected = release.ExeDigest != null && release.ExeDigest.StartsWith("sha256:", StringComparison.Ordinal)
                ? release.ExeDigest.Substring(7).ToLowerInvariant() : null;
            if (expected == null && release.SumsUrl != null)
            {
                RequireRepoUrl(release.SumsUrl);
                string sums = await Http.GetStringAsync(release.SumsUrl).ConfigureAwait(false);
                expected = ChecksumFor(sums);
            }
            if (expected == null) throw new InvalidDataException("缺少 SHA-256 校验值 / Missing SHA-256 checksum");

            string target = Path.Combine(Path.GetTempPath(), "claude-auto-pass-" + release.Tag + ".exe");
            using (HttpResponseMessage response = await Http.GetAsync(release.ExeUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? -1;
                using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (FileStream output = File.Create(target))
                {
                    var buffer = new byte[81920];
                    long read = 0;
                    int n;
                    while ((n = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, n).ConfigureAwait(false);
                        read += n;
                        if (total > 0) progress?.Report((int)(read * 100 / total));
                    }
                }
            }
            string actual;
            using (var sha = SHA256.Create())
            using (FileStream file = File.OpenRead(target))
                actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            if (actual != expected)
            {
                File.Delete(target);
                throw new InvalidDataException("SHA-256 校验失败 / SHA-256 mismatch");
            }
            return target;
        }

        /// <summary>Swaps the running exe for the downloaded one and starts it. Windows allows renaming a running exe.</summary>
        public static void Install(string downloaded)
        {
            string current = System.Windows.Forms.Application.ExecutablePath;
            string old = current + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(current, old);
            try { File.Move(downloaded, current); }
            catch
            {
                File.Move(old, current);
                throw;
            }
            Process.Start(new ProcessStartInfo(current, "--updated") { UseShellExecute = false });
        }

        /// <summary>Removes the previous exe left behind by an update.</summary>
        public static void CleanUp()
        {
            string old = System.Windows.Forms.Application.ExecutablePath + ".old";
            for (int i = 0; i < 20 && File.Exists(old); i++)
            {
                try { File.Delete(old); }
                catch (IOException) { System.Threading.Thread.Sleep(250); }
                catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(250); }
            }
        }
    }
}
