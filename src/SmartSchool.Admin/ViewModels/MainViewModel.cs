using System.Collections.ObjectModel;
using System.Windows;
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

// CHANGED (added): Chrome-style tab — ek module per tab, state bach ke rehti hai
public partial class TabItem : ObservableObject
{
    public string Title { get; init; } = string.Empty;
    public string IconKind { get; init; } = "ViewDashboard";
    /// <summary>Dashboard tab ko close nahi kar sakte (pinned home).</summary>
    public bool IsPinned { get; init; }
    /// <summary>Tab ka page — ek dafa banta hai, switch par wahi dikhta hai (form/drafts mehfooz).</summary>
    public UserControl? PageView { get; set; }
    public Func<UserControl> CreateView { get; init; } = null!;
    [ObservableProperty] private bool _isActive;
}

public partial class MainViewModel : ObservableObject
{
    private readonly SignalRClientService _signalR = new();
    private readonly SemaphoreSlim _updateCheckGate = new(1, 1); // CHANGED: manual/auto checks cannot race

    [ObservableProperty] private UserControl? _currentView;
    [ObservableProperty] private NavItem? _selectedNavItem;
    [ObservableProperty] private string _liveStatus = "⏳ Live connection shuru ho raha hai…";
    [ObservableProperty] private string _userName = string.Empty;
    [ObservableProperty] private string _userRole = string.Empty;
    [ObservableProperty] private bool _isDarkTheme;

    // CHANGED (removed): 7-palette combo + palette properties — ek FIXED maroon-cream theme he.
    // (ThemeHelper.SetDark sirf 🌙 toggle ke liye.)

    // ---- self-update (GitHub Release → school server → this desktop) ----
    [ObservableProperty] private string _appVersionText = $"v{UpdateService.CurrentVersion}";
    [ObservableProperty] private bool _hasUpdate;
    [ObservableProperty] private string _updateLabel = "⬆ Update";
    [ObservableProperty] private bool _isUpdating;

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
        WeakReferenceMessenger.Default.Register<AdminAlertMessage>(this, (_, m) =>
            Toasts.Error($"⚠️ {m.Payload.Title}", m.Payload.Message));

