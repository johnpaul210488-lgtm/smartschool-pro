using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Fees;

/// <summary>
/// Simple fee screen:
///  1. Class-wise or Single student (search by name / roll no).
///  2. Pick Monthly / Quarterly / Yearly / One-time → the fees of that type are listed.
///  3. Tick the fees and enter amounts → "Add to balance" bills them to the class or student.
///  4. Click a student → full fee history (what is paid, what is due, every payment with date & time),
///     printable / PDF, and WhatsApp share.
/// </summary>
public partial class FeeChargeViewModel : ObservableObject
{
    private readonly ApiClient _api = ApiClient.Instance;
    private readonly DispatcherTimer _searchTimer;
    private int _itemsVersion;
    private int _historyVersion;

    [ObservableProperty] private bool _isStudentMode;
    [ObservableProperty] private ClassListItem? _selectedClass;
    [ObservableProperty] private string _studentSearch = string.Empty;
    [ObservableProperty] private StudentListItem? _selectedStudent;
    [ObservableProperty] private string _selectedCycle = "Monthly";
    [ObservableProperty] private DateTime? _periodDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _dueDate = DateTime.Today.AddDays(10);
    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isError;

    // History panel (right side)
    [ObservableProperty] private StudentFeeHistoryDto? _history;
    [ObservableProperty] private bool _hasHistory;
    [ObservableProperty] private string _historyTitle = string.Empty;
    [ObservableProperty] private string _historyInfo = string.Empty;
    [ObservableProperty] private decimal _totalAmount;
    [ObservableProperty] private decimal _totalPaid;
    [ObservableProperty] private decimal _totalBalance;

    public ObservableCollection<ClassListItem> Classes { get; } = new();
    public ObservableCollection<StudentListItem> Students { get; } = new();
    public ObservableCollection<ChargeLine> FeeLines { get; } = new();
    public ICollectionView FeeLinesView { get; }
    public ObservableCollection<FeeHistoryRowDto> HistoryFees { get; } = new();
    public ObservableCollection<FeeHistoryPaymentDto> HistoryPayments { get; } = new();

    public event Action? BackRequested;
    public event Action? AutoBillingRequested;

    public FeeChargeViewModel()
    {
        FeeLinesView = CollectionViewSource.GetDefaultView(FeeLines);
        FeeLinesView.Filter = o => o is ChargeLine line && line.Cycle == SelectedCycle;

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            _ = SearchStudentsAsync();
        };

