using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Fees;

/// <summary>
/// Collect page for one student. Every unpaid fee head is a row: tick the ones being paid, edit the
/// amount for partial payment, or "Sab select karein" to tick everything at full balance.
/// Whatever is not paid stays on the bill as balance.
/// </summary>
public partial class StudentCollectViewModel : ObservableObject
{
    private readonly ApiClient _api = ApiClient.Instance;
    private readonly int _studentId;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private string _studentName = string.Empty;
    [ObservableProperty] private string _studentInfo = string.Empty;
    [ObservableProperty] private decimal _totalAmount;
    [ObservableProperty] private decimal _totalPaid;
    [ObservableProperty] private decimal _totalBalance;
    [ObservableProperty] private decimal _selectedTotal;
    [ObservableProperty] private decimal _balanceAfter;
    [ObservableProperty] private DateTime? _paidDate = DateTime.Today;
    [ObservableProperty] private string _method = "Cash";
    [ObservableProperty] private string _notes = string.Empty;

    public ObservableCollection<PayBillRow> Bills { get; } = new();
    public string[] PaymentMethods { get; } = { "Cash", "Bank Transfer", "Cheque", "Online" };

    public event Action? BackRequested;
    public event Action? ViewStatementRequested;

    public StudentCollectViewModel(int studentId)
    {
        _studentId = studentId;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var r = await _api.StudentFeeHistoryAsync(_studentId);
        if (!r.Ok || r.Data is null)
        {
            SetMessage("Student ki fee maloomat nahi aayi: " + r.Error, true);
            return;
        }

        var h = r.Data;
        StudentName = h.StudentName;
        StudentInfo = $"Class {h.ClassName}  ·  Roll {h.RollNo}  ·  Father: {h.FatherName}  ·  {h.FatherPhone}";
        TotalAmount = h.TotalAmount;
        TotalPaid = h.TotalPaid;
        TotalBalance = h.TotalBalance;

        Bills.Clear();
        foreach (var row in h.Fees.Where(f => f.Balance > 0m))
        {
            var bill = new PayBillRow(row);
            bill.PropertyChanged += OnBillChanged;
            Bills.Add(bill);
        }
        Recalculate();

        SetMessage(Bills.Count == 0
            ? "Is student ka koi baqaya balance nahi."
            : "Jo fee deni hai woh tick karein. Amount badal kar partial payment bhi ho sakti hai.", false);
    }

    private void OnBillChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PayBillRow.IsSelected) or nameof(PayBillRow.Amount))
            Recalculate();
    }

    private void Recalculate()
    {
        SelectedTotal = Bills.Where(b => b.IsSelected).Sum(b => b.Amount);
        BalanceAfter = Math.Max(0m, TotalBalance - SelectedTotal);
    }

    /// <summary>Full paid: ticks every fee at its complete balance.</summary>
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var b in Bills)
        {
            b.Amount = b.Balance;
            b.IsSelected = true;
        }
        Recalculate();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var b in Bills)
        {
            b.IsSelected = false;
            b.Amount = 0m;
        }
        Recalculate();
    }

    [RelayCommand]
    private async Task PayAsync()
    {
        var lines = Bills.Where(b => b.IsSelected).ToList();
        if (lines.Count == 0) { SetMessage("Pehle wo fee select karein jo student ne di hai.", true); return; }
        if (lines.Any(b => b.Amount <= 0m)) { SetMessage("Selected fee ka amount zero se zyada hona chahiye.", true); return; }
        if (lines.Any(b => b.Amount > b.Balance))
        { SetMessage("Kisi fee ka amount uske baaqi balance se zyada hai.", true); return; }
        if (PaidDate is null) { SetMessage("Payment ki tareekh select karein.", true); return; }

        var total = lines.Sum(b => b.Amount);
        var confirm = MessageBox.Show(
            $"{StudentName} se Rs {total:N0} ({Method}) lene hain?\n" +
            $"{lines.Count} fee head(s) par payment hogi.\n\n" +
            $"Payment ke baad baaqi balance: Rs {Math.Max(0m, TotalBalance - total):N0}",
            "Confirm: Payment", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var r = await _api.PaySelectedFeesAsync(_studentId, new
            {
                PaidDate = DateOnly.FromDateTime(PaidDate.Value),
                Method,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                Lines = lines.Select(b => new { FeeId = b.FeeId, Amount = b.Amount }).ToList()
            });
            if (!r.Ok || r.Data is null) { SetMessage(r.Error, true); return; }

            SetMessage(string.IsNullOrWhiteSpace(r.Msg) ? "Payment record ho gayi." : r.Msg, false);
            Notes = string.Empty;
            await LoadAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] private void ViewStatement() => ViewStatementRequested?.Invoke();

    [RelayCommand] private void Back() => BackRequested?.Invoke();

    private void SetMessage(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }
}

/// <summary>One unpaid fee head on the collect page.</summary>
public partial class PayBillRow : ObservableObject
{
    public PayBillRow(FeeHistoryRowDto r)
    {
        FeeId = r.FeeId;
        Month = r.Month;
        FeeHead = r.FeeHead;
        DueDate = r.DueDate;
        Balance = r.Balance;
        Status = r.Status;
    }

    public int FeeId { get; }
    public string Month { get; }
    public string FeeHead { get; }
    public DateOnly DueDate { get; }
    public decimal Balance { get; }
    public string Status { get; }

    [ObservableProperty] private bool _isSelected;
    /// <summary>Amount to pay now for this fee head (≤ balance).</summary>
    [ObservableProperty] private decimal _amount;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && Amount <= 0m) Amount = Balance;
    }

    partial void OnAmountChanged(decimal value)
    {
        if (value > 0m && !IsSelected) IsSelected = true;
    }
}
