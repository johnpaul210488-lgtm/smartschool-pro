using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Fees;

/// <summary>Collect a full/partial payment while retaining every transaction in the ledger.</summary>
public partial class FeeCollectionViewModel : ObservableObject
{
    private readonly ApiClient _api = ApiClient.Instance;
    private readonly FeeListItem _fee;

    [ObservableProperty] private string _studentName;
    [ObservableProperty] private string _className;
    [ObservableProperty] private string _month;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private decimal _balance;
    [ObservableProperty] private decimal _paymentAmount;
    [ObservableProperty] private string _paymentMethod = "Cash";
    [ObservableProperty] private string _paymentNotes = string.Empty;
    [ObservableProperty] private DateTime _paidDate = DateTime.Today;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _error = string.Empty;

    public ObservableCollection<FeePaymentItem> Payments { get; } = new();
    public string[] PaymentMethods { get; } = { "Cash", "Bank Transfer", "Cheque", "Online" };
    public event Action<FeeListItem>? PaidAndShowReceipt;
    public event Action<FeeListItem>? PartialPaymentRecorded;
    public event Action? Cancelled;

    public FeeCollectionViewModel(FeeListItem fee)
    {
        _fee = fee;
        _studentName = fee.StudentName;
        _className = fee.ClassName;
        _month = fee.Month;
        _amount = fee.Amount;
        _balance = fee.Balance > 0m ? fee.Balance : Math.Max(0m, fee.Amount - fee.AmountPaid);
        _paymentAmount = _balance;
        _ = LoadPaymentsAsync();
    }

    private async Task LoadPaymentsAsync()
    {
        var result = await _api.FeePaymentHistoryAsync(_fee.Id);
        if (!result.Ok || result.Data is null) return;
        Payments.Clear();
        foreach (var item in result.Data) Payments.Add(item);
    }

    [RelayCommand]
    private async Task ConfirmPaymentAsync()
    {
        if (PaymentAmount <= 0m || PaymentAmount > Balance)
        { Error = $"Enter an amount greater than 0 and no more than the remaining Rs {Balance:N2}."; return; }
        IsBusy = true;
        Error = string.Empty;
        try
        {
            var r = await _api.PostAsync<FeeListItem>($"api/fees/{_fee.Id}/pay",
                new { amount = PaymentAmount, paidDate = PaidDate.ToString("yyyy-MM-dd"), method = PaymentMethod, notes = PaymentNotes });
            if (r.Ok && r.Data is not null)
            {
                ToastService.Current.Info("Payment recorded",
                    $"{StudentName} — Rs {PaymentAmount:N2} received; Rs {r.Data.Balance:N2} remains.");
                if (r.Data.Balance <= 0m) PaidAndShowReceipt?.Invoke(r.Data);
                else PartialPaymentRecorded?.Invoke(r.Data);
            }
            else Error = r.Error;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] private void Cancel() => Cancelled?.Invoke();
}
