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
    /// <summary>Set when the server is older than this app (or has no version endpoint, so new screens get 404).</summary>
    [ObservableProperty] private string _serverWarning = string.Empty;
    [ObservableProperty] private int _totalUnpaidCount;
    [ObservableProperty] private decimal _totalUnpaidAmount;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isError;

    public BulkObservableCollection<StudentFeeSummaryDto> Students { get; } = new();
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
        // Version, classes aur dues ek sath mangwayein. List tab load hogi jab version warning tayyar ho
        // (warning list ke message mein shamil hoti hai), is liye list version ke baad chalti hai.
        var versionTask = CheckServerVersionAsync();
        var classesTask = _api.ClassesAsync();
        var duesTask = LoadDuesSummaryAsync();

        await versionTask;
        var listTask = LoadAsync();

        var classes = await classesTask;
        if (classes.Ok && classes.Data is not null)
        {
            Classes.Clear();
            foreach (var c in classes.Data) Classes.Add(c);
        }
        await duesTask;
        await listTask;
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

            Students.ReplaceAll(r.Data.Where(s => StatusFilter == "all"
                || string.Equals(s.StatusText, StatusLabelFor(StatusFilter), StringComparison.OrdinalIgnoreCase)).ToList());
            var summary = Students.Count == 0
                ? (StatusFilter == "all" ? "Koi student nahi mila." : "Is status mein koi student nahi.")
                : $"{Students.Count} student(s). Row par click karein to fee collect karein.";
            SetMessage(ServerWarning.Length > 0 ? $"{ServerWarning}  ·  {summary}" : summary, ServerWarning.Length > 0);
        }
        finally { if (version == _listVersion) IsBusy = false; }
    }

    private async Task CheckServerVersionAsync()
    {
        var app = NormalizeVersion(UpdateService.CurrentVersion);
        var r = await _api.GetAsync<ServerVersionDto>("api/system/version");
        if (!r.Ok || r.Data is null)
            ServerWarning = $"Server purana hai (v{app} ki API nahi mili). Server installer v{app} chalayein, warna fee screens par 404 aayega.";
        else if (NormalizeVersion(r.Data.Version) != app)
            ServerWarning = $"Server v{NormalizeVersion(r.Data.Version)} hai, app v{app} hai. Server installer v{app} chalayein.";
        else
            ServerWarning = string.Empty;
    }

    private static string NormalizeVersion(string? v) => (v ?? string.Empty).Split('+')[0].Trim().TrimStart('v', 'V');

    private static string StatusLabelFor(string filter) => filter switch
    {
        "paid" => "Paid",
        "unpaid" => "Unpaid",
        "partial" => "Partial",
        "due" => "Due",
        _ => string.Empty
    };

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
