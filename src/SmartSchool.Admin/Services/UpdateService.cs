using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartSchool.Admin.Services;

/// <summary>
/// Auto-update: asks the school server (GitHub Releases proxied by the API)
/// for the newest admin version; downloads and hot-swaps the exe on confirm.
/// The developer publishes updates by pushing a tag (vX.Y.Z) → CI → Release.
/// </summary>
public sealed class UpdateService
{
    public static readonly UpdateService Instance = new();

    private string? _latest;
    private string? _downloadUrl; // CHANGED: exact GitHub release asset URL for direct manual update

    /// <summary>Version stamped into the exe at publish time (from the git tag).</summary>
    public static string CurrentVersion
    {
        get
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
                return info.Split('+')[0]; // strip build metadata
            return asm.GetName().Version?.ToString() ?? "1.0.0";
        }
    }

    private sealed class AppUpdateInfo
    {
        [JsonPropertyName("app")] public string? App { get; set; }
        [JsonPropertyName("latestVersion")] public string? LatestVersion { get; set; }
        [JsonPropertyName("configured")] public bool Configured { get; set; }
        [JsonPropertyName("notes")] public string? Notes { get; set; }
        [JsonPropertyName("sizeHuman")] public string? SizeHuman { get; set; }
    }

    /// <summary>Manual refresh checks GitHub directly, bypassing any school-server release cache.</summary>
    public async Task<(bool CheckSucceeded, bool HasUpdate, string Latest, string Notes)> CheckAsync(bool forceRefresh = false)
    {
        try
        {
            if (forceRefresh)
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SmartSchoolUpdater", "1.0"));
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                using var response = await http.GetAsync("https://api.github.com/repos/johnpaul210488-lgtm/smartschool-pro/releases/latest");
                if (!response.IsSuccessStatusCode) return (false, false, string.Empty, string.Empty);

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
                var version = tag.TrimStart('v', 'V');
                var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;
                _downloadUrl = null;
                if (root.TryGetProperty("assets", out var assets))
                    foreach (var asset in assets.EnumerateArray())
                        if (asset.GetProperty("name").GetString() == "SmartSchool.Admin.exe")
                        {
                            _downloadUrl = asset.GetProperty("browser_download_url").GetString();
                            break;
                        }
                if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(_downloadUrl))
                    return (false, false, string.Empty, string.Empty);

                _latest = version;
                return (true, IsNewer(version, CurrentVersion), version, notes);
            }

            // Background check retains the school API feed, avoiding GitHub rate limits on the 5-minute loop.
            var (ok, data, _, _) = await ApiClient.Instance.GetAsync<AppUpdateInfo>("api/app-updates/admin");
            if (!ok || data is null || !data.Configured || data.LatestVersion is null)
                return (false, false, string.Empty, string.Empty);
            _downloadUrl = null;
            _latest = data.LatestVersion;
            return (true, IsNewer(data.LatestVersion, CurrentVersion), data.LatestVersion, data.Notes ?? string.Empty);
        }
        catch
        {
            return (false, false, string.Empty, string.Empty);
        }
    }

    /// <summary>Downloads the new exe and swaps it in on next start. Returns false on failure.</summary>
    public async Task<bool> ApplyAsync(IProgress<int> progress)
    {
        try
        {
            var currentExe = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Cannot resolve own exe path.");

            var tempExe = Path.Combine(Path.GetTempPath(), $"SmartSchool.Admin-{_latest ?? "new"}.exe");
            var ok = !string.IsNullOrWhiteSpace(_downloadUrl)
                ? await DownloadGitHubAssetAsync(_downloadUrl, tempExe, progress)
                : await ApiClient.Instance.DownloadFileAsync("api/app-updates/admin/download", tempExe, progress);
            if (!ok || !File.Exists(tempExe) || new FileInfo(tempExe).Length < 1_000_000) return false;

            // Tiny swapper: wait for exit → replace → relaunch. If Program Files
            // blocks the copy, retry ONCE with UAC elevation (single Yes-click).
            var script = Path.Combine(Path.GetTempPath(), "smartschool-admin-update.cmd");
            var elevatedCmd =
                $"powershell -NoProfile -Command \"Start-Process -Verb RunAs -Wait cmd.exe " +
                $"-ArgumentList '/c copy /y \\\"{tempExe}\\\" \\\"{currentExe}\\\" & start \\\"\\\" \\\"{currentExe}\\\"'\"";
            await File.WriteAllTextAsync(script,
                "@echo off\r\n" +
                "ping 127.0.0.1 -n 3 >nul\r\n" +
                $"copy /y \"{tempExe}\" \"{currentExe}\" >nul 2>&1\r\n" +
                $"if errorlevel 1 ({elevatedCmd}) else (start \"\" \"{currentExe}\")\r\n" +
                "del \"%~f0\"\r\n");

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });

            System.Windows.Application.Current?.Dispatcher.Invoke(
                () => System.Windows.Application.Current.Shutdown());
            return true;
        }
        catch
        {
            return false;
        }
    }

    // CHANGED: manual update asset is fetched from the exact GitHub release URL, not school API cache.
    private static async Task<bool> DownloadGitHubAssetAsync(string url, string path, IProgress<int> progress)
    {
        try
        {
            using var http = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.None
            }) { Timeout = TimeSpan.FromMinutes(20) };
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return false;

            var total = response.Content.Headers.ContentLength ?? -1;
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = File.Create(path);
            var buffer = new byte[128 * 1024];
            long received = 0;
            int count;
            while ((count = await source.ReadAsync(buffer)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, count));
                received += count;
                if (total > 0) progress.Report((int)(received * 100 / total));
            }
            progress.Report(100);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Loose SemVer compare: "1.10" &gt; "1.9", ignores suffixes.</summary>
    public static bool IsNewer(string latest, string current)
    {
        static int[] Parse(string v) => v.Split('+')[0].Split('-')[0]
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(new string(s.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0)
            .ToArray();

        var a = Parse(latest); var b = Parse(current);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }
}
