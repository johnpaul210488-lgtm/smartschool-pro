using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Fees;

/// <summary>Collect payment for one fee slip → then show/print the receipt.</summary>
public partial class FeeCollectionViewModel : ObservableObject
{
    private readonly ApiClient _api = ApiClient.Instance;
    private readonly FeeListItem _fee;

    [ObservableProperty] private string _studentName;
    [ObservableProperty] private string _className;
    [ObservableProperty] private string _month;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private DateTime _paidDate = DateTime.Today;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _error = string.Empty;

    public event Action<FeeListItem>? PaidAndShowReceipt;
    public event Action? Cancelled;

    public FeeCollectionViewModel(FeeListItem fee)
    {
        _fee = fee;
        _studentName = fee.StudentName;
        _className = fee.ClassName;
        _month = fee.Month;
        _amount = fee.Amount;
    }

    [RelayCommand]
    private async Task ConfirmPaymentAsync()
    {
        IsBusy = true;
        Error = string.Empty;
        try
        {
            var r = await _api.PostAsync<FeeListItem>($"api/fees/{_fee.Id}/pay",
                new { paidDate = PaidDate.ToString("yyyy-MM-dd") });
            if (r.Ok && r.Data is not null)
            {
                ToastService.Current.Info("Fee Collected",
                    $"{StudentName} — Rs {Amount:N0} received.");
                PaidAndShowReceipt?.Invoke(r.Data);
            }
            else
            {
                Error = r.Error;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand] private void Cancel() => Cancelled?.Invoke();
}
