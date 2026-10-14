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
}
