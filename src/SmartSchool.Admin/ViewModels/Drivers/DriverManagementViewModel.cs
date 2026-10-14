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
    [ObservableProperty] private bool _isEditing;

    public ObservableCollection<DriverListItem> Drivers { get; } = new();

    public DriverManagementViewModel() => _ = LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var r = await _api.DriversAsync();
            if (r.Ok && r.Data is not null)
            {
                Drivers.Clear();
                foreach (var d in r.Data) Drivers.Add(d);
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
        if (value is null) return;
        FormName = value.FullName;
        FormPhone = value.Phone;
        FormLicense = value.LicenseNo;
        IsEditing = true;
        FormError = string.Empty;
    }

    [RelayCommand]
    private void NewDriver()
    {
        SelectedDriver = null;
        FormName = FormPhone = FormLicense = FormUsername = FormPassword = string.Empty;
        IsEditing = false;
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
            var r = await _api.PutAsync<DriverListItem>($"api/drivers/{SelectedDriver.Id}",
                new DriverUpdateRequest { FullName = FormName.Trim(), Phone = FormPhone.Trim(), LicenseNo = FormLicense.Trim() });
            if (r.Ok) { ToastService.Current.Info("Driver", "Updated."); NewDriver(); await LoadAsync(); }
            else FormError = r.Error;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(FormUsername) || string.IsNullOrWhiteSpace(FormPassword))
            {
                FormError = "Naye driver ke liye username + password lazmi hain (mobile app login).";
                return;
            }
            var r = await _api.PostAsync<DriverListItem>("api/drivers",
                new DriverCreateRequest
                {
                    FullName = FormName.Trim(),
                    Phone = FormPhone.Trim(),
                    LicenseNo = FormLicense.Trim(),
                    Username = FormUsername.Trim(),
                    Password = FormPassword
                });
            if (r.Ok) { ToastService.Current.Info("Driver", $"{FormName} add ho gaya."); NewDriver(); await LoadAsync(); }
            else FormError = r.Error;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(DriverListItem? d)
    {
        if (d is null) return;
        var r = await _api.DeleteAsync($"api/drivers/{d.Id}");
        if (r.Ok) { ToastService.Current.Info("Driver", $"{d.FullName} deactivate hogaya."); await LoadAsync(); }
        else ToastService.Current.Error("Driver", r.Error);
    }
}
