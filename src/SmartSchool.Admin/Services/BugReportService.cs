using System.Text.Json;
using SmartSchool.Admin.Models;

namespace SmartSchool.Admin.Services;

/// <summary>
/// Bug/error report ko server (api/errorreports) bhejti hai — server phir
/// GitHub Issue banata hai. Is class mein koi GitHub token nahi hoti.
/// Kabhi exception nahi phenkta (fire-and-forget safe).
/// </summary>
public static class BugReportService
{
    public sealed record SendResult(bool Ok, int? IssueNumber, int Occurrences, string Error);

    /// <summary>Exception se auto-report. Login na hua ho to sirf false deta hai.</summary>
    public static async Task<SendResult> SendAsync(
        string severity, string title, string message,
        string? stackTrace = null, string? screen = null, string? userNote = null)
    {
        try
        {
            if (!AppSession.Current.IsLoggedIn)
                return new SendResult(false, null, 0, "Login nahi hua — report server ko nahi jai (crash.log mein safe hai).");

            var context = new
            {
                app = "SmartSchool.Admin",
                version = UpdateService.CurrentVersion,
                user = AppSession.Current.User?.FullName,
                role = AppSession.Current.User?.Role.ToString(),
                screen = screen ?? CurrentScreen(),
                note = userNote,
                os = Environment.OSVersion.VersionString,
                timePakistan = DateTime.Now.ToString("dd-MMM-yyyy hh:mm tt")
            };

            var r = await ApiClient.Instance.PostAsync<ErrorReportCreatedResponse>(
                "api/errorreports", new
                {
                    source = "Admin",
                    severity,
                    title = TrimTo(title, 280),
                    message = TrimTo(message, 4000),
                    stackTrace,
                    contextJson = JsonSerializer.Serialize(context)
                });

            return r.Ok
                ? new SendResult(true, r.Data?.IssueNumber, r.Data?.Occurrences ?? 1, "")
                : new SendResult(false, r.Data?.IssueNumber, r.Data?.Occurrences ?? 0, r.Error);
        }
        catch (Exception ex)
        {
            return new SendResult(false, null, 0, ex.Message);
        }
    }

    /// <summary>Crash se pehle kitni screen current thi — best effort.</summary>
    public static string CurrentScreen()
    {
        try
        {
            if (System.Windows.Application.Current?.MainWindow?.DataContext is not { } vm) return "?";
            var prop = vm.GetType().GetProperty("CurrentView")
                   ?? vm.GetType().GetProperty("CurrentPage")
                   ?? vm.GetType().GetProperty("CurrentViewModel");
            var val = prop?.GetValue(vm);
            return val?.GetType().Name ?? vm.GetType().Name;
        }
        catch { return "?"; }
    }

    private static string TrimTo(string s, int n) => s.Length <= n ? s : s[..n];
}
