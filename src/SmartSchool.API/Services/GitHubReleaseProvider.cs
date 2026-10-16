using System.Net.Http.Headers;
using System.Text.Json;

namespace SmartSchool.API.Services;

/// <summary>One release asset of the latest GitHub release.</summary>
public sealed record ReleaseAsset(string Name, long SizeBytes);

/// <summary>Cached view of the repo's latest GitHub release.</summary>
public sealed record ReleaseInfo(string Tag, string Version, string Notes, DateTime PublishedAtUtc,
    IReadOnlyList<ReleaseAsset> Assets);

/// <summary>
/// Fetches "who is the newest release?" from GitHub Releases and caches it for
/// a while, so each school server acts as the update feed for its own clients
/// (clients never need GitHub access or tokens).
/// </summary>
public sealed class GitHubReleaseProvider
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<GitHubReleaseProvider> _logger;
    private readonly string _repo;
    private readonly string? _token;
    private readonly TimeSpan _cacheTtl;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private ReleaseInfo? _cache;
    private DateTime _cacheAtUtc = DateTime.MinValue;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_repo);

    public GitHubReleaseProvider(IConfiguration config, IHttpClientFactory httpFactory,
        ILogger<GitHubReleaseProvider> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _repo = config["Updates:Repo"] ?? string.Empty;
        var token = config["Updates:GitHubToken"];
        _token = string.IsNullOrWhiteSpace(token) ? null : token;
        _cacheTtl = TimeSpan.FromMinutes(Math.Max(1, config.GetValue("Updates:CacheMinutes", 30)));
    }

    private HttpClient CreateClient()
    {
        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SmartSchoolUpdater", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (_token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return client;
    }

    /// <summary>Latest release info (cached). Null if Updates:Repo is empty or GitHub is unreachable.</summary>
    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct)
    {
        if (!IsConfigured) return null;
        if (_cache is not null && DateTime.UtcNow - _cacheAtUtc < _cacheTtl) return _cache;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache is not null && DateTime.UtcNow - _cacheAtUtc < _cacheTtl) return _cache;

            using var client = CreateClient();
            using var response = await client.GetAsync(
                $"https://api.github.com/repos/{_repo}/releases/latest", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub releases check failed: HTTP {Code}", (int)response.StatusCode);
                return _cache; // keep stale cache if we have one
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var published = root.TryGetProperty("published_at", out var p) && p.GetDateTime() is var dt ? dt : DateTime.UtcNow;

            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var arr))
                foreach (var a in arr.EnumerateArray())
                    assets.Add(new ReleaseAsset(
                        a.GetProperty("name").GetString() ?? "",
                        a.TryGetProperty("size", out var s) ? s.GetInt64() : 0));

            _cache = new ReleaseInfo(tag, tag.TrimStart('v', 'V'), notes, published, assets);
            _cacheAtUtc = DateTime.UtcNow;
            _logger.LogInformation("Update feed refreshed: latest release = {Tag}", tag);
            return _cache;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "GitHub releases check unreachable — serving stale cache if any.");
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Downloads one release asset to a local cache file (per tag) and returns its path.
    /// School LAN then serves admins instantly; null if not found/unreachable.
    /// </summary>
    public async Task<string?> DownloadAssetAsync(string assetName, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        var latest = await GetLatestAsync(ct);
        if (latest is null) return null;
        if (latest.Assets.All(a => a.Name != assetName)) return null;

        var cacheDir = Path.Combine(Path.GetTempPath(), "smartschool-updates", latest.Tag);
        Directory.CreateDirectory(cacheDir);
        var cachePath = Path.Combine(cacheDir, assetName);
        if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
            return cachePath;

        var client = CreateClient();
        try
        {
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            client.Timeout = TimeSpan.FromMinutes(15);

            // Resolve the asset id from the release listing.
            using var listResp = await client.GetAsync(
                $"https://api.github.com/repos/{_repo}/releases/latest", ct);
            if (!listResp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync(ct));
            long assetId = 0;
            if (doc.RootElement.TryGetProperty("assets", out var arr))
                foreach (var a in arr.EnumerateArray())
                    if (a.GetProperty("name").GetString() == assetName)
                    { assetId = a.GetProperty("id").GetInt64(); break; }
            if (assetId == 0) return null;

            // GitHub: 302 → signed URL; HttpClient follows it (Authorization header
            // is correctly dropped on the cross-host redirect).
            var tempPath = cachePath + ".part";
            await using (var src = await client.GetStreamAsync(
                $"https://api.github.com/repos/{_repo}/releases/assets/{assetId}", ct))
            await using (var dst = File.Create(tempPath))
            {
                await src.CopyToAsync(dst, ct);
            }
            File.Move(tempPath, cachePath, overwrite: true);
            return cachePath;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Asset download failed: {Asset}", assetName);
            return null;
        }
        finally
        {
            client.Dispose();
        }
    }
}
