using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Buses;

public partial class BusAttendanceLogViewModel : ObservableObject, IDisposable
{
    private readonly ApiClient _api = ApiClient.Instance;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private BusListItem? _selectedBus;

    public ObservableCollection<BusEventListItem> Events { get; } = new();
    public ObservableCollection<BusListItem> Buses { get; } = new();

    public BusAttendanceLogViewModel()
    {
        _ = InitAsync();
        WeakReferenceMessenger.Default.Register<PickInAlertMessage>(this, (_, m) => Add(m.Payload));
        WeakReferenceMessenger.Default.Register<DropOutAlertMessage>(this, (_, m) => Add(m.Payload));
    }

    private void Add(BusEventNotification n)
    {
        Events.Insert(0, new BusEventListItem
        {
            Id = n.EventId,
            StudentId = n.StudentId,
            StudentName = n.StudentName,
            ClassName = n.ClassName,
            BusId = n.BusId,
            BusNumber = n.BusNumber,
            DriverName = n.DriverName,
            Type = n.EventType,
            TimePakistan = n.TimePakistan,
            Latitude = n.Latitude,
            Longitude = n.Longitude,
            Location = n.LocationLabel
        });
    }

    private async Task InitAsync()
    {
        var b = await _api.BusesAsync();
        if (b.Ok && b.Data is not null)
        {
            Buses.Add(new BusListItem { Id = 0, BusNumber = "— All buses —" });
            foreach (var x in b.Data) Buses.Add(x);
            SelectedBus = Buses[0];
        }
        await LoadAsync();
    }

    partial void OnSelectedBusChanged(BusListItem? value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var busId = SelectedBus?.Id > 0 ? SelectedBus.Id : (int?)null;
            var r = await _api.TodayAttendanceAsync(busId);
            if (r.Ok && r.Data is not null)
            {
                Events.Clear();
                foreach (var e in r.Data.OrderByDescending(e => e.Id)) Events.Add(e);
            }
            else ToastService.Current.Error("Attendance", r.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
}
