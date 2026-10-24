using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Fees;

/// <summary>Edits the draft fee heads/amounts for a class. Saving never creates fee bills.</summary>
public partial class FeePlanViewModel : ObservableObject
{
    private readonly ApiClient _api = ApiClient.Instance;
    private int _loadVersion;
    private bool _suppressClassLoad;

    [ObservableProperty] private ClassListItem? _selectedClass;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isError;

    public ObservableCollection<ClassListItem> Classes { get; } = new();
    public ObservableCollection<ClassFeePlanItemDto> Items { get; } = new();
    public string[] BillingCycles { get; } = { "Monthly", "PerTerm", "Annually", "OneTime" };
    public event Action? BackRequested;

    public FeePlanViewModel() => _ = LoadAsync();

    partial void OnSelectedClassChanged(ClassListItem? value)
    {
        if (value is not null && !_suppressClassLoad) _ = LoadClassPlanAsync(value.Id);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        IsError = false;
        Message = string.Empty;
        try
        {
            var result = await _api.ClassesAsync();
            if (!result.Ok || result.Data is null)
            {
                SetMessage(result.Error, true);
                return;
            }

            Classes.Clear();
            foreach (var item in result.Data) Classes.Add(item);
            if (SelectedClass is null && Classes.Count > 0)
            {
                _suppressClassLoad = true;
                SelectedClass = Classes[0];
                _suppressClassLoad = false;
                await LoadClassPlanAsync(Classes[0].Id);
            }
            else if (SelectedClass is not null)
            {
                await LoadClassPlanAsync(SelectedClass.Id);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadClassPlanAsync(int classId)
    {
        var version = Interlocked.Increment(ref _loadVersion);
        IsBusy = true;
        IsError = false;
        Message = string.Empty;
        try
        {
            var result = await _api.ClassFeePlanAsync(classId);
            if (version != _loadVersion) return;
            if (!result.Ok || result.Data is null)
            {
                SetMessage(result.Error, true);
                return;
            }

            Items.Clear();
            foreach (var item in result.Data) Items.Add(item);
            SetMessage("Draft plan loaded. No fee bills have been created.", false);
        }
        finally
        {
            if (version == _loadVersion) IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedClass is null)
        {
            SetMessage("Pehle class select karein.", true);
            return;
        }
        if (Items.Any(x => x.Amount < 0 || x.Amount > 10_000_000m || !BillingCycles.Contains(x.BillingCycle)))
        {
            SetMessage("Amount ya billing cycle check karein. Amount 0 se 10,000,000 tak ho sakti hai.", true);
            return;
        }

        IsBusy = true;
        IsError = false;
        try
        {
            var request = new SaveClassFeePlanRequest
            {
                Items = Items.Select(x => new SaveClassFeePlanItemRequest
                {
                    FeeHeadId = x.FeeHeadId,
                    BillingCycle = x.BillingCycle,
                    Amount = x.Amount,
                    IsIncluded = x.IsIncluded,
                    IsOptionalForStudent = x.IsOptionalForStudent
                }).ToList()
            };
            var result = await _api.SaveClassFeePlanAsync(SelectedClass.Id, request);
            SetMessage(result.Ok
                ? $"{SelectedClass.DisplayName} draft plan saved. No invoices or charges were generated."
                : result.Error, !result.Ok);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand] private void Back() => BackRequested?.Invoke();

    private void SetMessage(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }
}
