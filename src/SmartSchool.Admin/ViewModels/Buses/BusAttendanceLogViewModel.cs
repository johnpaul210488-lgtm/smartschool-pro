using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;
using SmartSchool.Admin.Helpers;
using SmartSchool.Core.DTOs;

namespace SmartSchool.Admin.ViewModels.Buses;

public partial class BusAttendanceLogViewModel : ObservableObject, IDisposable
{
    private readonly ApiClient _api = ApiClient.Instance;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private BusListItem? _selectedBus;

    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private DateTime? _selectedDate; // yyyy-MM-dd (Pakistan time)

    public BulkObservableCollection<BusEventListItem> Events { get; } = new();
    public ObservableCollection<BusListItem> Buses { get; } = new();

    [ObservableProperty] private int _totalCount;

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
        TotalCount++;
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
        _selectedDate = DateTime.UtcNow.AddHours(5).Date; // Pakistan time today
        await LoadAsync();
    }

    partial void OnSelectedBusChanged(BusListItem? value) => _ = LoadAsync();
    partial void OnSearchTextChanged(string? value) => _ = LoadAsync();
    partial void OnSelectedDateChanged(DateTime? value) => _ = LoadAsync();

    private string? DateToString(DateTime? d) => d?.ToString("yyyy-MM-dd");

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var busId = SelectedBus?.Id > 0 ? SelectedBus.Id : (int?)null;
            var r = await _api.TodayAttendanceAsync(busId, DateToString(_selectedDate), _searchText);
            if (r.Ok && r.Data is not null)
            {
                Events.ReplaceAll(r.Data.Items.OrderByDescending(e => e.Id));
                TotalCount = r.Data.TotalCount;
            }
            else ToastService.Current.Error("Attendance", r.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = null;
        _ = LoadAsync();
    }

    [RelayCommand]
    private void Print()
    {
        // Notify code-behind to show PrintDialog for the attendance grid
        WeakReferenceMessenger.Default.Send(new PrintAttendanceMessage());
    }

    public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
}

public record PrintAttendanceMessage();
