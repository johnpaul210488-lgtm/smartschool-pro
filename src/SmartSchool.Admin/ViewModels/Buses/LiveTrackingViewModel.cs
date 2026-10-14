using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Buses;

/// <summary>One marker on the live map.</summary>
public partial class BusPin : ObservableObject
{
    [ObservableProperty] private int _busId;
    [ObservableProperty] private string _busNumber = string.Empty;
    [ObservableProperty] private string _driverName = string.Empty;
    [ObservableProperty] private double _latitude;
    [ObservableProperty] private double _longitude;
    [ObservableProperty] private double? _speedKmh;
    [ObservableProperty] private string _timePakistan = string.Empty;
}

/// <summary>
/// Live map of every bus. SignalR pushes UpdateBusLocation; the view also
/// broadcasts hub method WatchBus for the buses the admin is monitoring.
/// Coordinates flow to the GMap control through the Code-behind bridge.
/// </summary>
public partial class LiveTrackingViewModel : ObservableObject, IDisposable
{
    public ObservableCollection<BusPin> Pins { get; } = new();

    [ObservableProperty] private string _statusLine = "SignalR se locations ka intezar…";
    [ObservableProperty] private bool _anyBusLive;

    /// <summary>View code-behind subscribes to move GMap markers.</summary>
    public event Action<BusLocationUpdate>? LocationArrived;

    public LiveTrackingViewModel()
    {
        WeakReferenceMessenger.Default.Register<BusLocationMessage>(this, (_, m) =>
        {
            var u = m.Payload;
            var pin = Pins.FirstOrDefault(p => p.BusId == u.BusId);
            if (pin is null)
            {
                pin = new BusPin { BusId = u.BusId, BusNumber = u.BusNumber, DriverName = u.DriverName };
                Pins.Add(pin);
            }
            pin.Latitude = u.Latitude;
            pin.Longitude = u.Longitude;
            pin.SpeedKmh = u.SpeedKmh;
            pin.TimePakistan = u.TimePakistan;
            pin.BusNumber = u.BusNumber;
            pin.DriverName = u.DriverName;

            AnyBusLive = true;
            StatusLine = $"🟢 {Pins.Count} bus(es) live — last: Bus #{u.BusNumber} @ {u.TimePakistan}";
            LocationArrived?.Invoke(u);
        });

        WeakReferenceMessenger.Default.Register<TripUpdateMessage>(this, (_, m) =>
        {
            if (m.Payload.Status is "Completed" or "Cancelled")
            {
                var pin = Pins.FirstOrDefault(p => p.BusId == m.Payload.BusId);
                if (pin is not null) Pins.Remove(pin);
                AnyBusLive = Pins.Count > 0;
            }
        });
    }

    public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
}
