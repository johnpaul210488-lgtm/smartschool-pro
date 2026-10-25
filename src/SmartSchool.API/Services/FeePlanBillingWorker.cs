namespace SmartSchool.API.Services;

/// <summary>
/// Runs the fee-plan billing sweep every 30 minutes (the first sweep runs at
/// startup) using <see cref="FeePlanBillingService"/>. Generation is idempotent,
/// so restarting the API never duplicates a bill.
/// </summary>
public sealed class FeePlanBillingWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<FeePlanBillingWorker> _logger;

    public FeePlanBillingWorker(IServiceScopeFactory scopes, ILogger<FeePlanBillingWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var billing = scope.ServiceProvider.GetRequiredService<FeePlanBillingService>();
                await billing.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Recurring fee-plan generation failed; it will retry next cycle."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
