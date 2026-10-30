using System.IO;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels;

public partial class WhatsAppQrTestViewModel : ObservableObject, IDisposable
{
    private readonly ApiClient _api = ApiClient.Instance;
    private readonly DispatcherTimer _timer;
    private bool _refreshing;
    private bool _disposed;
    private string _state = "disabled";

    [ObservableProperty] private string _statusText = "Status check ho raha hai…";
    [ObservableProperty] private string _feedback = "";
    [ObservableProperty] private string _testPhone = "";
    [ObservableProperty] private bool _riskConfirmed;
    [ObservableProperty] private bool _consentConfirmed;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private ImageSource? _qrImage;
    [ObservableProperty] private bool _liveEnabled;
    [ObservableProperty] private string _liveStatus = "Real parent routing band hai.";
    [ObservableProperty] private bool _testReceivedConfirmed;
    [ObservableProperty] private bool _liveRiskConfirmed;
    [ObservableProperty] private string _studentSearch = "";
    [ObservableProperty] private StudentListItem? _selectedStudent;
    [ObservableProperty] private WhatsAppConsentContact? _selectedContact;
    [ObservableProperty] private bool _parentPermissionConfirmed;
    [ObservableProperty] private string _consentFeedback = "";
    public ObservableCollection<StudentListItem> Students { get; } = new();
    public ObservableCollection<WhatsAppConsentContact> Contacts { get; } = new();
    public ObservableCollection<WhatsAppLogListItem> RecentLogs { get; } = new();

