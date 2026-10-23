using System.IO;
using System.Text;
using System.Windows;
using SmartSchool.Admin.Services;
using SmartSchool.Admin.Views;

namespace SmartSchool.Admin;

public partial class App : Application
{
    public App()
    {
        // OpenStreetMap tile policy: identifiable app User-Agent/Referer required,
        // warna OSM server tiles pe "Access blocked" 403 laga deta hai.
        // OpenStreetMap tile usage policy: identifiable app User-Agent required (ya
        // tiles pe "Access blocked" 403 dikhati hain). UserAgent = static, pure app ko lagta hai.
        GMap.NET.MapProviders.GMapProvider.UserAgent =
            "SmartSchoolPro-Admin/1.1 (+https://github.com/johnpaul210488-lgtm/smartschool-pro)";

        // NEVER die silently: crash → crash.log + server report (server GitHub issue banata hai).
        DispatcherUnhandledException += (_, e) =>
        {
            var ex = e.Exception;
            WriteCrash(ex);
            var result = SendCrashReportWithTimeout(ex);
            var reportLine = result is null
                ? "HALL: report server ko nahi ja saki (server off/internet band?) — crash.log mein save kar di hai."
                : result.IssueNumber is int n
                    ? $"✅ Report bhej di gai — GitHub par issue #{n} ban gai. {((result.Occurrences > 1) ? $"(yeh crash ab tak {result.Occurrences} dafa aya)" : "")}"
                    : "✅ Report server mein save ho gai — GitHub off hai to jab server online ho khud GitHub chali jayegi.";
            MessageBox.Show(
                $"Masoom masla aa gaya — app ab band ho jayegi.\n\n" +
                $"{reportLine}\n\n" +
                $"Is issue par kaam shuru karne ke liye bas mujhe keh dein: 'GitHub par nayi bug report aayi hai'.\n\n" +
                $"— Technical detail —\n{ex.GetType().Name}: {ex.Message}",
                "Queen International School — Bug report", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { WriteCrash(e.Exception); e.SetObserved(); };
    }

    /// <summary>Crash ke aakhri lamhon mein report bhejta hai — max 4 second intzaar.</summary>
    private static BugReportService.SendResult? SendCrashReportWithTimeout(Exception ex)
    {
        try
        {
            var task = BugReportService.SendAsync(
                severity: "Crash",
                title: $"Auto crash: {ex.GetType().Name}: {TrimTo(ex.Message, 120)}",
                message: ex.Message,
                stackTrace: ex.ToString(),
                screen: BugReportService.CurrentScreen());
            if (task.Wait(TimeSpan.FromSeconds(4))) return task.Result;
        }
        catch { /* report fail — crash.log already written */ }
        return null;
    }

    private static string TrimTo(string s, int n) => s.Length <= n ? s : s[..n];

    private static void WriteCrash(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartSchoolAdmin");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"\r\n===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\r\n{ex}\r\n", Encoding.UTF8);
        }
        catch { /* last resort: never crash the crash handler */ }
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // CHANGED: saved color-palette lagayein (7 professional skins) — resources load hone ke baad
        ThemeHelper.ApplySaved();

        // Session (holds JWT + user info); shared by all windows via AppSession.Current.
        var session = new AppSession();
        AppSession.Current = session;

        // CHANGED: saved session ho to login MAANGNE ki zaroorat nahi — seedha main window.
        // (Logout dabane par hi session clear hota hai aur dobara login window dikhata hai.)
        var saved = ApiClient.Instance.TokenStore.Load();
        if (saved is not null)
        {
            var s = saved.Value;
            if (s.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(5))
            {
                session.Restore(s.Token, s.ExpiresAtUtc, s.UserId, s.Username ?? string.Empty, s.FullName, s.Role);
            }
            else if (!string.IsNullOrWhiteSpace(s.Username) && !string.IsNullOrWhiteSpace(s.Password))
            {
                // JWT expire ho gayi — saved credentials se khud silent re-login
                try { _ = await ApiClient.Instance.LoginAsync(s.Username, s.Password, true); } catch { /* offline → login dikhega */ }
            }
        }

        if (session.IsLoggedIn)
        {
            var main0 = new MainWindow(session);
            MainWindow = main0;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main0.Show();
            return;
        }

        var login = new LoginWindow(session);
        var ok = login.ShowDialog();

        if (ok == true && session.IsLoggedIn)
        {
            var main = new MainWindow(session);
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }
        else
        {
            Shutdown();
        }
    }
}