        // CHANGED: update sirf app-start par nahi dete — API uthne ka wait (45s) ke baad
        // ek check, phir har 5 minute mein dobara. (Pehle: app start hotay hi check hota —
        // API tabtak uthti nahi thi → sirf EK silent skip → update kabhi nahi deti.)
        _ = AutoUpdateLoopAsync();
    }

    private async Task AutoUpdateLoopAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(45));
        while (true)
        {
            await CheckForUpdatesAsync();
            if (IsUpdating) return; // download shuru/complete → app khud restart hone wali hai
            await Task.Delay(TimeSpan.FromMinutes(5));
        }
    }

    // CHANGED: auto check plus manual one-click check; manual path bypasses server's GitHub cache.
    private async Task CheckForUpdatesAsync(bool forceRefresh = false, bool manual = false)
    {
        if (!await _updateCheckGate.WaitAsync(0))
        {
            if (manual) UpdateLabel = "⟳ Update check chal raha hai…";
            return;
        }
        try
        {
            if (IsUpdating) return;
            if (manual)
            {
                IsUpdating = true;
                UpdateLabel = "⟳ GitHub check ho raha hai…";
            }

            var (checkSucceeded, has, latest, _) = await UpdateService.Instance.CheckAsync(forceRefresh);
            if (!checkSucceeded)
            {
                if (manual) UpdateLabel = "⚠ Check fail — dobara click karein";
                return;
            }
            if (!has)
            {
                if (manual) UpdateLabel = "✓ Latest version installed";
                return;
            }

            HasUpdate = true;
            IsUpdating = true;
            UpdateLabel = $"⬇ {latest} 0%";
            var progress = new Progress<int>(p => UpdateLabel = $"⬇ {latest} {p}%");
            var ok = await UpdateService.Instance.ApplyAsync(progress);
            if (!ok)
            {
                UpdateLabel = "⚠ Download fail — dobara click karein";
                return;
            }
            UpdateLabel = $"✅ {latest} lag gaya — restart…";
            Toasts.Info("Update complete", $"Naya version {latest} install ho gaya — app dobara khul rahi hai.");
            await Task.Delay(2500);
            System.Windows.Application.Current.Shutdown();
        }
        catch
        {
            if (manual) UpdateLabel = "⚠ Update check fail — dobara click karein";
        }
        finally
        {
            if (UpdateLabel.StartsWith("⚠") || UpdateLabel.StartsWith("✓"))
                IsUpdating = false;
            _updateCheckGate.Release();
        }
    }

    [RelayCommand]
    private Task UpdateNow()
        => CheckForUpdatesAsync(forceRefresh: true, manual: true);


    // CHANGED: nav selection ab Chrome-style tab kholta ya activate karta hai
    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value is null) return;
        OpenOrActivateTab(value);
    }

    // ---- TABS (Chrome-style strip) ----
    public ObservableCollection<TabItem> Tabs { get; } = new();
    [ObservableProperty] private TabItem? _activeTab;
    [ObservableProperty] private bool _isPageFullScreen; // CHANGED: selected module covers app content area
    [ObservableProperty] private bool _isSidebarCollapsed;
    public GridLength SidebarColumnWidth => IsPageFullScreen ? new GridLength(0) : IsSidebarCollapsed ? new GridLength(58) : new GridLength(230);

    partial void OnIsPageFullScreenChanged(bool value) => OnPropertyChanged(nameof(SidebarColumnWidth));
    partial void OnIsSidebarCollapsedChanged(bool value) => OnPropertyChanged(nameof(SidebarColumnWidth));

    [RelayCommand]
    private void ToggleSidebar()
    {
        if (IsPageFullScreen)
        {
            IsPageFullScreen = false;
            IsSidebarCollapsed = true;
        }
        else
        {
            IsSidebarCollapsed = !IsSidebarCollapsed;
        }
    }

    [RelayCommand]
    private void NavigateFromHeader(NavItem item) => SelectedNavItem = item;

    private void OpenOrActivateTab(NavItem nav)
    {
        var existing = Tabs.FirstOrDefault(t => t.Title == nav.Title);
        if (existing is not null)
        {
            ActivateTab(existing);
            return;
        }
        UserControl page;
        try
        {
            page = nav.CreateView();
        }
        catch (Exception ex)
        {
            // Page construct hote hi crash ho to blank na dikhe — tab mein error dikhega + report save.
            _ = Services.BugReportService.SendAsync("Error",
                $"Navigation fail: {nav.Title}", ex.Message, ex.ToString(), "MainViewModel");
            page = new SmartSchool.Admin.Views.Common.ErrorHostView(nav.Title, ex.ToString());
        }
        var tab = new TabItem
        {
            Title = nav.Title,
            IconKind = nav.IconKind,
            IsPinned = nav.Title == "Dashboard",
            CreateView = nav.CreateView,
            PageView = page
        };
        Tabs.Add(tab);
        ActivateTab(tab);
    }

    [RelayCommand]
    private void ActivateTab(TabItem tab)
    {
        ActiveTab = tab;
        foreach (var t in Tabs) t.IsActive = ReferenceEquals(t, tab);
        CurrentView = tab.PageView;
        IsPageFullScreen = !tab.IsPinned; // CHANGED: module full-screen; Dashboard shows menu
    }

    [RelayCommand]
    private void MinimizeTab(TabItem tab)
    {
        // CHANGED: page cached rahe; minimize par menu/Dashboard aur sidebar wapas
        var home = Tabs.FirstOrDefault(t => t.IsPinned);
        if (home is not null) ActivateTab(home);
        IsPageFullScreen = false;
    }

    [RelayCommand]
    private void CloseTab(TabItem tab)
    {
        if (tab.IsPinned) return;
        var idx = Tabs.IndexOf(tab);
        if (idx < 0) return;
        if (tab.PageView?.DataContext is IDisposable d) d.Dispose(); // messenger/SignalR listeners saaf
        Tabs.RemoveAt(idx);
        if (ActiveTab is null || ReferenceEquals(ActiveTab, tab))
        {
            // CHANGED: close returns to the main menu rather than unexpectedly opening another page.
            var home = Tabs.FirstOrDefault(t => t.IsPinned);
            if (home is not null) ActivateTab(home);
            else IsPageFullScreen = false;
        }
    }

    // CHANGED (added): page ke andar ki navigation (list → form → save ke baad NAYA list) agar
    // tab ke page type se milti ho to tab ki cached PageView bhi sync — state hamesha fresh.
    partial void OnCurrentViewChanged(UserControl? value)
    {
        if (ActiveTab is null || value is null || ActiveTab.PageView is null) return;
        if (value.GetType() == ActiveTab.PageView.GetType()) ActiveTab.PageView = value;
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
