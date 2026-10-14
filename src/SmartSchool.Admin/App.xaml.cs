using System.Windows;
using SmartSchool.Admin.Services;
using SmartSchool.Admin.Views;

namespace SmartSchool.Admin;

public partial class App : Application
{
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
