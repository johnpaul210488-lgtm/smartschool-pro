using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Fees;

/// <summary>Class templates, student overrides, opt-ins and explicit billing activation.</summary>
public partial class FeePlanViewModel : ObservableObject
{
    private readonly ApiClient _api = ApiClient.Instance;
    private bool _suppressSelectionLoad;
    private int _loadVersion;

    [ObservableProperty] private ClassListItem? _selectedClass;
    [ObservableProperty] private StudentListItem? _selectedStudent;
    [ObservableProperty] private bool _isStudentMode;
    [ObservableProperty] private string _studentSearch = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isPlanActive;
    [ObservableProperty] private DateTime? _effectiveFromDate; // <-- CHANGED: no assumed activation date
    [ObservableProperty] private int _dueDay; // <-- CHANGED: admin must configure
    [ObservableProperty] private int _quarterStartMonth; // <-- CHANGED: admin must configure
    [ObservableProperty] private int _schoolYearStartMonth; // <-- CHANGED: admin must configure

    public ObservableCollection<ClassListItem> Classes { get; } = new();
    public ObservableCollection<StudentListItem> StudentSearchResults { get; } = new();
    public ObservableCollection<FeePlanEditorItem> Items { get; } = new();
    public string[] BillingCycles { get; } = { "Monthly", "Quarterly", "Annually", "OneTime" };
    public int[] Months { get; } = Enumerable.Range(1, 12).ToArray();
    public event Action? BackRequested;

    public FeePlanViewModel() => _ = LoadAsync();

