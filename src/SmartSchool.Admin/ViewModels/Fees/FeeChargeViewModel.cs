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
/// Fee screen (student-wise):
///  - Live search by name / roll no (every keystroke, debounced) with an optional class filter.
///  - Class-wise mode: pick a class → its fee plan and students; tick fees → add to balance.
///  - Click a student → statement: bills (edit/delete if unpaid), every payment with date & time,
///    collect new money (oldest bill first), reverse a wrong payment, print / PDF / WhatsApp.
/// </summary>
public partial class FeeChargeViewModel : ObservableObject
{
    private readonly ApiClient _api = ApiClient.Instance;
    private readonly DispatcherTimer _searchTimer;
    private int _itemsVersion;
    private int _historyVersion;
    private int _listVersion;

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

    // Statement (right side)
    [ObservableProperty] private StudentFeeHistoryDto? _history;
    [ObservableProperty] private bool _hasHistory;
    [ObservableProperty] private string _historyTitle = string.Empty;
    [ObservableProperty] private string _historyInfo = string.Empty;
    [ObservableProperty] private string _historyError = string.Empty;
    [ObservableProperty] private decimal _totalAmount;
    [ObservableProperty] private decimal _totalPaid;
    [ObservableProperty] private decimal _totalBalance;

    // Collect new money
    [ObservableProperty] private decimal _collectAmount;
    [ObservableProperty] private string _collectMethod = "Cash";
    [ObservableProperty] private DateTime? _collectDate = DateTime.Today;
    [ObservableProperty] private string _collectNotes = string.Empty;

    // Selected bill (edit / delete) and selected payment (reverse)
    [ObservableProperty] private FeeHistoryRowDto? _selectedBill;
    [ObservableProperty] private decimal _editBillAmount;
    [ObservableProperty] private DateTime? _editBillDue;
    [ObservableProperty] private FeeHistoryPaymentDto? _selectedPayment;
    [ObservableProperty] private string _reverseReason = string.Empty;

    public ObservableCollection<ClassListItem> Classes { get; } = new();
    public ObservableCollection<StudentListItem> Students { get; } = new();
    public ObservableCollection<ChargeLine> FeeLines { get; } = new();
    public ICollectionView FeeLinesView { get; }
    public ObservableCollection<FeeHistoryRowDto> HistoryFees { get; } = new();
    public ObservableCollection<FeeHistoryPaymentDto> HistoryPayments { get; } = new();

    // Period filter for the statement: Sab (all), Ek din (one day), Ek mahina (one month).
    public string[] PeriodModes { get; } = { "Sab", "Ek din", "Ek mahina" };
    [ObservableProperty] private string _periodMode = "Sab";
    [ObservableProperty] private DateTime? _filterDate = DateTime.Today;
    [ObservableProperty] private string _filterSummary = string.Empty;
    private readonly List<FeeHistoryRowDto> _allFees = new();
    private readonly List<FeeHistoryPaymentDto> _allPayments = new();

    partial void OnPeriodModeChanged(string value) => ApplyPeriodFilter();
    partial void OnFilterDateChanged(DateTime? value) => ApplyPeriodFilter();

    /// <summary>Fills the statement lists from the full history using the chosen day or month.</summary>
    private void ApplyPeriodFilter()
    {
        var date = FilterDate?.Date;
        IEnumerable<FeeHistoryRowDto> fees = _allFees;
        IEnumerable<FeeHistoryPaymentDto> payments = _allPayments;

        if (PeriodMode == "Ek din" && date is not null)
        {
            var day = DateOnly.FromDateTime(date.Value);
            fees = fees.Where(f => f.DueDate == day);
            payments = payments.Where(p => p.PaidAtPkt.Date == date.Value);
            FilterSummary = $"Din: {date.Value:dd MMM yyyy}";
        }
        else if (PeriodMode == "Ek mahina" && date is not null)
        {
            var key = date.Value.ToString("yyyy-MM");
            fees = fees.Where(f => f.Month == key);
            payments = payments.Where(p => p.PaidAtPkt.Year == date.Value.Year && p.PaidAtPkt.Month == date.Value.Month);
            FilterSummary = $"Mahina: {date.Value:MMMM yyyy}";
        }
        else
        {
            FilterSummary = "Sab payments aur bills";
        }

        var paymentList = payments.ToList();
        HistoryFees.Clear();
        foreach (var f in fees) HistoryFees.Add(f);
        HistoryPayments.Clear();
        foreach (var p in paymentList) HistoryPayments.Add(p);

        FilterSummary += $"  ·  {paymentList.Count} payment(s), Rs {paymentList.Sum(p => p.Amount):N0} ·  {HistoryFees.Count} bill(s)";
    }
    public string[] PaymentMethods { get; } = { "Cash", "Bank Transfer", "Cheque", "Online" };

