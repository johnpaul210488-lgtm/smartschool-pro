using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels;

public sealed class DashboardCard
{
    public string Title { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string IconKind { get; init; } = "Information";
    public string Accent { get; init; } = "#7C4DFF";
    public string SubText { get; init; } = string.Empty;
}

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly ApiClient _api = ApiClient.Instance;

    [ObservableProperty] private string _dateLine = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<DashboardCard> Cards { get; } = new();
    public ObservableCollection<BusEventListItem> LiveAttendance { get; } = new();

    public DashboardViewModel()
    {
        _ = LoadAsync();

        // Live feed: prepend SignalR alerts to the grid.
        WeakReferenceMessenger.Default.Register<PickInAlertMessage>(this, (_, m) => AddLive(m.Payload, "PickIn"));
        WeakReferenceMessenger.Default.Register<DropOutAlertMessage>(this, (_, m) => AddLive(m.Payload, "DropOut"));

        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        t.Tick += async (_, _) => await LoadAsync();
        t.Start();
    }

    private void AddLive(BusEventNotification n, string type)
    {
        var item = new BusEventListItem
        {
            Id = n.EventId,
            StudentId = n.StudentId,
            StudentName = n.StudentName,
            ClassName = n.ClassName,
            BusId = n.BusId,
            BusNumber = n.BusNumber,
            DriverName = n.DriverName,
            Type = type,
            TimePakistan = n.TimePakistan,
            Latitude = n.Latitude,
            Longitude = n.Longitude,
            Location = n.LocationLabel,
            WhatsAppSent = false
        };
        LiveAttendance.Insert(0, item);
        while (LiveAttendance.Count > 100) LiveAttendance.RemoveAt(LiveAttendance.Count - 1);

        // Refresh header counts so today's pick-ins tick up live.
        _ = LoadCountsOnlyAsync();
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await LoadCountsOnlyAsync();

            var today = await _api.TodayAttendanceAsync();
            if (today.Ok && today.Data is not null)
            {
                LiveAttendance.Clear();
                foreach (var e in today.Data.OrderByDescending(e => e.Id)) LiveAttendance.Add(e);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadCountsOnlyAsync()
    {
        var r = await _api.DashboardAsync();
        if (!r.Ok || r.Data is null) return;
        var d = r.Data;
        DateLine = "📅 " + d.DatePakistan;

        Cards.Clear();
        Cards.Add(new DashboardCard { Title = "Total Students", Value = d.ActiveStudents.ToString(), IconKind = "AccountGroup", Accent = "#7C4DFF", SubText = "active enrolled" });
        Cards.Add(new DashboardCard { Title = "Fees Collected", Value = $"Rs {d.CollectedThisMonth:N0}", IconKind = "CashMultiple", Accent = "#00BFA5", SubText = "this month" });
        Cards.Add(new DashboardCard { Title = "Active Buses", Value = $"{d.ActiveTripsNow}/{d.TotalBuses}", IconKind = "Bus", Accent = "#FF9100", SubText = "on trip right now" });
        Cards.Add(new DashboardCard { Title = "Today's Pick-ins", Value = d.PickInsToday.ToString(), IconKind = "BusAlert", Accent = "#448AFF", SubText = $"{d.DropOutsToday} drop-outs" });
        Cards.Add(new DashboardCard { Title = "Pending Fees", Value = $"Rs {d.PendingFeesAmount:N0}", IconKind = "Alarm", Accent = "#FF5252", SubText = $"{d.PendingFeesCount} unpaid slips" });
        Cards.Add(new DashboardCard { Title = "WhatsApp sent", Value = d.WhatsAppSentToday.ToString(), IconKind = "Whatsapp", Accent = "#4CAF50", SubText = $"{d.WhatsAppFailedTotal} failed total" });
    }

    public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
}
