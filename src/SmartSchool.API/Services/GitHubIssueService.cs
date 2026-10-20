using System.Net.Http.Headers;
using System.Text.Json;

namespace SmartSchool.API.Services;

/// <summary>
/// Server-side GitHub Issues client: jab koi app bug report bhejti hai to
/// repo par issue banata hai. Token SIRF server ke appsettings.json mein
/// rehta hai — clients (WPF/APK) mein koi token nahi jati.
/// </summary>
public sealed class GitHubIssueService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<GitHubIssueService> _logger;
    private readonly string _owner;
    private readonly string _repo;
    private readonly string _token;

    public GitHubIssueService(IHttpClientFactory httpFactory, IConfiguration config,
        ILogger<GitHubIssueService> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        var sec = config.GetSection("GitHub");
        _owner = sec["Owner"] ?? "";
        _repo = sec["Repo"] ?? "";
        _token = sec["Token"] ?? "";
        IsEnabled = sec.GetValue("Enabled", false);
    }

    public bool IsConfigured { get; }
    public bool IsEnabled { get; }

    /// <summary>Returns the created issue number, or null when disabled/failed.</summary>
    public async Task<int?> CreateIssueAsync(string title, string body, IEnumerable<string> labels, CancellationToken ct)
    {
        if (!IsEnabled || !IsConfigured || string.IsNullOrWhiteSpace(_token)) return null;

        try
        {
            var client = _httpFactory.CreateClient(nameof(GitHubIssueService));
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SmartSchoolPro-API", "1.1"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var payload = new
            {
                title,
                body,
                labels = labels.ToArray()
            };
            using var resp = await client.PostAsJsonAsync(
                $"https://api.github.com/repos/{_owner}/{_repo}/issues", payload, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub issue create failed: {Status} {Body}", (int)resp.StatusCode, json[..Math.Min(300, json.Length)]);
                return null;
            }
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("number", out var n) ? n.GetInt32() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GitHub issue create nahi hui (network/config) — report DB mein safe hai");
            return null;
        }
    }
}

/// <summary>
/// Har 20 minute baad pending reports (issue number nahi mili) ko GitHub ko
/// dobara bhejta hai — server offline thi ya rate-limit lagi thi to bhi koi report nahi khota.
/// </summary>
public sealed class ErrorReportSyncWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ErrorReportSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // startup ke thori dair baad pehli koshish (DB init ho jaye)
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await ErrorReportOutbox.TryFlushPendingAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "ErrorReportSyncWorker flush mein masla");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
