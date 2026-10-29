namespace SmartSchool.API.Services;

/// <summary>
/// The disposable QR/notification preview is compiled without the existing
/// outbox worker. Normal builds retain their existing behavior. This safeguard
/// cannot stop a separate production API process; never install a preview over it.
/// </summary>
public static class PreviewSafetyPolicy
{
    public static bool WhatsAppWorkerEnabled
    {
        get
        {
#if QR_PREVIEW_ONLY
            return false;
#else
            return true;
#endif
        }
    }

    public static string ConnectionString(IConfiguration configuration)
    {
#if QR_PREVIEW_ONLY
        // Ignore environment and preserved production settings entirely.
        return @"Server=(localdb)\MSSQLLocalDB;Database=SmartSchoolQrPreview;Integrated Security=true;MultipleActiveResultSets=true;TrustServerCertificate=True";
#else
        return configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
#endif
    }
}
