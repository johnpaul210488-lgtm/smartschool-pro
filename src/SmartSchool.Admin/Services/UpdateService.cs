using System.Diagnostics;
using System.IO;
using System.Reflection;
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
        [JsonPropertyName("notes")] public string? Notes { get; set; }
        [JsonPropertyName("sizeHuman")] public string? SizeHuman { get; set; }
    }

    /// <summary>(HasUpdate, LatestVersion, Notes) — never throws.</summary>
    public async Task<(bool HasUpdate, string Latest, string Notes)> CheckAsync()
    {
        try
        {
            var (ok, data, _, _) = await ApiClient.Instance.GetAsync<AppUpdateInfo>("api/app-updates/admin");
            if (!ok || data?.LatestVersion is null)
                return (false, string.Empty, string.Empty);

            _latest = data.LatestVersion;
            return (IsNewer(data.LatestVersion, CurrentVersion), data.LatestVersion, data.Notes ?? "");
        }
        catch
        {
            return (false, string.Empty, string.Empty); // offline/server-old: silently skip
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
            var ok = await ApiClient.Instance.DownloadFileAsync("api/app-updates/admin/download", tempExe, progress);
            if (!ok || new FileInfo(tempExe).Length < 1_000_000) return false;

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
