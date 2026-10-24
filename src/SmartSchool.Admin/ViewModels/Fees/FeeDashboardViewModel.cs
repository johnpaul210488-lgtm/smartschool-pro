using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Fees;

public partial class FeeDashboardViewModel : ObservableObject
{
    private readonly ApiClient _api = ApiClient.Instance;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusFilter = "";
    [ObservableProperty] private string _monthFilter = DateTime.Today.ToString("yyyy-MM");
    [ObservableProperty] private int _totalUnpaidCount;
    [ObservableProperty] private decimal _totalUnpaidAmount;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages = 1;

    public ObservableCollection<FeeListItem> Fees { get; } = new();
    public string[] StatusFilters { get; } = { "", "Pending", "Paid", "Overdue", "Partial" };

    /// <summary>Fired when the user wants to collect a fee (opens collection page).</summary>
    public event Action<FeeListItem>? CollectRequested;

    private bool _reloadPending; // CHANGED: filters/paging clicked while loading are queued, not ignored

    public FeeDashboardViewModel()
    {
        _ = LoadAsync();
        _ = LoadDuesSummaryAsync(); // CHANGED: independent request; does not block filter/page clicks
    }

    partial void OnStatusFilterChanged(string value) { Page = 1; _ = LoadAsync(); }
    partial void OnMonthFilterChanged(string value) { Page = 1; _ = LoadAsync(); }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            _reloadPending = true;
            return;
        }
        IsBusy = true;
        try
        {
            do
            {
                _reloadPending = false;
                var month = MonthFilter;
                var status = string.IsNullOrEmpty(StatusFilter) ? null : StatusFilter;
                var page = Page;
                var r = await _api.FeesAsync(month: month, status: status, page: page, pageSize: 50);
                if (r.Ok && r.Data is not null)
                {
                    Fees.Clear();
                    foreach (var f in r.Data.Items) Fees.Add(f);
                    TotalPages = Math.Max(1, r.Data.TotalPages);
                }
                else
                {
                    ToastService.Current.Error("Fees", r.Error);
                }
            } while (_reloadPending);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDuesSummaryAsync()
    {
        var dues = await _api.DuesAsync();
        if (dues.Ok && dues.Data is not null)
        {
            TotalUnpaidCount = dues.Data.TotalUnpaidCount;
            TotalUnpaidAmount = dues.Data.TotalUnpaidAmount;
        }
    }

    [RelayCommand] private void NextPage() { if (Page < TotalPages) { Page++; _ = LoadAsync(); } }
    [RelayCommand] private void PrevPage() { if (Page > 1) { Page--; _ = LoadAsync(); } }

    [RelayCommand] private void Collect(FeeListItem? f) { if (f is not null) CollectRequested?.Invoke(f); }
}
