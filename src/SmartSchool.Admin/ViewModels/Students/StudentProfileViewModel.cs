using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Students;

public partial class StudentProfileViewModel : ObservableObject
{
    private readonly ApiClient _api = ApiClient.Instance;
    private readonly int _studentId;

    [ObservableProperty] private StudentDetailResponse? _student;
    [ObservableProperty] private bool _isBusy = true;
    [ObservableProperty] private string _error = string.Empty;

    public ObservableCollection<FeeListItem> Fees { get; } = new();

    public event Action? BackRequested;

    public StudentProfileViewModel(int studentId)
    {
        _studentId = studentId;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var r = await _api.GetAsync<StudentDetailResponse>($"api/students/{_studentId}");
            if (r.Ok && r.Data is not null)
            {
                Student = r.Data;
                var fees = await _api.GetAsync<List<FeeListItem>>($"api/fees/student/{_studentId}");
                if (fees.Ok && fees.Data is not null)
                    foreach (var f in fees.Data) Fees.Add(f);
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

    [RelayCommand] private void Back() => BackRequested?.Invoke();
}
