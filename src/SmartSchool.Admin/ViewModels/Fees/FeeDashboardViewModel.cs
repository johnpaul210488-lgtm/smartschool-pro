using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Fees;

/// <summary>
/// Fees &amp; payments dashboard: ONE row per student (all fee heads summed: total, paid, balance).
/// Row / Collect → student's collect page (tick fee heads, pay, or full pay).
/// View → student's statement (bills, every payment with date &amp; time, edit / reverse).
/// </summary>
/// <summary>Status dropdown entry. Value is sent to the server; Label is shown.</summary>
public sealed record StatusOption(string Value, string Label);

public partial class FeeDashboardViewModel : ObservableObject
{
    private readonly ApiClient _api = ApiClient.Instance;
    private readonly DispatcherTimer _searchTimer;
    private int _listVersion;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private ClassListItem? _selectedClass;
    [ObservableProperty] private string _statusFilter = "all";
    [ObservableProperty] private int _totalUnpaidCount;
    [ObservableProperty] private decimal _totalUnpaidAmount;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isError;

    public ObservableCollection<StudentFeeSummaryDto> Students { get; } = new();
    public IReadOnlyList<StatusOption> StatusOptions { get; } = new[]
    {
        new StatusOption("all", "Sab"), new StatusOption("paid", "Paid"), new StatusOption("unpaid", "Unpaid"),
        new StatusOption("partial", "Partial"), new StatusOption("due", "Due")
    };
    public ObservableCollection<ClassListItem> Classes { get; } = new();

    /// <summary>Open the collect page for this student.</summary>
    public event Action<StudentFeeSummaryDto>? CollectRequested;
    /// <summary>Open the statement (history) for this student.</summary>
    public event Action<StudentFeeSummaryDto>? ViewStatementRequested;
    /// <summary>Open the fee plan screen.</summary>
    public event Action? ManagePlansRequested;

    public FeeDashboardViewModel()
    {
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            _ = LoadAsync();
        };

        _ = InitAsync();
    }

    partial void OnSearchChanged(string value)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    partial void OnSelectedClassChanged(ClassListItem? value) => _ = LoadAsync();

    partial void OnStatusFilterChanged(string value) => _ = LoadAsync();

    private async Task InitAsync()
    {
        var classes = await _api.ClassesAsync();
        if (classes.Ok && classes.Data is not null)
        {
            Classes.Clear();
            foreach (var c in classes.Data) Classes.Add(c);
        }
        await LoadDuesSummaryAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var version = Interlocked.Increment(ref _listVersion);
        IsBusy = true;
        try
        {
            var term = Search.Trim();
            var r = await _api.StudentFeeSummariesAsync(
                term.Length == 0 ? null : term, SelectedClass?.Id, StatusFilter);
            if (version != _listVersion) return;

            if (!r.Ok || r.Data is null)
            {
                Students.Clear();
                SetMessage(r.Error, true);
                return;
            }

            Students.Clear();
            foreach (var s in r.Data) Students.Add(s);
            SetMessage(Students.Count == 0
                ? (StatusFilter == "all" ? "Koi student nahi mila." : "Is status mein koi student nahi.")
                : $"{Students.Count} student(s). Row par click karein to fee collect karein.", false);
        }
        finally { if (version == _listVersion) IsBusy = false; }
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

    [RelayCommand] private void ClearClassFilter() => SelectedClass = null;

    [RelayCommand] private void Collect(StudentFeeSummaryDto? s) { if (s is not null) CollectRequested?.Invoke(s); }

    [RelayCommand] private void ViewStatement(StudentFeeSummaryDto? s) { if (s is not null) ViewStatementRequested?.Invoke(s); }

    [RelayCommand] private void ManagePlans() => ManagePlansRequested?.Invoke();

    private void SetMessage(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }
}
