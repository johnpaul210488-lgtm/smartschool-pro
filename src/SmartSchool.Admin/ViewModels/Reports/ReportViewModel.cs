using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Reports;

/// <summary>Quick operational reports (monthly collection + dues + today's bus events).</summary>
public partial class ReportViewModel : ObservableObject
{
    private readonly ApiClient _api = ApiClient.Instance;

    [ObservableProperty] private string _month = DateTime.Today.ToString("yyyy-MM");
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private decimal _collected;
    [ObservableProperty] private decimal _unpaid;
    [ObservableProperty] private int _paidCount;
    [ObservableProperty] private int _unpaidCount;
    [ObservableProperty] private int _pickIns;
    [ObservableProperty] private int _dropOuts;

    public ObservableCollection<FeeListItem> Fees { get; } = new();

    public ReportViewModel() => _ = LoadAsync();

    partial void OnMonthChanged(string value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var r = await _api.FeesAsync(month: Month, pageSize: 500);
            if (r.Ok && r.Data is not null)
            {
                Fees.Clear();
                foreach (var f in r.Data.Items) Fees.Add(f);
                Collected = r.Data.Items.Where(f => f.Status == "Paid").Sum(f => f.Amount);
                Unpaid = r.Data.Items.Where(f => f.Status != "Paid").Sum(f => f.Amount);
                PaidCount = r.Data.Items.Count(f => f.Status == "Paid");
                UnpaidCount = r.Data.Items.Count(f => f.Status != "Paid");
            }
            else ToastService.Current.Error("Report", r.Error);

            var att = await _api.TodayAttendanceAsync();
            if (att.Ok && att.Data is not null)
            {
                PickIns = att.Data.Count(e => e.Type == "PickIn");
                DropOuts = att.Data.Count(e => e.Type == "DropOut");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