        _ = InitAsync();
    }

    partial void OnSelectedCycleChanged(string value)
    {
        FeeLinesView.Refresh();
        UpdateSummary();
    }

    partial void OnIsStudentModeChanged(bool value)
    {
        Students.Clear();
        SelectedStudent = null;
        FeeLines.Clear();
        ClearHistory();
        SetMessage(string.Empty, false);

        if (value)
        {
            SetMessage("Naam ya roll number likh kar student search karein, phir student par click karein.", false);
        }
        else if (SelectedClass is not null)
        {
            _ = LoadClassAsync(SelectedClass.Id);
        }
        UpdateSummary();
    }

    partial void OnSelectedClassChanged(ClassListItem? value)
    {
        if (IsStudentMode || value is null) return;
        _ = LoadClassAsync(value.Id);
    }

    partial void OnStudentSearchChanged(string value)
    {
        if (!IsStudentMode) return;
        _searchTimer.Stop();
        if (string.IsNullOrWhiteSpace(value))
        {
            Students.Clear();
            return;
        }
        _searchTimer.Start();
    }

    partial void OnSelectedStudentChanged(StudentListItem? value)
    {
        if (value is null)
        {
            ClearHistory();
            if (IsStudentMode) FeeLines.Clear();
            UpdateSummary();
            return;
        }

        _ = LoadHistoryAsync(value.Id);
        if (IsStudentMode) _ = LoadStudentFeeLinesAsync(value.Id);
        UpdateSummary();
    }

    private async Task InitAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _api.ClassesAsync();
            if (!result.Ok || result.Data is null) { SetMessage(result.Error, true); return; }
            Classes.Clear();
            foreach (var c in result.Data) Classes.Add(c);
            SelectedClass = Classes.FirstOrDefault();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (IsStudentMode)
        {
            if (SelectedStudent is not null) await LoadHistoryAsync(SelectedStudent.Id);
            return;
        }
        if (SelectedClass is not null) await LoadClassAsync(SelectedClass.Id);
    }

    /// <summary>Class mode: class fee plan (amounts per fee) and the class's students.</summary>
    private async Task LoadClassAsync(int classId)
    {
        var version = Interlocked.Increment(ref _itemsVersion);
        IsBusy = true;
        SetMessage(string.Empty, false);
        try
        {
            var plan = await _api.ClassFeePlanAsync(classId);
            if (version != _itemsVersion) return;
            if (!plan.Ok || plan.Data is null) { SetMessage(plan.Error, true); return; }

            ReplaceFeeLines(plan.Data.Select(x => new ChargeLine(
                x.FeeHeadId, x.Name, x.Category, ToUiCycle(x.BillingCycle), x.Amount,
                isSelected: x.IsIncluded && x.Amount > 0m && !x.IsOptionalForStudent)));

            var students = await _api.StudentsAsync(classId: classId, pageSize: 300);
            if (version != _itemsVersion) return;
            Students.Clear();
            if (students.Ok && students.Data is not null)
                foreach (var s in students.Data.Items) Students.Add(s);

            SelectedStudent = null; // history resets when the class changes
            ClearHistory();
            UpdateSummary();
            SetMessage($"{Students.Count} student(s) in this class. Fee tick karein aur 'Balance mein add' dabayein.", false);
        }
        finally { if (version == _itemsVersion) IsBusy = false; }
    }

    /// <summary>Student mode: the student's own effective fee plan (class amounts + overrides + opt-ins).</summary>
    private async Task LoadStudentFeeLinesAsync(int studentId)
    {
        var version = Interlocked.Increment(ref _itemsVersion);
        IsBusy = true;
        SetMessage(string.Empty, false);
        try
        {
            var plan = await _api.StudentFeePlanAsync(studentId);
            if (version != _itemsVersion) return;
            if (!plan.Ok || plan.Data is null) { SetMessage(plan.Error, true); return; }

            ReplaceFeeLines(plan.Data.Select(x => new ChargeLine(
                x.FeeHeadId, x.Name, x.Category, ToUiCycle(x.BillingCycle), x.Amount,
                isSelected: x.IsIncluded && x.Amount > 0m && (!x.IsOptionalForStudent || x.IsOptedIn))));
            SetMessage("Fee tick karein, amount check karein, phir 'Balance mein add' dabayein.", false);
        }
        finally { if (version == _itemsVersion) IsBusy = false; }
    }

    private void ReplaceFeeLines(IEnumerable<ChargeLine> lines)
    {
        foreach (var old in FeeLines) old.PropertyChanged -= OnLinePropertyChanged;
        FeeLines.Clear();
        foreach (var line in lines)
        {
            line.PropertyChanged += OnLinePropertyChanged;
            FeeLines.Add(line);
        }
        FeeLinesView.Refresh();
        UpdateSummary();
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChargeLine.IsSelected) or nameof(ChargeLine.Amount))
            UpdateSummary();
    }

    private void UpdateSummary()
    {
        var selected = FeeLines.Where(l => l.IsSelected && l.Amount > 0m && l.Cycle == SelectedCycle).ToList();
        var perStudent = selected.Sum(l => l.Amount);
        var targets = IsStudentMode ? (SelectedStudent is null ? 0 : 1) : Students.Count;

        SummaryText = selected.Count == 0
            ? "Upar se fee tick karein aur amount likhein."
            : IsStudentMode
                ? $"{selected.Count} fee(s): Rs {perStudent:N0} balance mein add hoga."
                : $"{selected.Count} fee(s): Rs {perStudent:N0} per student × {targets} student(s) = Rs {perStudent * targets:N0}";
    }

    /// <summary>Adds the ticked fees of the chosen cycle to the class or the single student's balance.</summary>
    [RelayCommand]
    private async Task AddToBalanceAsync()
    {
        if (IsStudentMode ? SelectedStudent is null : SelectedClass is null)
        {
            SetMessage(IsStudentMode ? "Pehle student select karein." : "Pehle class select karein.", true);
            return;
        }
        if (PeriodDate is null) { SetMessage("Billing month select karein.", true); return; }
        if (DueDate is null) { SetMessage("Due date select karein.", true); return; }

        var selected = FeeLines.Where(l => l.IsSelected && l.Amount > 0m && l.Cycle == SelectedCycle).ToList();
        if (selected.Count == 0) { SetMessage("Kam az kam ek fee tick karein aur amount likhein.", true); return; }
        if (selected.Any(l => l.Amount > 10_000_000m)) { SetMessage("Amount 10,000,000 se zyada nahi ho sakta.", true); return; }

        var targetCount = IsStudentMode ? 1 : Students.Count;
        if (targetCount == 0) { SetMessage("Is class mein koi active student nahi hai.", true); return; }

        var perStudent = selected.Sum(l => l.Amount);
        var who = IsStudentMode ? SelectedStudent!.FullName : $"{SelectedClass!.DisplayName} ({targetCount} students)";
        var names = string.Join(", ", selected.Select(l => l.Name));
        var confirm = MessageBox.Show(
            $"{SelectedCycle} fee: {names}\n" +
            $"Rs {perStudent:N0} per student\n\n" +
            $"{who} ko total Rs {perStudent * targetCount:N0} balance mein add karein?\n\n" +
            "Jo fee pehle se is mahine/period ke liye bani hui hai, woh dobara nahi banegi.",
            "Confirm: Balance mein add",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        SetMessage(string.Empty, false);
        try
        {
            var request = new
            {
                Scope = IsStudentMode ? "Student" : "Class",
                ClassId = IsStudentMode ? (int?)null : SelectedClass!.Id,
                StudentId = IsStudentMode ? (int?)SelectedStudent!.Id : null,
                Cycle = ToServerCycle(SelectedCycle),
                PeriodMonth = PeriodDate.Value.ToString("yyyy-MM"),
                DueDate = DateOnly.FromDateTime(DueDate.Value),
                Items = selected.Select(l => new { FeeHeadId = l.FeeHeadId, Amount = l.Amount }).ToList()
            };

            var result = await _api.ChargeFeesAsync(request);
            if (!result.Ok || result.Data is null) { SetMessage(result.Error, true); return; }

            SetMessage(string.IsNullOrWhiteSpace(result.Msg) ? "Fee balance mein add ho gayi." : result.Msg, false);
            if (SelectedStudent is not null) await LoadHistoryAsync(SelectedStudent.Id);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SearchStudentsAsync()
    {
        var term = StudentSearch.Trim();
        if (!IsStudentMode || term.Length == 0)
        {
            Students.Clear();
            return;
        }

        var result = await _api.StudentsAsync(search: term, pageSize: 50);
        if (!result.Ok || result.Data is null) { SetMessage(result.Error, true); return; }

        Students.Clear();
        foreach (var s in result.Data.Items) Students.Add(s);
        if (Students.Count == 0) SetMessage("Is naam/roll par koi active student nahi mila.", false);
    }

    private async Task LoadHistoryAsync(int studentId)
    {
        var version = Interlocked.Increment(ref _historyVersion);
        var result = await _api.StudentFeeHistoryAsync(studentId);
        if (version != _historyVersion) return;
        if (!result.Ok || result.Data is null)
        {
            ClearHistory();
            SetMessage(result.Error, true);
            return;
        }
        ApplyHistory(result.Data);
    }

    private void ApplyHistory(StudentFeeHistoryDto h)
    {
        History = h;
        HasHistory = true;
        HistoryTitle = $"{h.StudentName}  ·  Class {h.ClassName}";
        HistoryInfo = $"Roll {h.RollNo}   ·   Father: {h.FatherName}   ·   Phone: {h.FatherPhone}";
        TotalAmount = h.TotalAmount;
        TotalPaid = h.TotalPaid;
        TotalBalance = h.TotalBalance;

        HistoryFees.Clear();
        foreach (var r in h.Fees) HistoryFees.Add(r);
        HistoryPayments.Clear();
        foreach (var p in h.Payments.OrderByDescending(x => x.PaidAtPkt)) HistoryPayments.Add(p);
    }

    private void ClearHistory()
    {
        History = null;
        HasHistory = false;
        HistoryTitle = string.Empty;
        HistoryInfo = string.Empty;
        TotalAmount = 0m;
        TotalPaid = 0m;
        TotalBalance = 0m;
        HistoryFees.Clear();
        HistoryPayments.Clear();
    }

    [RelayCommand]
    private void PrintHistory()
    {
        if (History is null) return;
        try
        {
            var path = FeeHistoryPdf.Create(History, FeeHistoryPdf.DefaultFolder);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            SetMessage("PDF khul gaya. Wahan se Print (Ctrl+P) karein.", false);
        }
        catch (Exception ex)
        {
            SetMessage("PDF nahi ban saka: " + ex.Message, true);
        }
    }

    [RelayCommand]
    private void SendWhatsApp()
    {
        if (History is null) return;
        try
        {
            var path = FeeHistoryPdf.Create(History, FeeHistoryPdf.DefaultFolder);
            var number = FeeHistoryPdf.WhatsAppNumber(History.FatherPhone);

            // WhatsApp links cannot attach files, so: open the chat with the summary text,
            // and open the folder with the PDF selected so it can be dragged into the chat.
            if (number is not null)
            {
                var text = Uri.EscapeDataString(FeeHistoryPdf.BuildWhatsAppMessage(History));
                Process.Start(new ProcessStartInfo($"https://wa.me/{number}?text={text}") { UseShellExecute = true });
            }
            Process.Start("explorer.exe", $"/select,\"{path}\"");

            SetMessage(number is null
                ? "Parent ka phone number sahi nahi hai. PDF folder khul gaya; WhatsApp par khud bhejein."
                : "WhatsApp khul gaya. Folder mein se PDF ko chat mein drag karke bhejein.",
                number is null);
        }
        catch (Exception ex)
        {
            SetMessage("WhatsApp kholne mein masla: " + ex.Message, true);
        }
    }

    [RelayCommand] private void OpenAutoBilling() => AutoBillingRequested?.Invoke();
    [RelayCommand] private void Back() => BackRequested?.Invoke();

    private static string ToUiCycle(string? serverCycle) => serverCycle switch
    {
        "Quarterly" or "PerTerm" => "Quarterly",
        "Annually" => "Yearly",
        "OneTime" => "One-time",
        _ => "Monthly"
    };

    private static string ToServerCycle(string uiCycle) => uiCycle switch
    {
        "Quarterly" => "Quarterly",
        "Yearly" => "Annually",
        "One-time" => "OneTime",
        _ => "Monthly"
    };

    private void SetMessage(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }
}

/// <summary>One fee row on the screen: a fee head with its amount and whether it is ticked.</summary>
public partial class ChargeLine : ObservableObject
{
    public ChargeLine(int feeHeadId, string name, string category, string cycle, decimal amount, bool isSelected)
    {
        FeeHeadId = feeHeadId;
        Name = name;
        Category = category;
        Cycle = cycle;
        _amount = amount;
        _isSelected = isSelected;
    }

    public int FeeHeadId { get; }
    public string Name { get; }
    public string Category { get; }
    /// <summary>UI cycle label: Monthly, Quarterly, Yearly or One-time.</summary>
    public string Cycle { get; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private decimal _amount;
}