    partial void OnSelectedClassChanged(ClassListItem? value)
    {
        if (value is not null && !_suppressSelectionLoad && !IsStudentMode) _ = LoadClassPlanAsync(value.Id);
    }
    partial void OnSelectedStudentChanged(StudentListItem? value)
    {
        if (value is not null && !_suppressSelectionLoad && IsStudentMode) _ = LoadStudentPlanAsync(value.Id);
    }
    partial void OnIsStudentModeChanged(bool value)
    {
        if (_suppressSelectionLoad) return;
        Items.Clear();
        Message = value ? "Search for a student to configure individual fee overrides." : string.Empty;
        if (!value && SelectedClass is not null) _ = LoadClassPlanAsync(SelectedClass.Id);
        if (value && SelectedStudent is not null) _ = LoadStudentPlanAsync(SelectedStudent.Id);
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
            if (SelectedClass is null && Classes.Count > 0)
            {
                _suppressSelectionLoad = true;
                SelectedClass = Classes[0];
                _suppressSelectionLoad = false;
            }
            if (!IsStudentMode && SelectedClass is not null) await LoadClassPlanAsync(SelectedClass.Id);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SearchStudentsAsync()
    {
        var result = await _api.StudentsAsync(search: StudentSearch, page: 1, pageSize: 50);
        if (!result.Ok || result.Data is null) { SetMessage(result.Error, true); return; }
        StudentSearchResults.Clear();
        foreach (var item in result.Data.Items) StudentSearchResults.Add(item);
        if (StudentSearchResults.Count == 0) SetMessage("No active students matched that search.", false);
    }

    /// <summary>Runs the idempotent billing sweep now instead of waiting for the 30-minute worker tick.</summary>
    [RelayCommand]
    private async Task GenerateNowAsync()
    {
        IsBusy = true; IsError = false;
        try
        {
            var result = await _api.GeneratePlanFeesNowAsync();
            SetMessage(result.Ok ? result.Msg : result.Error, !result.Ok);
            if (result.Ok && !IsStudentMode && SelectedClass is not null) await LoadClassPlanAsync(SelectedClass.Id);
        }
        finally { IsBusy = false; }
    }

    private async Task LoadClassPlanAsync(int classId)
    {
        var version = Interlocked.Increment(ref _loadVersion);
        ResetSchedule(); // <-- CHANGED: don't carry another class's schedule into this one
        IsBusy = true; IsError = false; Message = string.Empty;
        try
        {
            var plan = await _api.ClassFeePlanAsync(classId);
            if (version != _loadVersion) return;
            if (!plan.Ok || plan.Data is null) { SetApiError(plan.Error); return; }
            Items.Clear();
            foreach (var item in plan.Data)
                Items.Add(new FeePlanEditorItem { FeeHeadId = item.FeeHeadId, Name = item.Name, Category = item.Category,
                    Amount = item.Amount, BillingCycle = item.BillingCycle, IsIncluded = item.IsIncluded,
                    IsOptionalForStudent = item.IsOptionalForStudent, IsOverridden = true });
            var schedule = await _api.ClassPlanAssignmentAsync(classId);
            if (version != _loadVersion) return;
            if (schedule.Ok && schedule.Data is not null) ApplySchedule(schedule.Data);
            else SetMessage(schedule.Error, true);
            if (string.IsNullOrWhiteSpace(Message)) SetMessage("Class draft loaded. Activate it only after reviewing amounts and calendar.", false);
        }
        finally { if (version == _loadVersion) IsBusy = false; }
    }

    private async Task LoadStudentPlanAsync(int studentId)
    {
        var version = Interlocked.Increment(ref _loadVersion);
        ResetSchedule(); // <-- CHANGED: don't carry another student's schedule into this one
        IsBusy = true; IsError = false; Message = string.Empty;
        try
        {
            var plan = await _api.StudentFeePlanAsync(studentId);
            if (version != _loadVersion) return;
            if (!plan.Ok || plan.Data is null) { SetApiError(plan.Error); return; }
            Items.Clear();
            foreach (var item in plan.Data)
                Items.Add(new FeePlanEditorItem { FeeHeadId = item.FeeHeadId, Name = item.Name, Category = item.Category,
                    Amount = item.Amount, BillingCycle = item.BillingCycle, IsIncluded = item.IsIncluded,
                    IsOptionalForStudent = item.IsOptionalForStudent, IsOverridden = item.IsOverridden, IsOptedIn = item.IsOptedIn });
            var schedule = await _api.StudentPlanAssignmentAsync(studentId);
            if (version != _loadVersion) return;
            if (schedule.Ok && schedule.Data is not null) ApplySchedule(schedule.Data);
            else SetMessage(schedule.Error, true);
            if (string.IsNullOrWhiteSpace(Message)) SetMessage("Student plan loaded. Unchanged heads inherit the class plan.", false);
        }
        finally { if (version == _loadVersion) IsBusy = false; }
    }

    private void ResetSchedule() // <-- CHANGED: require deliberate admin schedule entry when no saved assignment exists
    {
        IsPlanActive = false;
        EffectiveFromDate = null;
        DueDay = 0;
        QuarterStartMonth = 0;
        SchoolYearStartMonth = 0;
    }

    private void ApplySchedule(FeePlanAssignmentDto schedule)
    {
        IsPlanActive = schedule.IsActive;
        EffectiveFromDate = schedule.EffectiveFrom.ToDateTime(TimeOnly.MinValue);
        DueDay = schedule.DueDay;
        QuarterStartMonth = schedule.QuarterStartMonth;
        SchoolYearStartMonth = schedule.SchoolYearStartMonth;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsStudentMode ? SelectedStudent is null : SelectedClass is null)
        { SetMessage(IsStudentMode ? "Search and select a student first." : "Select a class first.", true); return; }
        if (EffectiveFromDate is null || DueDay is < 1 or > 28 || QuarterStartMonth is < 1 or > 12 || SchoolYearStartMonth is < 1 or > 12)
        { SetMessage("Set an effective date, due day (1–28), and calendar start months (1–12).", true); return; }
        if (Items.Any(x => x.Amount < 0 || x.Amount > 10_000_000m || !BillingCycles.Contains(x.BillingCycle)))
        { SetMessage("Check amounts and billing cycles; amount must be 0–10,000,000.", true); return; }

        IsBusy = true; IsError = false;
        try
        {
            if (IsStudentMode)
            {
                var request = new SaveStudentFeePlanRequest { Items = Items.Select(x => new SaveStudentFeePlanItemRequest
                { FeeHeadId = x.FeeHeadId, IsOverridden = x.IsOverridden, IsOptedIn = x.IsOptionalForStudent && x.IsOptedIn,
                  IsIncluded = x.IsIncluded, Amount = x.Amount, BillingCycle = x.BillingCycle }).ToList() };
                var saved = await _api.SaveStudentFeePlanAsync(SelectedStudent!.Id, request);
                if (!saved.Ok) { SetMessage(saved.Error, true); return; }
                var schedule = await _api.SaveStudentPlanAssignmentAsync(SelectedStudent.Id, CreateScheduleRequest());
                SetMessage(schedule.Ok ? "Student overrides and opt-ins saved. Future periods only." : schedule.Error, !schedule.Ok);
            }
            else
            {
                var request = new SaveClassFeePlanRequest { Items = Items.Select(x => new SaveClassFeePlanItemRequest
                { FeeHeadId = x.FeeHeadId, BillingCycle = x.BillingCycle, Amount = x.Amount,
                  IsIncluded = x.IsIncluded, IsOptionalForStudent = x.IsOptionalForStudent }).ToList() };
                var saved = await _api.SaveClassFeePlanAsync(SelectedClass!.Id, request);
                if (!saved.Ok) { SetMessage(saved.Error, true); return; }
                var schedule = await _api.SaveClassPlanAssignmentAsync(SelectedClass.Id, CreateScheduleRequest());
                SetMessage(schedule.Ok ? (IsPlanActive ? "Class plan activated. Automatic bills start from its effective date." : "Class draft saved; it is inactive and creates no bills.") : schedule.Error, !schedule.Ok);
            }
        }
        finally { IsBusy = false; }
    }

    private SaveFeePlanAssignmentRequest CreateScheduleRequest() => new()
    {
        IsActive = IsPlanActive,
        EffectiveFrom = DateOnly.FromDateTime(EffectiveFromDate!.Value),
        DueDay = DueDay,
        QuarterStartMonth = QuarterStartMonth,
        SchoolYearStartMonth = SchoolYearStartMonth
    };

    [RelayCommand] private void Back() => BackRequested?.Invoke();

    private void SetApiError(string error)
    {
        if (error.Contains("404", StringComparison.OrdinalIgnoreCase))
            SetMessage("Fee Plan API is missing on the server. Install the School Server/API update; Admin-only updates are not enough.", true);
        else SetMessage(error, true);
    }
    private void SetMessage(string message, bool isError) { Message = message; IsError = isError; }
}

/// <summary>One editable row of the plan grid (notifies the UI so edits stay visible).</summary>
public partial class FeePlanEditorItem : ObservableObject
{
    [ObservableProperty] private int _feeHeadId;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _category = string.Empty;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private string _billingCycle = "Monthly";
    [ObservableProperty] private bool _isIncluded;
    [ObservableProperty] private bool _isOptionalForStudent;
    [ObservableProperty] private bool _isOverridden;
    [ObservableProperty] private bool _isOptedIn;
}
