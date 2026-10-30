namespace SmartSchool.Infrastructure.Services.WhatsApp;

public static class WhatsAppProviders
{
    public const string DryRun = "DryRun";
    public const string Twilio = "Twilio";
    public const string LinkedDevice = "LinkedDevice";
}

public sealed class WhatsAppOptions
{
    public string Provider { get; set; } = WhatsAppProviders.DryRun;
    public string Language { get; set; } = "en";
    public bool NotifyMother { get; set; }
    public LinkedDeviceOptions LinkedDevice { get; set; } = new();
    public WhatsAppQueueOptions Queue { get; set; } = new();
}

public sealed class LinkedDeviceOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:5110";
    public string SecretsFile { get; set; } = "%LOCALAPPDATA%/SmartSchool/WhatsAppGateway/client-secret.json";
    public int RequestTimeoutSeconds { get; set; } = 90;
    public bool AutoStart { get; set; }
    public string NodeExecutable { get; set; } = "../whatsapp-gateway/SmartSchool.WhatsAppGateway.exe";
    public string EntryPoint { get; set; } = "../whatsapp-gateway/src/index.js";
}

public sealed class WhatsAppQueueOptions
{
    public int Capacity { get; set; } = 10_000;
    /// <summary>Total send attempts, including the first attempt.</summary>
    public int MaxAttempts { get; set; } = 3;
    public int RetryWindowSeconds { get; set; } = 120;
    public int InitialRetryDelaySeconds { get; set; } = 15;
    public int SweepIntervalSeconds { get; set; } = 5;
}

internal static class WhatsAppPathResolver
{
    public static string Resolve(string configuredPath, string? relativeTo = null)
    {
        var expanded = Environment.ExpandEnvironmentVariables(configuredPath ?? string.Empty);
        // Environment.ExpandEnvironmentVariables leaves %LOCALAPPDATA% untouched on hosts
        // where it is absent (Linux CI/server). Use the platform local-data directory there.
        if (expanded.Contains("%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase))
        {
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            expanded = expanded.Replace("%LOCALAPPDATA%", localData, StringComparison.OrdinalIgnoreCase);
        }
        expanded = expanded.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(expanded, relativeTo ?? AppContext.BaseDirectory);
    }
}
