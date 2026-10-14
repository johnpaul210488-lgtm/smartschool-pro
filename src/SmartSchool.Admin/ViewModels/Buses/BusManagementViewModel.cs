using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Buses;

public partial class BusManagementViewModel : ObservableObject, IDisposable
{
    private readonly ApiClient _api = ApiClient.Instance;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private BusListItem? _selectedBus;

    // form fields
    [ObservableProperty] private string _formNumber = string.Empty;
    [ObservableProperty] private string _formRoute = string.Empty;
    [ObservableProperty] private string _formVehicleNo = string.Empty;
    [ObservableProperty] private int _formCapacity = 40;
    [ObservableProperty] private string _formError = string.Empty;

    public ObservableCollection<BusListItem> Buses { get; } = new();
    public ObservableCollection<DriverListItem> Drivers { get; } = new();

    public BusManagementViewModel()
    {
        _ = LoadAsync();
        WeakReferenceMessenger.Default.Register<BusLocationMessage>(this, (_, m) =>
        {
            var b = Buses.FirstOrDefault(x => x.Id == m.Payload.BusId);
            if (b is null) return;
            var i = Buses.IndexOf(b);
            Buses[i] = b; // tag row refresh (Status column shows live dot via converter)
        });
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var r = await _api.BusesAsync();
            if (r.Ok && r.Data is not null)
            {
                Buses.Clear();
                foreach (var b in r.Data) Buses.Add(b);
            }
            else ToastService.Current.Error("Buses", r.Error);

            var d = await _api.DriversAsync();
            if (d.Ok && d.Data is not null)
            {
                Drivers.Clear();
                foreach (var x in d.Data) Drivers.Add(x);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedBusChanged(BusListItem? value)
    {
        if (value is null) return;
        FormNumber = value.BusNumber;
        FormRoute = value.Route;
        FormVehicleNo = value.VehicleNo;
        FormCapacity = value.Capacity;
    }

    [RelayCommand] private void NewBus()
    {
        SelectedBus = null;
        FormNumber = FormRoute = FormVehicleNo = string.Empty;
        FormCapacity = 40;
        FormError = string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(FormNumber) || string.IsNullOrWhiteSpace(FormRoute) ||
            string.IsNullOrWhiteSpace(FormVehicleNo))
        {
            FormError = "Bus number, route aur vehicle no — teeno lazmi hain.";
            return;
        }

        var body = new BusUpsertRequest
        {
            BusNumber = FormNumber.Trim(),
            Route = FormRoute.Trim(),
            VehicleNo = FormVehicleNo.Trim(),
            Capacity = FormCapacity,
            IsActive = true
        };

        var r = SelectedBus is null
            ? await _api.PostAsync<BusListItem>("api/buses", body)
            : await _api.PutAsync<BusListItem>($"api/buses/{SelectedBus.Id}", body);

        if (r.Ok) { ToastService.Current.Info("Bus", "Saved."); NewBus(); await LoadAsync(); }
        else FormError = r.Error;
    }

    [RelayCommand]
    private async Task DeleteAsync(BusListItem? b)
    {
        if (b is null) return;
        var r = await _api.DeleteAsync($"api/buses/{b.Id}");
        if (r.Ok) { ToastService.Current.Info("Bus", $"Bus #{b.BusNumber} deactivate hui."); await LoadAsync(); }
        else ToastService.Current.Error("Bus", r.Error);
    }

    [RelayCommand]
    private async Task AssignDriverAsync(object? driverObj)
    {
        if (SelectedBus is null || driverObj is not DriverListItem driver) return;
        var r = await _api.PutAsync<BusListItem>($"api/buses/{SelectedBus.Id}/assign-driver",
            new { driverId = driver.Id });
        if (r.Ok) { ToastService.Current.Info("Bus", $"{driver.FullName} → Bus #{SelectedBus.BusNumber}"); await LoadAsync(); }
        else ToastService.Current.Error("Assign", r.Error);
    }

    public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
}
