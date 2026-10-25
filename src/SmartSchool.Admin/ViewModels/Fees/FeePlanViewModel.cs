using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Fees;

/// <summary>
/// Fee plan screen:
///  1. Scope: whole class, or one student (live search by name / roll no).
///  2. Period tabs: Monthly / Quarterly / Yearly / One-time → only that period's fee heads are listed.
///  3. Tick the fees you want, edit amounts, then Save (draft) or Save &amp; activate.
///     Activate creates bills for the current period right away (class or student), and the
///     background worker keeps billing future periods.
///  4. For a student the balance is shown; payments are taken on the Fee &amp; balance page.
/// </summary>
public partial class FeePlanViewModel : ObservableObject
{
    private readonly ApiClient _api = ApiClient.Instance;
    private readonly DispatcherTimer _searchTimer;
    private bool _suppressSelectionLoad;
    private int _loadVersion;
    private int _listVersion;

    [ObservableProperty] private bool _isStudentMode;
    [ObservableProperty] private ClassListItem? _selectedClass;
    [ObservableProperty] private string _studentSearch = string.Empty;
    [ObservableProperty] private StudentListItem? _selectedStudent;
    [ObservableProperty] private string _selectedCycle = "Monthly"; // UI label: Monthly | Quarterly | Yearly | One-time
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isPlanActive;
    [ObservableProperty] private DateTime? _effectiveFromDate;
    [ObservableProperty] private int _dueDay = 10;
    [ObservableProperty] private int _quarterStartMonth = 1;
    [ObservableProperty] private int _schoolYearStartMonth = 4;

    public ObservableCollection<ClassListItem> Classes { get; } = new();
    public ObservableCollection<StudentListItem> Students { get; } = new();
    public ObservableCollection<FeePlanEditorItem> Items { get; } = new();
    public ICollectionView FilteredItems { get; }
    public int[] Months { get; } = Enumerable.Range(1, 12).ToArray();

    public event Action? BackRequested;

    public FeePlanViewModel()
    {
        FilteredItems = CollectionViewSource.GetDefaultView(Items);
        FilteredItems.Filter = o => o is FeePlanEditorItem row && FeePlanCycles.ToUi(row.BillingCycle) == SelectedCycle;

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            _ = RefreshStudentListAsync();
        };

