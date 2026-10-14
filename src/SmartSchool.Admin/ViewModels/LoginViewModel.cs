using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly ApiClient _api = ApiClient.Instance;

    [ObservableProperty] private string _username = "admin";
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _rememberMe = true;

    /// <summary>LoginWindow sets this so the VM can close the window with DialogResult=true.</summary>
    public Action? CloseWithSuccess { get; set; }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Username aur password dono likhein.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var (ok, _, error) = await _api.LoginAsync(Username.Trim(), Password, RememberMe);
            if (ok)
            {
                CloseWithSuccess?.Invoke();
            }
            else
            {
                ErrorMessage = error;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
