using System.Windows;
using SmartSchool.Admin.Services;
using SmartSchool.Admin.ViewModels;

namespace SmartSchool.Admin.Views;

public partial class LoginWindow : Window
{
    public LoginWindow(AppSession session)
    {
        InitializeComponent();
        var vm = new LoginViewModel { CloseWithSuccess = () => DialogResult = true };
        DataContext = vm;
    }

    // CHANGED (added): Password box mein Enter dabana = login button
    private void PasswordBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && DataContext is LoginViewModel vm)
            vm.LoginCommand.Execute(null);
    }
}