        _ = LoadAsync();
    }

    partial void OnSelectedCycleChanged(string value) => FilteredItems.Refresh();

    partial void OnIsStudentModeChanged(bool value)
    {
        if (_suppressSelectionLoad) return;
        Items.Clear();
        Students.Clear();
        SetMessage(string.Empty, false);

        _suppressSelectionLoad = true;
        SelectedStudent = null;
        // Student search looks across all classes; the class box becomes an optional filter.
        if (value) SelectedClass = null;
        _suppressSelectionLoad = false;

        if (value)
        {
            SetMessage("Naam ya roll number likhein, phir student chunein.", false);
        }
        else
        {
            _suppressSelectionLoad = true;
            if (SelectedClass is null && Classes.Count > 0) SelectedClass = Classes[0];
            _suppressSelectionLoad = false;
            if (SelectedClass is not null) _ = LoadClassPlanAsync(SelectedClass.Id);
        }
    }

    partial void OnSelectedClassChanged(ClassListItem? value)
    {
        if (_suppressSelectionLoad) return;
        if (IsStudentMode)
        {
            _ = RefreshStudentListAsync();
            return;
        }
        if (value is null)
        {
            Items.Clear();
            return;
        }
        _ = LoadClassPlanAsync(value.Id);
    }

    partial void OnStudentSearchChanged(string value)
    {
        if (!IsStudentMode) return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    partial void OnSelectedStudentChanged(StudentListItem? value)
    {
        if (_suppressSelectionLoad) return;
        if (value is null || !IsStudentMode) return;
        _ = LoadStudentPlanAsync(value.Id);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _api.ClassesAsync();
            if (!result.Ok || result.Data is null) { SetMessage(result.Error, true); return; }
            Classes.Clear();
            foreach (var item in result.Data) Classes.Add(item);

            if (!IsStudentMode && SelectedClass is null && Classes.Count > 0)
            {
                _suppressSelectionLoad = true;
                SelectedClass = Classes[0];
                _suppressSelectionLoad = false;
                await LoadClassPlanAsync(SelectedClass.Id);
            }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ClearClassFilter() => SelectedClass = null;

    /// <summary>Student list = search term (name / roll / father / phone), narrowed by the class box.</summary>
    private async Task RefreshStudentListAsync()
    {
        if (!IsStudentMode) return;
        var version = Interlocked.Increment(ref _listVersion);
        var term = StudentSearch.Trim();
        var classId = SelectedClass?.Id;

        if (term.Length == 0 && classId is null)
        {
            Students.Clear();
            SetMessage("Naam ya roll number likhein, ya upar se class chunein.", false);
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _api.StudentsAsync(search: term.Length == 0 ? null : term, classId: classId, pageSize: 100);
            if (version != _listVersion) return;
            if (!result.Ok || result.Data is null) { SetMessage(result.Error, true); return; }

            Students.Clear();
            foreach (var s in result.Data.Items) Students.Add(s);
            SetMessage(Students.Count == 0
                ? "Koi active student nahi mila."
                : $"{Students.Count} student(s) mile. Kisi par click karein.", false);
        }
        finally { if (version == _listVersion) IsBusy = false; }
    }

    /// <summary>Class plan: every fee head with its amount, included flag and billing period.</summary>
    private async Task LoadClassPlanAsync(int classId)
    {
        var version = Interlocked.Increment(ref _loadVersion);
        ResetSchedule();
        IsBusy = true; IsError = false; Message = string.Empty;
        try
        {
            var plan = await _api.ClassFeePlanAsync(classId);
            if (version != _loadVersion) return;
            if (!plan.Ok || plan.Data is null) { SetApiError(plan.Error); return; }

            Items.Clear();
            foreach (var item in plan.Data)
            {
                var row = new FeePlanEditorItem
                {
                    FeeHeadId = item.FeeHeadId, Name = item.Name, Category = item.Category,
                    DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Name : item.DisplayName!,
                    Amount = item.Amount, BillingCycle = FeePlanCycles.NormalizeServer(item.BillingCycle),
                    IsIncluded = item.IsIncluded, IsOptionalForStudent = item.IsOptionalForStudent
                };
                row.SavedName = row.DisplayName;
                row.PropertyChanged += OnRowNameChanged;
                Items.Add(row);
            }

            var schedule = await _api.ClassPlanAssignmentAsync(classId);
            if (version != _loadVersion) return;
            if (schedule.Ok && schedule.Data is not null) ApplySchedule(schedule.Data);
            else SetMessage(schedule.Error, true);

            if (string.IsNullOrWhiteSpace(Message))
                SetMessage($"{SelectedClass?.DisplayName}: plan loaded. Fee tick karein, amount check karein, phir Save ya Save & activate karein.", false);
        }
        finally { if (version == _loadVersion) IsBusy = false; }
    }

    /// <summary>Student plan: the effective plan (class values + this student's overrides) and balance.</summary>
    private async Task LoadStudentPlanAsync(int studentId)
    {
        var version = Interlocked.Increment(ref _loadVersion);
        ResetSchedule();
        IsBusy = true; IsError = false; Message = string.Empty;
        try
        {
            var plan = await _api.StudentFeePlanAsync(studentId);
            if (version != _loadVersion) return;
            if (!plan.Ok || plan.Data is null) { SetApiError(plan.Error); return; }

            Items.Clear();
            foreach (var item in plan.Data)
            {
                var row = new FeePlanEditorItem
                {
                    FeeHeadId = item.FeeHeadId, Name = item.Name, Category = item.Category,
                    Amount = item.Amount, BillingCycle = FeePlanCycles.NormalizeServer(item.BillingCycle),
                    IsIncluded = item.IsIncluded, IsOptionalForStudent = item.IsOptionalForStudent,
                    IsOverridden = item.IsOverridden, IsOptedIn = item.IsOptedIn
                };
                row.SavedName = row.DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Name : item.DisplayName!;
                row.PropertyChanged += OnStudentRowChanged;
                row.PropertyChanged += OnRowNameChanged;
                Items.Add(row);
            }

            var schedule = await _api.StudentPlanAssignmentAsync(studentId);
            if (version != _loadVersion) return;
            if (schedule.Ok && schedule.Data is not null) ApplySchedule(schedule.Data);
            else SetMessage(schedule.Error, true);

            if (string.IsNullOrWhiteSpace(Message))
                SetMessage("Student plan loaded. Jo fee aap badlein gi woh sirf is student par lagegi.", false);
        }
        finally { if (version == _loadVersion) IsBusy = false; }
    }

    /// <summary>
    /// Wording of a row. Class mode: saved immediately for this class (next period's bills use it).
    /// Student mode: the name belongs to the class, so the edit is reverted with a hint.
    /// </summary>
    private async void OnRowNameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FeePlanEditorItem.DisplayName) || sender is not FeePlanEditorItem row) return;
        if (row.DisplayName == row.SavedName) return;

        if (IsStudentMode || SelectedClass is null)
        {
            row.DisplayName = row.SavedName; // reverts and re-enters here with equal values, which returns above
            SetMessage("Naam badalne ke liye scope 'Puri class' chunein. Naam poori class ke liye save hota hai.", true);
            return;
        }

        var label = row.DisplayName.Trim();
        var storedLabel = label.Length == 0 ? null : label;
        var result = await _api.RenameClassFeeHeadAsync(SelectedClass.Id, row.FeeHeadId, storedLabel);
        if (!result.Ok)
        {
            row.DisplayName = row.SavedName;
            SetMessage(result.Error, true);
            return;
        }

        var shown = storedLabel ?? row.Name;
        row.SavedName = shown;
        if (row.DisplayName != shown) row.DisplayName = shown; // trims or resets empty to catalogue name
        SetMessage($"'{shown}' save ho gaya. Agle bills isi naam se banenge.", false);
    }

    [ObservableProperty] private string _newRowName = string.Empty;
    [ObservableProperty] private string _newRowCategory = "Other";
    [ObservableProperty] private string _newRowAmount = string.Empty;

    /// <summary>Manual row: adds a fee that is not in the catalogue. Tick it and Save to store the amount.</summary>
    [RelayCommand]
    private async Task AddRowAsync()
    {
        var name = (NewRowName ?? string.Empty).Trim();
        if (name.Length < 2) { SetMessage("Row ka naam kam az kam 2 harf ka likhein.", true); return; }

        var amountText = (NewRowAmount ?? string.Empty).Trim();
        decimal amount = 0m;
        if (amountText.Length > 0 && !decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
        { SetMessage("Amount sirf number mein likhein.", true); return; }
        if (amount < 0 || amount > 10_000_000m) { SetMessage("Amount 0 se 10,000,000 ke andar hona chahiye.", true); return; }

        IsBusy = true; IsError = false;
        try
        {
            var created = await _api.CreateCustomFeeHeadAsync(name, string.IsNullOrWhiteSpace(NewRowCategory) ? "Other" : NewRowCategory.Trim());
            if (!created.Ok || created.Data is null) { SetMessage(created.Error, true); return; }

            var row = new FeePlanEditorItem
            {
                FeeHeadId = created.Data.Id, Name = created.Data.Name, Category = created.Data.Category,
                DisplayName = created.Data.Name, SavedName = created.Data.Name,
                Amount = amount, BillingCycle = FeePlanCycles.NormalizeServer(SelectedCycle),
                IsIncluded = true, IsOptionalForStudent = false,
                IsOverridden = IsStudentMode, IsOptedIn = false
            };
            row.PropertyChanged += OnRowNameChanged;
            if (IsStudentMode) row.PropertyChanged += OnStudentRowChanged;
            Items.Add(row);

            NewRowName = string.Empty;
            NewRowAmount = string.Empty;
            SetMessage($"'{row.Name}' row add ho gaya. Amount check karke Save (draft) ya Save & activate dabayein.", false);
        }
        finally { IsBusy = false; }
    }

    /// <summary>Editing a row for one student stores an override (class value is no longer used for that row).</summary>
    private void OnStudentRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsStudentMode || sender is not FeePlanEditorItem row) return;
        if (e.PropertyName is nameof(FeePlanEditorItem.Amount) or nameof(FeePlanEditorItem.IsIncluded))
            row.IsOverridden = true;
    }

    /// <summary>Defaults shown until a saved schedule is loaded (same defaults the server uses).</summary>
    private void ResetSchedule()
    {
        IsPlanActive = false;
        EffectiveFromDate = DateTime.Today;
        DueDay = 10;
        QuarterStartMonth = 1;
        SchoolYearStartMonth = 4;
    }

    private void ApplySchedule(FeePlanAssignmentDto schedule)
    {
        IsPlanActive = schedule.IsActive;
        EffectiveFromDate = schedule.EffectiveFrom.ToDateTime(TimeOnly.MinValue);
        DueDay = schedule.DueDay;
        QuarterStartMonth = schedule.QuarterStartMonth;
        SchoolYearStartMonth = schedule.SchoolYearStartMonth;
    }

    [RelayCommand] private Task SaveDraftAsync() => SaveAsync(activate: false);

    [RelayCommand]
    private async Task SaveAndActivateAsync()
    {
        var target = IsStudentMode ? SelectedStudent?.FullName : SelectedClass?.DisplayName;
        if (target is null) { SetMessage(IsStudentMode ? "Pehle student chunein." : "Pehle class chunein.", true); return; }

        var ticked = Items.Count(x => x.IsIncluded && x.Amount > 0m);
        if (ticked == 0) { SetMessage("Kam az kam ek fee tick karein aur amount likhein.", true); return; }

        var who = IsStudentMode ? $"student {target}" : $"poori class {target} ({Students.Count} students)";
        var confirm = MessageBox.Show(
            $"Yeh plan {who} par activate hoga.\n\n" +
            $"{ticked} fee head(s) ka current period ka bill foran ban jayega, aur aage ke periods bhi automatic banenge.\n\n" +
            "Continue karein?",
            "Confirm: Plan activate",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        await SaveAsync(activate: true);
    }

    private async Task<bool> SaveAsync(bool activate)
    {
        if (IsStudentMode ? SelectedStudent is null : SelectedClass is null)
        {
            SetMessage(IsStudentMode ? "Pehle student chunein." : "Pehle class chunein.", true);
            return false;
        }
        if (DueDay is < 1 or > 28 || QuarterStartMonth is < 1 or > 12 || SchoolYearStartMonth is < 1 or > 12)
        {
            SetMessage("Due day 1–28 ho, aur calendar months 1–12 ke andar hon.", true);
            return false;
        }
        if (Items.Any(x => x.Amount < 0 || x.Amount > 10_000_000m || !FeePlanCycles.ServerValues.Contains(x.BillingCycle)))
        {
            SetMessage("Amount 0 se 10,000,000 ke andar hona chahiye.", true);
            return false;
        }

        IsBusy = true; IsError = false;
        try
        {
            // Activation always starts from today (server rule: no backdated bills).
            IsPlanActive = activate;
            EffectiveFromDate = activate || EffectiveFromDate is null ? DateTime.Today : EffectiveFromDate;
            var schedule = CreateScheduleRequest();

            if (IsStudentMode)
            {
                var request = new SaveStudentFeePlanRequest
                {
                    Items = Items.Select(x => new SaveStudentFeePlanItemRequest
                    {
                        FeeHeadId = x.FeeHeadId, IsOverridden = x.IsOverridden,
                        IsOptedIn = x.IsOptionalForStudent && x.IsOptedIn,
                        IsIncluded = x.IsIncluded, Amount = x.Amount, BillingCycle = x.BillingCycle
                    }).ToList()
                };
                var saved = await _api.SaveStudentFeePlanAsync(SelectedStudent!.Id, request);
                if (!saved.Ok) { SetMessage(saved.Error, true); return false; }
                var sched = await _api.SaveStudentPlanAssignmentAsync(SelectedStudent.Id, schedule);
                if (!sched.Ok) { SetMessage(sched.Error, true); return false; }
            }
            else
            {
                var request = new SaveClassFeePlanRequest
                {
                    Items = Items.Select(x => new SaveClassFeePlanItemRequest
                    {
                        FeeHeadId = x.FeeHeadId, BillingCycle = x.BillingCycle, Amount = x.Amount,
                        IsIncluded = x.IsIncluded, IsOptionalForStudent = x.IsOptionalForStudent
                    }).ToList()
                };
                var saved = await _api.SaveClassFeePlanAsync(SelectedClass!.Id, request);
                if (!saved.Ok) { SetMessage(saved.Error, true); return false; }
                var sched = await _api.SaveClassPlanAssignmentAsync(SelectedClass.Id, schedule);
                if (!sched.Ok) { SetMessage(sched.Error, true); return false; }
            }

            if (!activate)
            {
                SetMessage("Draft save ho gaya. Is plan se abhi koi bill nahi bana.", false);
                return true;
            }

            // Issue the current period's bills now instead of waiting for the 30-minute worker.
            var gen = await _api.GeneratePlanFeesNowAsync();
            SetMessage(gen.Ok
                ? $"Plan activate ho gaya. {gen.Msg}"
                : $"Plan save ho gaya, lekin bills abhi nahi bane: {gen.Error}. 'Bills abhi banayein' dabayein.", !gen.Ok);

            return gen.Ok;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task GenerateNowAsync()
    {
        IsBusy = true; IsError = false;
        try
        {
            var result = await _api.GeneratePlanFeesNowAsync();
            SetMessage(result.Ok ? result.Msg : result.Error, !result.Ok);
        }
        finally { IsBusy = false; }
    }


    [RelayCommand] private void Back() => BackRequested?.Invoke();

    private SaveFeePlanAssignmentRequest CreateScheduleRequest() => new()
    {
        IsActive = IsPlanActive,
        EffectiveFrom = DateOnly.FromDateTime(EffectiveFromDate ?? DateTime.Today),
        DueDay = DueDay,
        QuarterStartMonth = QuarterStartMonth,
        SchoolYearStartMonth = SchoolYearStartMonth
    };

    private void SetApiError(string error)
    {
        if (error.Contains("404", StringComparison.OrdinalIgnoreCase))
            SetMessage("Fee plan API server par nahi mili. Server (installer) ko update karein; sirf Admin update kafi nahi.", true);
        else SetMessage(error, true);
    }

    private void SetMessage(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }
}

/// <summary>Maps between server billing values and the UI period labels.</summary>
public static class FeePlanCycles
{
    /// <summary>Values the server accepts for a plan line.</summary>
    public static readonly string[] ServerValues = { "Monthly", "Quarterly", "Annually", "OneTime" };

    public static string ToUi(string? server) => server switch
    {
        "Annually" => "Yearly",
        "OneTime" => "One-time",
        "Quarterly" or "PerTerm" => "Quarterly",
        _ => "Monthly"
    };

    public static string NormalizeServer(string? server) => ToUi(server) switch
    {
        "Yearly" => "Annually",
        "One-time" => "OneTime",
        "Quarterly" => "Quarterly",
        _ => "Monthly"
    };
}

/// <summary>One editable row of the plan grid.</summary>
public partial class FeePlanEditorItem : ObservableObject
{
    [ObservableProperty] private int _feeHeadId;
    [ObservableProperty] private string _name = string.Empty;
    /// <summary>Wording shown and edited in the plan (class-specific). Defaults to Name.</summary>
    [ObservableProperty] private string _displayName = string.Empty;
    /// <summary>Last wording stored on the server; used to revert an edit that could not be saved.</summary>
    public string SavedName { get; set; } = string.Empty;
    [ObservableProperty] private string _category = string.Empty;
    [ObservableProperty] private decimal _amount;
    /// <summary>Server value: Monthly, Quarterly, Annually or OneTime.</summary>
    [ObservableProperty] private string _billingCycle = "Monthly";
    [ObservableProperty] private bool _isIncluded;
    [ObservableProperty] private bool _isOptionalForStudent;
    [ObservableProperty] private bool _isOverridden;
    [ObservableProperty] private bool _isOptedIn;

    /// <summary>Period label shown in the grid.</summary>
    public string PeriodLabel => FeePlanCycles.ToUi(BillingCycle);
}