    public bool IsIdle => !IsBusy;
    public bool CanSend => IsIdle && IsConnected;
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(CanSend)); }
    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(CanSend));

    public WhatsAppQrTestViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_disposed || _refreshing || IsBusy) return;
        _refreshing = true;
        try
        {
            var r = await _api.GetAsync<QrTestReply>("api/whatsapp-qr-test/status");
            if (_disposed) return;
            if (!r.Ok || r.Data is null || !r.Data.Ok)
            {
                _state = "unavailable";
                IsConnected = false;
                QrImage = null;
                StatusText = !string.IsNullOrWhiteSpace(r.Error) ? r.Error : r.Data?.Error ?? "Test service available nahi hai.";
                return;
            }
            _state = r.Data.State ?? "off";
            IsConnected = _state == "connected";
            LiveEnabled = r.Data.LiveEnabled == true;
            LiveStatus = !LiveEnabled ? "Real parent routing band hai — sirf test." :
                r.Data.LiveSessionMatches == true ? "Real parent routing ON: sirf naye eligible alerts." :
                "Real routing ON hai lekin authorized account connected nahi; messages nahi bheje ja rahe.";
            StatusText = _state switch
            {
                "disabled" or "off" => "Test mode band hai",
                "connecting" => "Test WhatsApp se rabta ho raha hai…",
                "awaiting_scan" => "Test phone se QR scan karein",
                "connected" => "Test WhatsApp connected hai",
                "reconnecting" => "Connection toot gaya — dobara rabta ho raha hai…",
                "needs_link" => "QR dobara shuru karna hoga",
                "needs_relink" => "Session khatam — pehle unlink/reset karein",
                "session_error" => "Session mein masla — unlink/reset karke dobara shuru karein",
                "unlinking" => "Test session unlink ho rahi hai…",
                _ => "Test connection ka status check ho raha hai…"
            };
            if (_state != "awaiting_scan") { QrImage = null; return; }
            var qr = await _api.GetAsync<QrTestReply>("api/whatsapp-qr-test/qr");
            if (_disposed) return;
            QrImage = qr.Ok && qr.Data?.Ok == true ? DecodeQr(qr.Data.QrDataUrl) : null;
        }
        finally { _refreshing = false; }
    }

    private static ImageSource? DecodeQr(string? data)
    {
        const string prefix = "data:image/png;base64,";
        if (data is null || !data.StartsWith(prefix, StringComparison.Ordinal) || data.Length > 200_000) return null;
        try
        {
            var bytes = Convert.FromBase64String(data[prefix.Length..]);
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsBusy) return;
        if (!RiskConfirmed)
        {
            Feedback = "Pehle unofficial WhatsApp aur test number ke risk wala box tick karein.";
            return;
        }
        if (MessageBox.Show("Sirf ALAG test WhatsApp number link karein, school ka live number nahi. " +
                            "Unofficial connection se account restrict ya ban ho sakta hai.\n\nQR test shuru karein?",
                "WhatsApp — sirf test", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        IsBusy = true;
        Feedback = "Test session shuru ho rahi hai…";
        try
        {
            var r = await _api.PostAsync<QrTestReply>("api/whatsapp-qr-test/start", new { riskConfirmed = true });
            Feedback = r.Ok && r.Data?.Ok == true
                ? "QR aaye to test phone par WhatsApp → Linked devices → Link a device se scan karein. QR kisi ko na bhejein."
                : r.Data?.Error ?? r.Error;
        }
        finally { IsBusy = false; }
        await RefreshAsync();
    }

    [RelayCommand]
    private Task SendPickAsync() => SendAsync("pick");

    [RelayCommand]
    private Task SendDropAsync() => SendAsync("drop");

    private async Task SendAsync(string type)
    {
        if (IsBusy || !IsConnected) return;
        var phone = TestPhone.Trim();
        if (!ConsentConfirmed || !Regex.IsMatch(phone, @"^\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant))
        {
            Feedback = "International format mein test number (+92…) likhein aur recipient ki ijazat confirm karein.";
            return;
        }
        if (MessageBox.Show($"Sirf sample {(type == "pick" ? "pick-in" : "drop-out")} message {phone} par bhejna hai?\n\n" +
                            "Yeh asli student ya bus event nahi hai.", "Test message ki tasdeeq",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        IsBusy = true;
        Feedback = "Test message submit ho raha hai…";
        try
        {
            var r = await _api.PostAsync<QrTestReply>("api/whatsapp-qr-test/send", new
            {
                phone, type, consentConfirmed = true, requestId = Guid.NewGuid()
            });
            if (!r.Ok || r.Data is null)
                Feedback = "Nateeja maloom nahi. Dobara bhejne se pehle test phone check karein. " + r.Error;
            else if (r.Data.Result == "submitted")
                Feedback = "Test message submit ho gaya; delivery confirm nahi hai. Test phone check karein.";
            else if (r.Data.Result == "unknown")
                Feedback = "Nateeja maloom nahi. Dobara bhejne se pehle test phone zaroor check karein.";
            else
                Feedback = r.Data.Error ?? "Test message send nahi hua.";
        }
        finally { IsBusy = false; }
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task UnlinkAsync()
    {
        if (IsBusy) return;
        if (MessageBox.Show("Test session unlink/reset karni hai? Is se sirf test link ka local data hatega. " +
                            "Agar phone se unlink na ho sake to WhatsApp Linked devices mein khud bhi remove karein.",
                "Test session unlink", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        IsBusy = true;
        QrImage = null;
        try
        {
            var r = await _api.PostAsync<QrTestReply>("api/whatsapp-qr-test/unlink", new { confirm = true });
            Feedback = r.Ok && r.Data?.Ok == true
                ? r.Data.PhoneUnlinked == true
                    ? "Test session unlink ho gayi."
                    : "Local test session hat gayi. Phone ke Linked devices mein bhi check karke device remove karein."
                : r.Data?.Error ?? r.Error;
        }
        finally { IsBusy = false; }
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ActivateLiveAsync()
    {
        if (IsBusy || !IsConnected) return;
        if (!TestReceivedConfirmed || !LiveRiskConfirmed)
        {
            Feedback = "Pehle test phone par dono samples receive hone aur risk ki tasdeeq karein.";
            return;
        }
        if (MessageBox.Show("Ab connected number se ijazat walay ASLI parents ko NAYE pick/drop messages ja sakte hain. " +
            "Purane queued, held ya stale messages nahi bheje jayenge. Unofficial WhatsApp se account restrict ho sakta hai.\n\n" +
            "Kya aap test ke baad real parent routing ON karne ki ijazat dete hain?",
            "Real parents ke liye alag tasdeeq", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        IsBusy = true;
        try
        {
            var r = await _api.PostAsync<QrTestReply>("api/whatsapp-qr-test/activate-live", new { testReceived = true, riskConfirmed = true });
            Feedback = r.Ok && r.Data?.Ok == true ? "Real routing on hai; sirf nayi aur consented notifications." : r.Data?.Error ?? r.Error;
        }
        finally { IsBusy = false; }
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeactivateLiveAsync()
    {
        if (IsBusy || MessageBox.Show("Real parent routing band karein? Jo send pehle shuru ho chuka ho woh mukammal ho sakta hai.",
            "Real routing band", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        IsBusy = true;
        try
        {
            var r = await _api.PostAsync<QrTestReply>("api/whatsapp-qr-test/deactivate-live", new { confirm = true });
            Feedback = r.Ok && r.Data?.Ok == true ? "Real routing band hai; purane alerts baad mein khud nahi jayenge." : r.Data?.Error ?? r.Error;
        }
        finally { IsBusy = false; }
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SearchStudentsAsync()
    {
        if (IsBusy) return;
        if (StudentSearch.Trim().Length < 2) { ConsentFeedback = "Student name ya roll number ke kam az kam 2 haroof likhein."; return; }
        IsBusy = true;
        try
        {
            var r = await _api.StudentsAsync(StudentSearch.Trim(), pageSize: 25);
            Students.Clear(); Contacts.Clear(); SelectedStudent = null; SelectedContact = null;
            if (r.Ok && r.Data is not null)
            {
                foreach (var student in r.Data.Items.Where(s => s.IsActive)) Students.Add(student);
                ConsentFeedback = Students.Count == 0 ? "Koi active student nahi mila." : "List se student select karke numbers load karein.";
            }
            else ConsentFeedback = r.Error;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task LoadContactsAsync()
    {
        if (IsBusy || SelectedStudent is null) return;
        IsBusy = true;
        try
        {
            var r = await _api.GetAsync<WhatsAppConsentStudent>($"api/whatsapp-consents/student/{SelectedStudent.Id}");
            Contacts.Clear(); SelectedContact = null; ParentPermissionConfirmed = false;
            if (r.Ok && r.Data is not null)
            {
                foreach (var contact in r.Data.Contacts) Contacts.Add(contact);
                ConsentFeedback = Contacts.Count == 0 ? "Student ka valid father/mother number nahi mila." :
                    $"{r.Data.StudentName}: number select karein; parent opt-out hamesha lagu hai.";
            }
            else ConsentFeedback = r.Error;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] private Task GrantConsentAsync() => ChangeConsentAsync(true);
    [RelayCommand] private Task RevokeConsentAsync() => ChangeConsentAsync(false);
    private async Task ChangeConsentAsync(bool grant)
    {
        if (IsBusy || SelectedStudent is null || SelectedContact is null) return;
        if (grant && !ParentPermissionConfirmed) { ConsentFeedback = "Parent ki explicit ijazat pehle hasil aur confirm karein."; return; }
        var phone = SelectedContact.Phone;
        if (MessageBox.Show($"{SelectedStudent.FullName} — {phone}: " +
            (grant ? "parent ki hasil-shuda ijazat record karein?" : "ijazat revoke karein?"),
            "WhatsApp permission", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        IsBusy = true;
        try
        {
            var r = await _api.PostAsync<object>("api/whatsapp-consents", new
            {
                studentId = SelectedStudent.Id, phone, granted = grant, consentConfirmed = grant && ParentPermissionConfirmed
            });
            ConsentFeedback = r.Ok ? r.Msg : r.Error;
        }
        finally { IsBusy = false; }
        await LoadContactsAsync();
    }

    [RelayCommand]
    private async Task LoadLogsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var r = await _api.GetAsync<PagedResult<WhatsAppLogListItem>>("api/whatsapp-logs?page=1&pageSize=50");
            RecentLogs.Clear();
            if (r.Ok && r.Data is not null)
                foreach (var row in r.Data.Items) RecentLogs.Add(row);
            else Feedback = r.Error;
        }
        finally { IsBusy = false; }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        QrImage = null;
        TestPhone = "";
    }
}
