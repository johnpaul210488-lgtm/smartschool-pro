using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSchool.API.Services;
using SmartSchool.Shared.Common;

namespace SmartSchool.API.Controllers;

/// <summary>
/// Client self-update feed. Clients (WPF admin, driver/parent APKs) ask THEIR OWN
/// school server for the newest version; the server proxies GitHub Releases so
/// devices never need GitHub access or tokens.
/// Flow: developer pushes tag v1.x.y → CI builds assets → GitHub Release →
/// school API picks it up (cached) → clients prompt "Update available".
/// </summary>
[ApiController]
[Authorize]
[Route("api/app-updates")]
public sealed class AppUpdatesController : BaseApiController
{
    private readonly GitHubReleaseProvider _provider;
    private readonly IConfiguration _config;

    public AppUpdatesController(GitHubReleaseProvider provider, IConfiguration config)
    {
        _provider = provider;
        _config = config;
    }

    private string? AssetNameFor(string app) => app.ToLowerInvariant() switch
    {
        "admin" => _config["Updates:AdminAsset"] ?? "SmartSchool.Admin.exe",
        "driver" => _config["Updates:DriverAsset"] ?? "driver-release.apk",
        "parent" => _config["Updates:ParentAsset"] ?? "parent-release.apk",
        _ => null
    };

    public sealed record AppUpdateInfo(
        string App,
        string? LatestVersion,
        bool Configured,
        string? Notes,
        string? SizeHuman);

    /// <summary>GET /api/app-updates/admin (or driver|parent) → newest version info.</summary>
    [HttpGet("{app}")]
    public async Task<ActionResult<ApiResponse<AppUpdateInfo>>> GetInfo(string app, CancellationToken ct)
    {
        var assetName = AssetNameFor(app);
        if (assetName is null)
            return BadRequestEnvelope("app must be admin | driver | parent.");

        var latest = await _provider.GetLatestAsync(ct);
        if (latest is null)
        {
            // Updates not configured (or GitHub unreachable): report "nothing new" gracefully.
            return OkEnvelope(new AppUpdateInfo(app, null, _provider.IsConfigured, null, null));
        }

        var asset = latest.Assets.FirstOrDefault(a => a.Name == assetName);
        var sizeHuman = asset is null ? null : $"{asset.SizeBytes / 1024.0 / 1024.0:F1} MB";
        return OkEnvelope(new AppUpdateInfo(app, latest.Version, true, latest.Notes, sizeHuman));
    }

    /// <summary>Streams the newest asset for an app (proxied + cached on the school server).</summary>
    [HttpGet("{app}/download")]
    public async Task<IActionResult> Download(string app, CancellationToken ct)
    {
        var assetName = AssetNameFor(app);
        if (assetName is null)
            return BadRequest("app must be admin | driver | parent.");

        var path = await _provider.DownloadAssetAsync(assetName, ct);
        if (path is null)
            return NotFound("Update asset not available (check Updates settings / GitHub release).");

        return PhysicalFile(path,
            "application/octet-stream",
            assetName,
            enableRangeProcessing: true);
    }
}
