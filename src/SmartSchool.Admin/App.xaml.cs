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
        // NEVER die silently: any unhandled error lands in a MessageBox + crash log.
        DispatcherUnhandledException += (_, e) =>
        {
            WriteCrash(e.Exception);
            MessageBox.Show(e.Exception.ToString(), "SmartSchool Admin — Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { WriteCrash(e.Exception); e.SetObserved(); };
    }

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

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Session (holds JWT + user info); shared by all windows via AppSession.Current.
        var session = new AppSession();
        AppSession.Current = session;

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
