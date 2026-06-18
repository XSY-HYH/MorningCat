using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MorningCatLaunchCore
{
    /// <summary>
    /// 下载源类型
    /// </summary>
    public enum DownloadSourceType
    {
        /// <summary>自动选择（延迟测试后决定）</summary>
        Auto,
        /// <summary>强制使用 GitHub</summary>
        GitHub,
        /// <summary>强制使用官方源</summary>
        Official
    }

    /// <summary>
    /// 下载源信息
    /// </summary>
    public class DownloadSource
    {
        public DownloadSourceType Type { get; init; }
        public string Name { get; init; } = "";
        public string TestUrl { get; init; } = "";
        public double LatencyMs { get; set; } = -1;
        public bool Available { get; set; }
        /// <summary>该源提供的最新版本号</summary>
        public string? LatestVersion { get; set; }
    }

    /// <summary>
    /// GitHub Release 信息
    /// </summary>
    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("prerelease")]
        public bool PreRelease { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    /// <summary>
    /// GitHub Release Asset
    /// </summary>
    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    /// <summary>
    /// 下载源提供者 - 管理链接列表、延迟测试、源选择
    /// </summary>
    public class DownloadSourceProvider
    {
        private const string GitHubRepo = "XSY-HYH/MorningCat";
        private const string GitHubApiBase = "https://api.github.com/repos";
        private const string GitHubDownloadBase = "https://github.com";

        private static readonly string OfficialServer = "https://110.42.98.47:59113";

        private readonly HttpClient _httpClient;
        private readonly List<DownloadSource> _sources;

        public DownloadSourceProvider(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _sources = new List<DownloadSource>
            {
                new DownloadSource
                {
                    Type = DownloadSourceType.GitHub,
                    Name = "GitHub",
                    TestUrl = "https://api.github.com",
                    Available = false
                },
                new DownloadSource
                {
                    Type = DownloadSourceType.Official,
                    Name = "Official",
                    TestUrl = OfficialServer,
                    Available = false
                }
            };
        }

        /// <summary>
        /// 并行测试所有源的延迟和版本，返回最优源
        /// 选择策略：版本更高的源优先；版本相同时选延迟更低的
        /// </summary>
        public async Task<DownloadSource> TestAndSelectBestAsync(DownloadSourceType forceSource = DownloadSourceType.Auto)
        {
            // 如果强制指定了源，直接返回
            if (forceSource != DownloadSourceType.Auto)
            {
                var forced = _sources.First(s => s.Type == forceSource);
                forced.LatencyMs = await TestLatencyAsync(forced.TestUrl);
                forced.Available = forced.LatencyMs > 0;
                forced.LatestVersion = await GetLatestVersionAsync(forced.Type);
                Console.WriteLine($"[MLC] 强制使用下载源: {forced.Name} (延迟: {FormatLatency(forced.LatencyMs)}, 版本: {forced.LatestVersion ?? "未知"})");
                return forced;
            }

            Console.WriteLine("[MLC] 正在测试下载源延迟和版本...");

            // 并行测试所有源的延迟和版本
            var tasks = _sources.Select(async source =>
            {
                source.LatencyMs = await TestLatencyAsync(source.TestUrl);
                source.Available = source.LatencyMs > 0;
                source.LatestVersion = await GetLatestVersionAsync(source.Type);
                Console.WriteLine($"[MLC] {source.Name}: 延迟 {FormatLatency(source.LatencyMs)}, 版本 {source.LatestVersion ?? "未知"}");
                return source;
            }).ToList();

            await Task.WhenAll(tasks);

            var available = _sources.Where(s => s.Available).ToList();

            if (available.Count == 0)
            {
                Console.WriteLine("[MLC] 所有下载源均不可用");
                return _sources[0];
            }

            // 比较版本：版本更高的源优先；版本相同时选延迟更低的
            var best = available
                .OrderByDescending(s => ParseVersion(s.LatestVersion))
                .ThenBy(s => s.LatencyMs)
                .First();

            Console.WriteLine($"[MLC] 选择下载源: {best.Name} (延迟: {FormatLatency(best.LatencyMs)}, 版本: {best.LatestVersion ?? "未知"})");
            return best;
        }

        /// <summary>
        /// 获取指定源的最新版本号
        /// GitHub: 从 Releases API 获取 tag
        /// 官方源: 从目录列表 API 获取版本目录名
        /// </summary>
        public async Task<string?> GetLatestVersionAsync(DownloadSourceType sourceType)
        {
            try
            {
                if (sourceType == DownloadSourceType.GitHub)
                    return await GetLatestGitHubVersionAsync();

                // 官方源：从 /api/files 获取 MorningCat/Core 目录下的版本目录
                var url = $"{OfficialServer}/api/files?path={Uri.EscapeDataString("MorningCat/Core")}";
                var request = CreateRequest(HttpMethod.Get, url);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _httpClient.SendAsync(request, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cts.Token);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("directories", out var dirs))
                    {
                        return dirs.EnumerateArray()
                            .Select(d => d.TryGetProperty("name", out var n) ? n.GetString() : null)
                            .Where(v => !string.IsNullOrEmpty(v))
                            .OrderByDescending(v => v, new VersionStringComparer())
                            .FirstOrDefault();
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取 GitHub 最新 Release 版本号
        /// </summary>
        public async Task<string?> GetLatestGitHubVersionAsync()
        {
            try
            {
                var url = $"{GitHubApiBase}/{GitHubRepo}/releases";
                var request = CreateRequest(HttpMethod.Get, url);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json);

                var latest = releases?
                    .Where(r => !r.Draft && !r.PreRelease)
                    .FirstOrDefault();

                if (latest != null)
                {
                    var version = latest.TagName.TrimStart('v');
                    Console.WriteLine($"[MLC] GitHub 最新版本: {version}");
                    return version;
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MLC] 获取 GitHub 版本失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取 GitHub Release 下载链接
        /// </summary>
        public string GetGitHubCoreDownloadUrl(string version)
        {
            return $"{GitHubDownloadBase}/{GitHubRepo}/releases/download/v{version}/MorningCat-Core-{version}.zip";
        }

        public string GetGitHubMlcDownloadUrl(string version)
        {
            return $"{GitHubDownloadBase}/{GitHubRepo}/releases/download/v{version}/MorningCatLaunchCore-{version}.zip";
        }

        /// <summary>
        /// 获取官方源基础 URL
        /// </summary>
        public string GetOfficialBaseUrl() => OfficialServer;

        private async Task<double> TestLatencyAsync(string url)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var request = CreateRequest(HttpMethod.Get, url);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                sw.Stop();

                if (response.IsSuccessStatusCode || (int)response.StatusCode < 500)
                    return sw.Elapsed.TotalMilliseconds;

                return -1;
            }
            catch
            {
                return -1;
            }
        }

        private static HttpRequestMessage CreateRequest(HttpMethod method, string url) => LaunchCore.CreateRequest(method, url);

        private static string FormatLatency(double ms)
        {
            if (ms < 0) return "不可用";
            return $"{ms:F0}ms";
        }

        /// <summary>
        /// 将版本字符串解析为 Version 对象用于比较，解析失败返回 0.0.0
        /// </summary>
        private static Version ParseVersion(string? version)
        {
            if (string.IsNullOrEmpty(version)) return new Version(0, 0, 0);
            return Version.TryParse(version.TrimStart('v'), out var v) ? v : new Version(0, 0, 0);
        }

        /// <summary>
        /// 版本字符串比较器
        /// </summary>
        private class VersionStringComparer : IComparer<string?>
        {
            public int Compare(string? x, string? y)
            {
                return ParseVersion(x).CompareTo(ParseVersion(y));
            }
        }
    }
}
