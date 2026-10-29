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
    [ObservableProperty] private string _formTitle = "Naya bus";
    [ObservableProperty] private string _toggleActiveText = "Deactivate bus";
    [ObservableProperty] private bool _showInactive;

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

    /// <summary>Buses aur drivers dono load karta hai. Chuni hui bus reload ke baad bhi chuni rehti hai.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var busesTask = _api.BusesAsync(ShowInactive);
            var driversTask = _api.DriversAsync();
            var buses = await busesTask;
            var drivers = await driversTask;

            var keepId = SelectedBus?.Id;
            if (buses.Ok && buses.Data is not null)
            {
                Buses.Clear();
                foreach (var b in buses.Data) Buses.Add(b);
                if (keepId is int id) SelectedBus = Buses.FirstOrDefault(x => x.Id == id);
            }
            else ToastService.Current.Error("Buses", buses.Error);

            if (drivers.Ok && drivers.Data is not null)
            {
                Drivers.Clear();
                foreach (var x in drivers.Data) Drivers.Add(x);
            }
            else ToastService.Current.Error("Drivers", drivers.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnShowInactiveChanged(bool value) => _ = LoadAsync();

    partial void OnSelectedBusChanged(BusListItem? value)
    {
        FormError = string.Empty;
        if (value is null)
        {
            FormTitle = "Naya bus";
            ToggleActiveText = "Deactivate bus";
            return;
        }
        FormNumber = value.BusNumber;
        FormRoute = value.Route;
        FormVehicleNo = value.VehicleNo;
        FormCapacity = value.Capacity;
        FormTitle = $"Bus #{value.BusNumber} edit ho raha hai" + (value.IsActive ? string.Empty : "  (INACTIVE)");
        ToggleActiveText = value.IsActive ? "Deactivate bus" : "Activate bus";
    }

    /// <summary>Row ke "Edit" button se: us bus ko form mein khol deta hai (selection par depend nahi).</summary>
    [RelayCommand]
    private void EditBus(BusListItem? b)
    {
        if (b is null) return;
        if (ReferenceEquals(SelectedBus, b)) OnSelectedBusChanged(b);
        else SelectedBus = b;
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
        if (FormCapacity < 1 || FormCapacity > 200)
        {
            FormError = "Capacity 1 se 200 ke beech honi chahiye.";
            return;
        }

        var body = new BusUpsertRequest
        {
            BusNumber = FormNumber.Trim(),
            Route = FormRoute.Trim(),
            VehicleNo = FormVehicleNo.Trim(),
            Capacity = FormCapacity,
            // Edit mein active/inactive status bus ka apna rahe (Save se active nahi hota).
            IsActive = SelectedBus?.IsActive ?? true
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
        if (r.Ok)
        {
            if (SelectedBus?.Id == b.Id) NewBus();
            ToastService.Current.Info("Bus", $"Bus #{b.BusNumber} hata di gayi.");
            await LoadAsync();
        }
        else ToastService.Current.Error("Bus", r.Error);
    }

    /// <summary>Bus ko deactivate / activate karta hai. Deactivate bus list se hat jati hai (Inactive dikhane par nazar aati hai).</summary>
    [RelayCommand]
    private async Task ToggleActiveAsync(BusListItem? row)
    {
        var bus = row ?? SelectedBus;
        if (bus is null) { ToastService.Current.Error("Bus", "Pehle list mein bus ke saamne 'Edit' dabayein."); return; }
        var activate = !bus.IsActive;
        if (!activate)
        {
            var ok = System.Windows.MessageBox.Show(
                $"Bus #{bus.BusNumber} deactivate karni hai?\n\nIs bus ke students aur driver assign rahenge; bus sirf list se hat jayegi.\nBaad mein 'Inactive dikhayein' se wapas active kar sakte hain.",
                "Confirm: Deactivate bus", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (ok != System.Windows.MessageBoxResult.Yes) return;
        }
        var r = await _api.PutAsync<BusListItem>($"api/buses/{bus.Id}", new BusUpsertRequest
        {
            BusNumber = bus.BusNumber,
            Route = bus.Route,
            VehicleNo = bus.VehicleNo,
            Capacity = bus.Capacity,
            IsActive = activate
        });
        if (r.Ok)
        {
            ToastService.Current.Info("Bus", activate ? $"Bus #{bus.BusNumber} active ho gayi." : $"Bus #{bus.BusNumber} deactivate ho gayi.");
            if (!activate && !ShowInactive) NewBus();
            await LoadAsync();
        }
        else ToastService.Current.Error("Bus", r.Error);
    }

    /// <summary>Chuni hui bus par driver assign karta hai (combo se).</summary>
    [RelayCommand]
    private async Task AssignDriverAsync(object? driverObj)
    {
        if (driverObj is not DriverListItem driver) return;
        if (SelectedBus is null)
        {
            ToastService.Current.Error("Assign", "Pehle upar se bus select karein, phir driver chunein.");
            return;
        }
        var bus = SelectedBus;
        var r = await _api.PutAsync<BusListItem>($"api/buses/{bus.Id}/assign-driver",
            new { driverId = driver.Id });
        if (r.Ok)
        {
            ToastService.Current.Info("Bus", $"{driver.FullName} → Bus #{bus.BusNumber}");
            await LoadAsync();
        }
        else ToastService.Current.Error("Assign", r.Error);
    }

    /// <summary>Chuni hui bus se driver hata deta hai.</summary>
    [RelayCommand]
    private async Task UnassignDriverAsync()
    {
        if (SelectedBus is null) { ToastService.Current.Error("Driver", "Pehle upar se bus select karein."); return; }
        if (SelectedBus.DriverId is null) { ToastService.Current.Info("Driver", "Is bus par koi driver assign nahi hai."); return; }
        var bus = SelectedBus;
        var r = await _api.PutAsync<BusListItem>($"api/buses/{bus.Id}/assign-driver",
            new { driverId = (int?)null });
        if (r.Ok)
        {
            ToastService.Current.Info("Bus", $"Bus #{bus.BusNumber} se driver hata diya gaya.");
            await LoadAsync();
        }
        else ToastService.Current.Error("Driver", r.Error);
    }

    public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
}
