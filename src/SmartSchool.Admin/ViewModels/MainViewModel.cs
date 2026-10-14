using System.Collections.ObjectModel;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SmartSchool.Admin.Services;
using SmartSchool.Admin.ViewModels;
using SmartSchool.Admin.ViewModels.Buses;
using SmartSchool.Admin.ViewModels.Drivers;
using SmartSchool.Admin.ViewModels.Fees;
using SmartSchool.Admin.ViewModels.Reports;
using SmartSchool.Admin.ViewModels.Students;
using SmartSchool.Admin.Views;
using SmartSchool.Admin.Views.Buses;
using SmartSchool.Admin.Views.Drivers;
using SmartSchool.Admin.Views.Fees;
using SmartSchool.Admin.Views.Reports;
using SmartSchool.Admin.Views.Students;

namespace SmartSchool.Admin.ViewModels;

public sealed class NavItem
{
    public string Title { get; init; } = string.Empty;
    public string IconKind { get; init; } = "ViewDashboard";
    public Func<UserControl> CreateView { get; init; } = null!;
}

public partial class MainViewModel : ObservableObject
{
    private readonly SignalRClientService _signalR = new();

    [ObservableProperty] private UserControl? _currentView;
    [ObservableProperty] private NavItem? _selectedNavItem;
    [ObservableProperty] private string _liveStatus = "⏳ Live connection shuru ho raha hai…";
    [ObservableProperty] private string _userName = string.Empty;
    [ObservableProperty] private string _userRole = string.Empty;
    [ObservableProperty] private bool _isDarkTheme = true;

    public ObservableCollection<NavItem> NavItems { get; } = new();
    public ToastService Toasts => ToastService.Current;
    public SignalRClientService SignalR => _signalR;

    public MainViewModel()
    {
        var u = AppSession.Current.User;
        UserName = u?.FullName ?? "Admin";
        UserRole = u?.Role ?? "";

        NavItems.Add(new NavItem { Title = "Dashboard", IconKind = "ViewDashboard", CreateView = OpenDashboard });
        NavItems.Add(new NavItem { Title = "Students", IconKind = "AccountGroup", CreateView = OpenStudentList });
        NavItems.Add(new NavItem { Title = "Fees", IconKind = "CashMultiple", CreateView = OpenFeeDashboard });
        NavItems.Add(new NavItem { Title = "Buses", IconKind = "Bus", CreateView = OpenBuses });
        NavItems.Add(new NavItem { Title = "Live Tracking", IconKind = "MapMarkerRadius", CreateView = OpenLiveTracking });
        NavItems.Add(new NavItem { Title = "Bus Attendance", IconKind = "ClipboardList", CreateView = OpenAttendance });
        NavItems.Add(new NavItem { Title = "Drivers", IconKind = "CardAccountDetails", CreateView = OpenDrivers });
        NavItems.Add(new NavItem { Title = "Reports", IconKind = "ChartBar", CreateView = OpenReports });

        SelectedNavItem = NavItems[0];

        _signalR.ConnectionStateChanged += s => LiveStatus = s;
        _ = _signalR.StartAsync();

        // Global live alerts: toast + sound (grids subscribe via their own VMs).
        WeakReferenceMessenger.Default.Register<PickInAlertMessage>(this, (_, m) =>
            Toasts.PickIn(m.Payload.StudentName, m.Payload.BusNumber, m.Payload.TimePakistan));
        WeakReferenceMessenger.Default.Register<DropOutAlertMessage>(this, (_, m) =>
            Toasts.DropOut(m.Payload.StudentName, m.Payload.BusNumber, m.Payload.TimePakistan));
        WeakReferenceMessenger.Default.Register<TripUpdateMessage>(this, (_, m) =>
            Toasts.Info("Trip Update", $"Bus #{m.Payload.BusNumber} → {m.Payload.Status}"));
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value is not null) CurrentView = value.CreateView();
    }

    // ---- simple pages ----
    private DashboardView OpenDashboard()
        => new() { DataContext = new DashboardViewModel() };
    private BusManagementView OpenBuses()
        => new() { DataContext = new BusManagementViewModel() };
    private LiveTrackingView OpenLiveTracking()
        => new() { DataContext = new LiveTrackingViewModel() };
    private BusAttendanceLogView OpenAttendance()
        => new() { DataContext = new BusAttendanceLogViewModel() };
    private DriverManagementView OpenDrivers()
        => new() { DataContext = new DriverManagementViewModel() };
    private ReportView OpenReports()
        => new() { DataContext = new ReportViewModel() };

    // ---- Students flow: list → form / profile ----
    private StudentListView OpenStudentList()
    {
        var vm = new StudentListViewModel();
        vm.EditRequested += id =>
        {
            var fvm = new StudentFormViewModel(id);
            fvm.SavedAndClose += () => CurrentView = OpenStudentList();
            fvm.Cancelled += () => CurrentView = OpenStudentList();
            CurrentView = new StudentFormView { DataContext = fvm };
        };
        vm.ProfileRequested += id =>
        {
            var pvm = new StudentProfileViewModel(id);
            pvm.BackRequested += () => CurrentView = OpenStudentList();
            CurrentView = new StudentProfileView { DataContext = pvm };
        };
        return new StudentListView { DataContext = vm };
    }

    // ---- Fees flow: dashboard → collect → receipt ----
    private FeeDashboardView OpenFeeDashboard()
    {
        var vm = new FeeDashboardViewModel();
        vm.CollectRequested += fee =>
        {
            var cvm = new FeeCollectionViewModel(fee);
            cvm.PaidAndShowReceipt += paidFee =>
            {
                var rvm = new FeeReceiptViewModel(paidFee);
                rvm.BackRequested += () => CurrentView = OpenFeeDashboard();
                CurrentView = new FeeReceiptView { DataContext = rvm };
            };
            cvm.Cancelled += () => CurrentView = OpenFeeDashboard();
            CurrentView = new FeeCollectionView { DataContext = cvm };
        };
        return new FeeDashboardView { DataContext = vm };
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        ThemeHelper.SetDark(!IsDarkTheme);
        IsDarkTheme = !IsDarkTheme;
    }

    [RelayCommand]
    private void Logout()
    {
        AppSession.Current.SignOut();
        ApiClient.Instance.TokenStore.Clear();
        AppSession.Current = new AppSession();
        _ = _signalR.DisposeAsync();
        System.Windows.Application.Current.Shutdown();
    }
}
