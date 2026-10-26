using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Drivers;

public partial class DriverManagementViewModel : ObservableObject
{
    private readonly ApiClient _api = ApiClient.Instance;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private DriverListItem? _selectedDriver;

    [ObservableProperty] private string _formName = string.Empty;
    [ObservableProperty] private string _formPhone = string.Empty;
    [ObservableProperty] private string _formLicense = string.Empty;
    [ObservableProperty] private string _formUsername = string.Empty;
    [ObservableProperty] private string _formPassword = string.Empty;
    [ObservableProperty] private string _formError = string.Empty;
    [ObservableProperty] private string _formTitle = "Naya driver";
    [ObservableProperty] private string _formPasswordHint = "Password * (app login ke liye)";
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _showInactive;

    public ObservableCollection<DriverListItem> Drivers { get; } = new();

    public DriverManagementViewModel() => _ = LoadAsync();

    partial void OnShowInactiveChanged(bool value) => _ = LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var keepId = SelectedDriver?.Id;
            var r = await _api.DriversAsync(ShowInactive);
            if (r.Ok && r.Data is not null)
            {
                Drivers.Clear();
                foreach (var d in r.Data) Drivers.Add(d);
                if (keepId is int id) SelectedDriver = Drivers.FirstOrDefault(x => x.Id == id);
            }
            else ToastService.Current.Error("Drivers", r.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedDriverChanged(DriverListItem? value)
    {
        FormError = string.Empty;
        if (value is null)
        {
            FormTitle = "Naya driver";
            IsEditing = false;
            FormPasswordHint = "Password * (app login ke liye)";
            return;
        }
        FormName = value.FullName;
        FormPhone = value.Phone;
        FormLicense = value.LicenseNo;
        FormUsername = value.Username;
        FormPassword = string.Empty;
        IsEditing = true;
        FormTitle = $"{value.FullName} edit ho raha hai" + (value.IsActive ? string.Empty : "  (INACTIVE)");
        FormPasswordHint = "Naya password (khali chhodein to purana rahega)";
    }

    /// <summary>List ke "Edit" button se: driver ko form mein khol deta hai.</summary>
    [RelayCommand]
    private void EditDriver(DriverListItem? d)
    {
        if (d is null) return;
        if (ReferenceEquals(SelectedDriver, d)) OnSelectedDriverChanged(d);
        else SelectedDriver = d;
    }

    [RelayCommand]
    private void NewDriver()
    {
        SelectedDriver = null;
        FormName = FormPhone = FormLicense = FormUsername = FormPassword = string.Empty;
        IsEditing = false;
        FormTitle = "Naya driver";
        FormPasswordHint = "Password * (app login ke liye)";
        FormError = string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(FormName) || string.IsNullOrWhiteSpace(FormPhone) ||
            string.IsNullOrWhiteSpace(FormLicense))
        {
            FormError = "Naam, phone aur license no lazmi hain.";
            return;
        }

        if (IsEditing && SelectedDriver is not null)
        {
            var driver = SelectedDriver;
            if (string.IsNullOrWhiteSpace(FormUsername))
            {
                FormError = "Username khali nahi ho sakta.";
                return;
            }

            var r = await _api.PutAsync<DriverListItem>($"api/drivers/{driver.Id}", new DriverUpdateRequest
            {
                FullName = FormName.Trim(),
                Phone = FormPhone.Trim(),
                LicenseNo = FormLicense.Trim(),
                IsActive = driver.IsActive // edit se status nahi badalta
            });
            if (!r.Ok) { FormError = r.Error; return; }

            var username = FormUsername.Trim();
            var newPassword = FormPassword;
            if (username != driver.Username || newPassword.Length > 0)
            {
                var login = await _api.PutAsync<DriverListItem>($"api/drivers/{driver.Id}/login", new DriverLoginRequest
                {
                    Username = username,
                    NewPassword = newPassword.Length > 0 ? newPassword : null
                });
                if (!login.Ok)
                {
                    FormError = $"Naam save hua, lekin login nahi badla: {login.Error}";
                    await LoadAsync();
                    return;
                }
            }

            ToastService.Current.Info("Driver", "Driver ki tabdeeli save ho gayi.");
            NewDriver();
            await LoadAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(FormUsername) || string.IsNullOrWhiteSpace(FormPassword))
        {
            FormError = "Naye driver ke liye username + password lazmi hain (mobile app login).";
            return;
        }
        var add = await _api.PostAsync<DriverListItem>("api/drivers", new DriverCreateRequest
        {
            FullName = FormName.Trim(),
            Phone = FormPhone.Trim(),
            LicenseNo = FormLicense.Trim(),
            Username = FormUsername.Trim(),
            Password = FormPassword
        });
        if (add.Ok) { ToastService.Current.Info("Driver", $"{FormName.Trim()} add ho gaya."); NewDriver(); await LoadAsync(); }
        else FormError = add.Error;
    }

    /// <summary>Driver ko active / inactive karta hai. Inactive driver ka app login bhi band ho jata hai.</summary>
    [RelayCommand]
    private async Task ToggleActiveAsync(DriverListItem? row)
    {
        var d = row ?? SelectedDriver;
        if (d is null) { ToastService.Current.Error("Driver", "Pehle list mein driver ke saamne 'Edit' dabayein."); return; }
        var activate = !d.IsActive;
        if (!activate)
        {
            var ok = System.Windows.MessageBox.Show(
                $"{d.FullName} ko deactivate karein?\n\nIs driver ka app login band ho jayega aur bus se driver hat jayega.",
                "Confirm: Deactivate driver", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (ok != System.Windows.MessageBoxResult.Yes) return;
        }
        var r = await _api.PutAsync<DriverListItem>($"api/drivers/{d.Id}", new DriverUpdateRequest
        {
            FullName = d.FullName,
            Phone = d.Phone,
            LicenseNo = d.LicenseNo,
            IsActive = activate
        });
        if (r.Ok)
        {
            ToastService.Current.Info("Driver", activate ? $"{d.FullName} active ho gaya." : $"{d.FullName} deactivate ho gaya.");
            if (!activate && !ShowInactive && SelectedDriver?.Id == d.Id) NewDriver();
            await LoadAsync();
        }
        else ToastService.Current.Error("Driver", r.Error);
    }
}