    public event Action? BackRequested;
    public event Action? AutoBillingRequested;

    public FeeChargeViewModel()
    {
        FeeLinesView = CollectionViewSource.GetDefaultView(FeeLines);
        FeeLinesView.Filter = o => o is ChargeLine line && line.Cycle == SelectedCycle;

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            _ = RefreshStudentListAsync();
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

        if (value) _ = RefreshStudentListAsync();
        else if (SelectedClass is not null) _ = LoadClassAsync(SelectedClass.Id);
        UpdateSummary();
    }

    /// <summary>Student mode: class is a filter on the list. Class mode: class is the fee target.</summary>
    partial void OnSelectedClassChanged(ClassListItem? value)
    {
        if (IsStudentMode)
        {
            _ = RefreshStudentListAsync();
            return;
        }
        if (value is null)
        {
            Students.Clear();
            SelectedStudent = null;
            FeeLines.Clear();
            ClearHistory();
            UpdateSummary();
            return;
        }
        _ = LoadClassAsync(value.Id);
    }

    /// <summary>Every keystroke restarts the short debounce timer → live search.</summary>
    partial void OnStudentSearchChanged(string value)
    {
        if (!IsStudentMode) return;
        _searchTimer.Stop();
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

    partial void OnSelectedBillChanged(FeeHistoryRowDto? value)
    {
        EditBillAmount = value?.Amount ?? 0m;
        EditBillDue = value is null ? null : value.DueDate.ToDateTime(TimeOnly.MinValue);
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
            // No class pre-selected: student search works across all classes from the start.
            SetMessage("Naam ya roll number likhein (har harf par search hoga), ya class chunein.", false);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (IsStudentMode)
        {
            await RefreshStudentListAsync();
            if (SelectedStudent is not null) await LoadHistoryAsync(SelectedStudent.Id);
            return;
        }
        if (SelectedClass is not null) await LoadClassAsync(SelectedClass.Id);
    }

    [RelayCommand]
    private void ClearClassFilter() => SelectedClass = null;

    /// <summary>Student list = search term (name / roll / father / phone) narrowed by the class filter.</summary>
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
            var result = await _api.StudentsAsync(
                search: term.Length == 0 ? null : term, classId: classId, pageSize: 100);
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

            SelectedStudent = null;
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
        try
        {
            var plan = await _api.StudentFeePlanAsync(studentId);
            if (version != _itemsVersion) return;
            if (!plan.Ok || plan.Data is null) { SetMessage(plan.Error, true); return; }

            ReplaceFeeLines(plan.Data.Select(x => new ChargeLine(
                x.FeeHeadId, x.Name, x.Category, ToUiCycle(x.BillingCycle), x.Amount,
                isSelected: x.IsIncluded && x.Amount > 0m && (!x.IsOptionalForStudent || x.IsOptedIn))));
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

    /// <summary>Opens the statement of one student (used when coming from the dashboard).</summary>
    public Task ShowStudentAsync(int studentId) => LoadHistoryAsync(studentId);

    private async Task LoadHistoryAsync(int studentId)
    {
        var version = Interlocked.Increment(ref _historyVersion);
        var result = await _api.StudentFeeHistoryAsync(studentId);
        if (version != _historyVersion) return;
        if (!result.Ok || result.Data is null)
        {
            ClearHistory();
            HistoryError = $"Is student ki history nahi aayi: {result.Error}. " +
                           "Agar 'Not Found' ya 404 aa raha hai to server ko v1.1.54 par update karein.";
            SetMessage(result.Error, true);
            return;
        }
        ApplyHistory(result.Data);
    }

    private void ApplyHistory(StudentFeeHistoryDto h)
    {
        History = h;
        HasHistory = true;
        HistoryError = string.Empty;
        HistoryTitle = $"{h.StudentName}  ·  Class {h.ClassName}";
        HistoryInfo = $"Roll {h.RollNo}   ·   Father: {h.FatherName}   ·   Phone: {h.FatherPhone}";
        TotalAmount = h.TotalAmount;
        TotalPaid = h.TotalPaid;
        TotalBalance = h.TotalBalance;
        CollectAmount = h.TotalBalance;
        CollectNotes = string.Empty;
        ReverseReason = string.Empty;

        SelectedBill = null;
        SelectedPayment = null;

        _allFees.Clear();
        _allFees.AddRange(h.Fees);
        _allPayments.Clear();
        _allPayments.AddRange(h.Payments.OrderByDescending(x => x.PaidAtPkt));
        ApplyPeriodFilter();
    }

    private void ClearHistory()
    {
        History = null;
        HasHistory = false;
        HistoryError = string.Empty;
        HistoryTitle = string.Empty;
        HistoryInfo = string.Empty;
        TotalAmount = 0m;
        TotalPaid = 0m;
        TotalBalance = 0m;
        CollectAmount = 0m;
        SelectedBill = null;
        SelectedPayment = null;
        _allFees.Clear();
        _allPayments.Clear();
        HistoryFees.Clear();
        HistoryPayments.Clear();
    }

    [RelayCommand]
    private async Task CollectAsync()
    {
        if (History is null) return;
        if (CollectAmount <= 0m) { SetMessage("Jo amount lena hai woh likhein.", true); return; }
        if (CollectAmount > TotalBalance)
        {
            SetMessage($"Amount baaqi balance Rs {TotalBalance:N0} se zyada nahi ho sakta.", true);
            return;
        }
        if (CollectDate is null) { SetMessage("Payment ki tareekh select karein.", true); return; }

        var confirm = MessageBox.Show(
            $"{History.StudentName} se Rs {CollectAmount:N0} ({CollectMethod}) lene hain?\n\n" +
            "Yeh amount khud purane bills mein, oldest due date se, allocate hoga.",
            "Confirm: Payment", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var r = await _api.CollectStudentFeeAsync(History.StudentId, new
            {
                Amount = CollectAmount,
                PaidDate = DateOnly.FromDateTime(CollectDate.Value),
                Method = CollectMethod,
                Notes = string.IsNullOrWhiteSpace(CollectNotes) ? null : CollectNotes.Trim()
            });
            if (!r.Ok || r.Data is null) { SetMessage(r.Error, true); return; }

            SetMessage(string.IsNullOrWhiteSpace(r.Msg) ? "Payment record ho gayi." : r.Msg, false);
            await LoadHistoryAsync(History.StudentId);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ReversePaymentAsync()
    {
        if (History is null) return;
        if (SelectedPayment is null)
        {
            SetMessage("Pehle payment list mein woh payment select karein jo ghalat thi.", true);
            return;
        }
        if (SelectedPayment.Amount <= 0m)
        {
            SetMessage("Reversal entry ko dobara reverse nahi kiya ja sakta.", true);
            return;
        }
        if (ReverseReason.Trim().Length < 3)
        {
            SetMessage("Reversal ki wajah likhein (kam az kam 3 huruf).", true);
            return;
        }

        var confirm = MessageBox.Show(
            $"Rs {SelectedPayment.Amount:N0} ki payment ({SelectedPayment.ReceiptNo}) reverse karni hai?\n\n" +
            "Record delete nahi hoga; ek reversal entry banegi aur balance dobara baaqi ho jayega.",
            "Confirm: Reverse payment", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var r = await _api.ReverseFeePaymentAsync(SelectedPayment.PaymentId, ReverseReason.Trim());
            if (!r.Ok || r.Data is null) { SetMessage(r.Error, true); return; }

            SetMessage(string.IsNullOrWhiteSpace(r.Msg) ? "Payment reverse ho gayi." : r.Msg, false);
            await LoadHistoryAsync(History.StudentId);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveBillAsync()
    {
        if (History is null || SelectedBill is null) { SetMessage("Pehle neeche se bill select karein.", true); return; }
        if (EditBillAmount <= 0m) { SetMessage("Bill ka amount zero se zyada hona chahiye.", true); return; }
        if (EditBillDue is null) { SetMessage("Due date select karein.", true); return; }

        IsBusy = true;
        try
        {
            var r = await _api.UpdateFeeBillAsync(SelectedBill.FeeId, new
            {
                StudentId = History.StudentId,
                Amount = EditBillAmount,
                Month = SelectedBill.Month,
                DueDate = DateOnly.FromDateTime(EditBillDue.Value),
                Notes = (string?)null
            });
            if (!r.Ok) { SetMessage(r.Error, true); return; }

            SetMessage("Bill update ho gaya.", false);
            await LoadHistoryAsync(History.StudentId);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DeleteBillAsync()
    {
        if (History is null || SelectedBill is null) { SetMessage("Pehle neeche se bill select karein.", true); return; }

        var confirm = MessageBox.Show(
            $"{SelectedBill.FeeHead} ({SelectedBill.Month}, Rs {SelectedBill.Amount:N0}) ka bill delete karna hai?\n\n" +
            "Sirf bina payment ke bill delete hota hai.",
            "Confirm: Delete bill", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var r = await _api.DeleteFeeBillAsync(SelectedBill.FeeId);
            if (!r.Ok) { SetMessage(r.Error, true); return; }

            SetMessage("Bill delete ho gaya.", false);
            await LoadHistoryAsync(History.StudentId);
        }
        finally { IsBusy = false; }
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
