using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Students;

public partial class StudentListViewModel : ObservableObject, IDisposable
{
    private readonly ApiClient _api = ApiClient.Instance;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ClassListItem? _selectedClass;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private StudentListItem? _selectedStudent;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private string _importStatus = string.Empty;
    [ObservableProperty] private string _importDetails = string.Empty;

    public ObservableCollection<StudentListItem> Students { get; } = new();
    public ObservableCollection<ClassListItem> Classes { get; } = new();
    public ICollectionView StudentsView { get; }

    /// <summary>MainWindow listens for this to navigate to the form/profile page.</summary>
    public event Action<int?>? EditRequested;       // null = new student
    public event Action<int>? ProfileRequested;

    public StudentListViewModel()
    {
        StudentsView = CollectionViewSource.GetDefaultView(Students);
        StudentsView.SortDescriptions.Add(new SortDescription(nameof(StudentListItem.ClassName), ListSortDirection.Ascending));
        StudentsView.SortDescriptions.Add(new SortDescription(nameof(StudentListItem.RollNo), ListSortDirection.Ascending));

        _ = InitAsync();

        // SignalR: flip the live chip next to the student's name.
        WeakReferenceMessenger.Default.Register<PickInAlertMessage>(this, (_, m) => MarkLive(m.Payload.StudentId, "Picked"));
        WeakReferenceMessenger.Default.Register<DropOutAlertMessage>(this, (_, m) => MarkLive(m.Payload.StudentId, "Dropped"));
    }

    private void MarkLive(int studentId, string status)
    {
        var s = Students.FirstOrDefault(x => x.Id == studentId);
        if (s is null) return;
        var idx = Students.IndexOf(s);
        s.LiveStatus = status;
        Students[idx] = s; // replace to refresh the row
    }

    private async Task InitAsync()
    {
        var cls = await _api.ClassesAsync();
        if (cls.Ok && cls.Data is not null)
        {
            Classes.Add(new ClassListItem { Id = 0, DisplayName = "— All classes —" });
            foreach (var c in cls.Data) Classes.Add(c);
            SelectedClass = Classes[0];
        }
        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var classId = SelectedClass?.Id > 0 ? SelectedClass.Id : (int?)null;
            var r = await _api.StudentsAsync(SearchText, classId, 1, 500);
            if (!r.Ok || r.Data is null)
            {
                ToastService.Current.Error("Students", r.Error);
                return;
            }
            Students.Clear();
            foreach (var s in r.Data.Items) Students.Add(s);
            TotalCount = r.Data.TotalCount;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchTextChanged(string value) => _ = LoadAsync();
    partial void OnSelectedClassChanged(ClassListItem? value) => _ = LoadAsync();

    [RelayCommand] private void AddNew() => EditRequested?.Invoke(null);

    [RelayCommand]
    private async Task ImportExcelAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Excel / CSV|*.xlsx;*.csv|Excel file|*.xlsx|CSV file|*.csv",
            Title = "Students ki Excel file select karein (row 1 = headers)"
        };
        if (dlg.ShowDialog() != true) return;

        IsImporting = true; ImportDetails = string.Empty;
        ImportStatus = "📥 File parh raha hun…";
        try
        {
            var progress = new Progress<string>(s => ImportStatus = s);
            var r = await StudentExcelImporter.ImportAsync(dlg.FileName, progress);

            ImportStatus = r.Total == 0 ? "❌ File mein koi data row nahi thi"
                : r.Imported == r.Total ? $"✅ {r.Imported}/{r.Total} students import ho gaye!"
                : r.Imported > 0 ? $"⚠ {r.Imported} import • {r.Failed} fail (total {r.Total})"
                : $"❌ Koi row import nahi hui ({r.Total} mein se)";
            if (r.Errors.Count > 0)
                ImportDetails = string.Join("\n", r.Errors.Take(12))
                    + (r.Errors.Count > 12 ? $"\n… aur {r.Errors.Count - 12} aur notes" : "");

            if (r.Imported > 0) await LoadAsync();
        }
        catch (Exception ex)
        {
            ImportStatus = $"❌ Import fail: {ex.Message}";
            if (dlg.FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
                ImportDetails = "Purana .xls format nahi chalta — Excel mein file khol ke '.xlsx' (ya .csv) mein Save As karein.";
        }
        finally { IsImporting = false; }
    }
    [RelayCommand] private void Edit(StudentListItem? s) { if (s is not null) EditRequested?.Invoke(s.Id); }
    [RelayCommand] private void ViewProfile(StudentListItem? s) { if (s is not null) ProfileRequested?.Invoke(s.Id); }

    [RelayCommand]
    private async Task DeactivateAsync(StudentListItem? s)
    {
        if (s is null) return;
        var r = await _api.DeleteAsync($"api/students/{s.Id}");
        if (r.Ok) { ToastService.Current.Info("Student", $"{s.FullName} deactivate kar diya."); await LoadAsync(); }
        else ToastService.Current.Error("Student", r.Error);
    }

    public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
}
