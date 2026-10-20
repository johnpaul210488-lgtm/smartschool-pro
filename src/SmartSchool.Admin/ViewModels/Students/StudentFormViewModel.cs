using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.Students;

/// <summary>
/// Add/Edit student. Uses ObservableValidator so broken fields light up red in the form.
/// </summary>
public partial class StudentFormViewModel : ObservableValidator
{
    private readonly ApiClient _api = ApiClient.Instance;
    private readonly int? _studentId;

    [ObservableProperty] private string _title;

    [Required(ErrorMessage = "Naam lazmi hai")]
    [MinLength(3, ErrorMessage = "Kam az kam 3 haroof")]
    [ObservableProperty] private string _fullName = string.Empty;

    [Required(ErrorMessage = "Roll number lazmi hai")]
    [ObservableProperty] private string _rollNo = string.Empty;

    [Required(ErrorMessage = "Class select karein")]
    [ObservableProperty] private ClassListItem? _selectedClass;

    [ObservableProperty] private DateTime _dateOfBirth = new(2018, 1, 1);

    [Required(ErrorMessage = "Walid ka naam lazmi hai")]
    [ObservableProperty] private string _fatherName = string.Empty;

    [Required(ErrorMessage = "Phone lazmi hai")]
    [RegularExpression(@"^0\d{10}$", ErrorMessage = "03xxxxxxxxx (11 digits)")]
    [ObservableProperty] private string _fatherPhone = string.Empty;

    [RegularExpression(@"^(0\d{10})?$", ErrorMessage = "03xxxxxxxxx ya khali")]
    [ObservableProperty] private string _motherPhone = string.Empty;

    [Required(ErrorMessage = "Pata lazmi hai")]
    [ObservableProperty] private string _address = string.Empty;

    [ObservableProperty] private BusListItem? _selectedBus;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _formError = string.Empty;

    // ---- Profile photo ----
    [ObservableProperty] private string? _photoPath;           // freshly picked local file
    [ObservableProperty] private ImageSource? _photoPreview;   // new ya existing server photo
    [ObservableProperty] private bool _hasPhotoPreview;

    [RelayCommand]
    private void PickPhoto()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Photos|*.jpg;*.jpeg;*.png",
            Title = "Student photo select karein"
        };
        if (dlg.ShowDialog() != true) return;
        PhotoPath = dlg.FileName;
        SetPreview(new BitmapImage(new Uri(PhotoPath)));
    }

    [RelayCommand]
    private void RemovePhoto()
    {
        PhotoPath = null;
        PhotoPreview = null;
        HasPhotoPreview = false;
    }

    private void SetPreview(ImageSource src)
    {
        PhotoPreview = src;
        HasPhotoPreview = true;
    }

    /// <summary>Resize+encode to 640px JPEG (server/light DB bhi rahe).</summary>
    private static byte[] ToJpegBytes(string path)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.UriSource = new Uri(path);
        img.DecodePixelWidth = 640;
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        var enc = new JpegBitmapEncoder { QualityLevel = 88 };
        enc.Frames.Add(BitmapFrame.Create(img));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    public ObservableCollection<ClassListItem> Classes { get; } = new();
    public ObservableCollection<BusListItem> Buses { get; } = new();

    public event Action? SavedAndClose;
    public event Action? Cancelled;

    public StudentFormViewModel(int? studentId)
    {
        _studentId = studentId;
        _title = studentId is null ? "➕ New Student" : "✏️ Edit Student";
        ValidateAllProperties();
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var cls = await _api.ClassesAsync();
        if (cls.Ok && cls.Data is not null) foreach (var c in cls.Data) Classes.Add(c);

        var buses = await _api.BusesAsync();
        if (buses.Ok && buses.Data is not null) foreach (var b in buses.Data) Buses.Add(b);

        if (_studentId is int id)
        {
            var r = await _api.GetAsync<StudentDetailResponse>($"api/students/{id}");
            if (r.Ok && r.Data is not null)
            {
                var d = r.Data;
                FullName = d.FullName;
                RollNo = d.RollNo;
                DateOfBirth = d.DateOfBirth.ToDateTime(TimeOnly.MinValue);
                FatherName = d.FatherName;
                FatherPhone = d.FatherPhone;
                MotherPhone = d.MotherPhone ?? string.Empty;
                Address = d.Address;
                IsActive = d.IsActive;
                SelectedClass = Classes.FirstOrDefault(c => c.Id == d.ClassId);
                SelectedBus = Buses.FirstOrDefault(b => b.Id == d.BusId);

                if (!string.IsNullOrWhiteSpace(d.PhotoUrl))
                {
                    try
                    {
                        var abs = _api.BaseUrl.TrimEnd('/') + d.PhotoUrl;
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(abs);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                        SetPreview(bmp);
                    }
                    catch { /* photo optional */ }
                }
            }
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        FormError = string.Empty;
        if (SelectedClass is null)
        {
            FormError = "Class select karein.";
            return;
        }
        ValidateAllProperties();
        if (HasErrors)
        {
            FormError = "Form mein ghaltiyan hain — red fields theek karein.";
            return;
        }
        if (DateOfBirth > DateTime.Today.AddYears(-3))
        {
            FormError = "Date of birth theek nahi lag raha (kam az kam 3 saal purana).";
            return;
        }

        var body = new StudentUpsertRequest
        {
            FullName = FullName.Trim(),
            RollNo = RollNo.Trim(),
            ClassId = SelectedClass.Id,
            DateOfBirth = DateOfBirth.ToString("yyyy-MM-dd"),
            FatherName = FatherName.Trim(),
            FatherPhone = FatherPhone.Trim(),
            MotherPhone = string.IsNullOrWhiteSpace(MotherPhone) ? null : MotherPhone.Trim(),
            Address = Address.Trim(),
            BusId = SelectedBus?.Id,
            IsActive = _studentId is null ? null : IsActive
        };

        IsBusy = true;
        try
        {
            var r = _studentId is int id
                ? await _api.PutAsync<StudentDetailResponse>($"api/students/{id}", body)
                : await _api.PostAsync<StudentDetailResponse>("api/students", body);

            if (r.Ok)
            {
                // Photo upload (after student exists — endpoint needs the id)
                var sid = _studentId ?? r.Data?.Id;
                if (sid is int stuId && PhotoPath is not null)
                {
                    var bytes = ToJpegBytes(PhotoPath);
                    var (upOk, upErr) = await _api.UploadBytesAsync(
                        $"api/students/{stuId}/photo", bytes, "photo.jpg", "image/jpeg");
                    if (!upOk)
                        ToastService.Current.Info("Photo", $"Photo upload fail raha: {upErr}");
                }
                ToastService.Current.Info("Student", _studentId is null
                    ? $"{FullName} add ho gaya." : $"{FullName} update ho gaya.");
                SavedAndClose?.Invoke();
            }
            else
            {
                FormError = r.Error;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand] private void Cancel() => Cancelled?.Invoke();
}
