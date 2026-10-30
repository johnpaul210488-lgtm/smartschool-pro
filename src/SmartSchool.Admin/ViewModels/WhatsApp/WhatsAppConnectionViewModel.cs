using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSchool.Admin.Models;
using SmartSchool.Admin.Services;

namespace SmartSchool.Admin.ViewModels.WhatsApp;

public partial class WhatsAppConnectionViewModel : ObservableObject, IDisposable
{
    private readonly ApiClient _api = ApiClient.Instance;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _disposed;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _termsAccepted;
    [ObservableProperty] private string _provider = "—";
    [ObservableProperty] private string _statusTitle = "Checking connection…";
    [ObservableProperty] private string _statusDetail = "Please wait while the local gateway is checked.";
    [ObservableProperty] private string _connectedPhone = "—";
    [ObservableProperty] private string _complianceNotice =
        "Linked-device mode is unofficial and cannot guarantee against WhatsApp account restrictions.";
    [ObservableProperty] private string _safetySummary = "Safety counters will appear when the gateway is ready.";
    [ObservableProperty] private string _lastUpdated = string.Empty;
    [ObservableProperty] private ImageSource? _qrImage;
    [ObservableProperty] private bool _hasQr;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _hasSavedSession;
    [ObservableProperty] private bool _isLinkedDeviceProvider;
    [ObservableProperty] private string _studentSearch = string.Empty;
    [ObservableProperty] private StudentListItem? _selectedStudent;
    [ObservableProperty] private WhatsAppConsentContact? _selectedContact;
    [ObservableProperty] private bool _parentPermissionConfirmed;
    [ObservableProperty] private string _consentFeedback =
        "Search a student, load the current father/mother numbers, then record permission received from that recipient.";

    public ObservableCollection<StudentListItem> Students { get; } = new();
    public ObservableCollection<WhatsAppConsentContact> Contacts { get; } = new();
    public bool IsIdle => !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsIdle));

    public WhatsAppConnectionViewModel()
    {
        _ = RefreshCoreAsync(showError: false);
        _ = PollAsync();
    }

    [RelayCommand]
    private Task RefreshAsync() => RefreshCoreAsync(showError: true);

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (!TermsAccepted)
        {
            ToastService.Current.Error(
                "WhatsApp terms",
                "Pehle compliance/unofficial-client notice parh kar acceptance checkbox select karein.");
            return;
        }
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            var result = await _api.PostAsync<WhatsAppSessionInfoDto>(
                "api/whatsapp/session/connect",
                new { acceptTermsAndRisk = true });
            if (!result.Ok || result.Data is null)
            {
                ToastService.Current.Error("WhatsApp connection", result.Error);
                return;
            }
            Apply(result.Data);
            ToastService.Current.Info(
                "WhatsApp connection",
                result.Data.IsConnected ? "Previous session restore ho gaya." : "QR tayar ho raha hai — WhatsApp > Linked devices se scan karein.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UnlinkAsync()
    {
        if (IsBusy) return;
        var answer = MessageBox.Show(
            "Is WhatsApp device ko unlink karne se encrypted session is PC se erase ho jayega. " +
            "Dobara use karne ke liye naya QR scan karna hoga.\n\nUnlink karein?",
            "Unlink WhatsApp device",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var result = await _api.PostAsync<WhatsAppSessionInfoDto>(
                "api/whatsapp/session/logout",
                new { confirmUnlink = true });
            if (!result.Ok || result.Data is null)
            {
                ToastService.Current.Error("WhatsApp unlink", result.Error);
                return;
            }
            TermsAccepted = false;
            Apply(result.Data);
            ToastService.Current.Info("WhatsApp unlink", "Linked session aur local encrypted credentials erase ho gaye.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PollAsync()
    {
        var token = _lifetime.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                await RefreshCoreAsync(showError: false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task RefreshCoreAsync(bool showError)
    {
        if (_disposed || !await _refreshGate.WaitAsync(0)) return;
        try
        {
            var result = await _api.GetAsync<WhatsAppSessionInfoDto>("api/whatsapp/session");
            if (result.Ok && result.Data is not null)
            {
                Apply(result.Data);
            }
            else if (showError)
            {
                ToastService.Current.Error("WhatsApp status", result.Error);
            }
        }
        catch (Exception ex)
        {
            if (showError) ToastService.Current.Error("WhatsApp status", ex.Message);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void Apply(WhatsAppSessionInfoDto session)
    {
        Provider = session.Provider;
        IsLinkedDeviceProvider = string.Equals(session.Provider, "LinkedDevice", StringComparison.OrdinalIgnoreCase);
        IsConnected = session.IsConnected;
        HasSavedSession = session.HasSavedSession;
        ConnectedPhone = session.ConnectedPhone ?? "—";
        ComplianceNotice = string.IsNullOrWhiteSpace(session.ComplianceNotice)
            ? ComplianceNotice
            : session.ComplianceNotice;
        LastUpdated = $"Last checked: {DateTime.Now:hh:mm:ss tt}";

        (StatusTitle, StatusDetail) = (session.Status ?? string.Empty).ToLowerInvariant() switch
        {
            "connected" => ("Connected", $"Linked number: {session.ConnectedPhone ?? "unknown"}. Encrypted session auto-restores after restart."),
            "qr" => ("Scan the current QR code", "Phone par WhatsApp > Linked devices > Link a device khol kar QR scan karein. QR short-lived hai aur khud refresh hota hai."),
            "connecting" => ("Connecting…", "WhatsApp ke saath secure linked-device session establish ho raha hai."),
            "reconnecting" => ("Reconnecting with backoff…", $"Network interruption ke baad attempt {session.ReconnectAttempt}. Outbox messages database mein mehfooz hain."),
            "logged_out" => ("Session logged out", session.LastError ?? "Naya QR link required hai."),
            "blocked" => ("WhatsApp refused the connection", session.LastError ?? "Sending rok dein aur WhatsApp review process follow karein."),
            "unavailable" => ("Local gateway unavailable", session.LastError ?? "API supervisor gateway ko restart karega."),
            "dry_run" => ("Dry-run provider", "Koi live WhatsApp message send nahi hoga; outbox flow test mode mein hai."),
            "managed_provider" => ("Managed provider selected", "Twilio credentials/API se sending hoti hai; QR linking apply nahi karta."),
            "configuration_error" => ("Provider configuration error", session.LastError ?? "Check the server WhatsApp provider configuration."),
            _ => ("Disconnected", session.LastError ?? "Terms accept karke QR connection start karein.")
        };

        HasQr = !string.IsNullOrWhiteSpace(session.QrDataUrl);
        QrImage = HasQr ? DecodeDataImage(session.QrDataUrl!) : null;
        SafetySummary = session.Safety is null
            ? "Linked-device safety counters are not available for the current provider/state."
            : $"Today: {session.Safety.SentToday} confirmed / {session.Safety.AttemptedToday} attempts " +
              $"(limit {session.Safety.MaxMessagesPerDay})  •  Random spacing " +
              $"{session.Safety.MinSendIntervalMs / 1000.0:0.#}–{session.Safety.MaxSendIntervalMs / 1000.0:0.#} sec  •  " +
              $"Per recipient/hour: {session.Safety.MaxMessagesPerRecipientHour}  •  Pending: {session.Safety.PendingRequests}";
    }

    [RelayCommand]
    private async Task SearchStudentsAsync()
    {
        if (IsBusy) return;
        if (StudentSearch.Trim().Length < 2)
        {
            ConsentFeedback = "Enter at least two letters of the student name or roll number.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _api.StudentsAsync(StudentSearch.Trim(), pageSize: 25);
            Students.Clear();
            Contacts.Clear();
            SelectedStudent = null;
            SelectedContact = null;
            if (result.Ok && result.Data is not null)
            {
                foreach (var student in result.Data.Items.Where(s => s.IsActive))
                    Students.Add(student);
                ConsentFeedback = Students.Count == 0
                    ? "No active student matched the search."
                    : "Select a student, then load the current recipient numbers.";
            }
            else
            {
                ConsentFeedback = result.Error;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadContactsAsync()
    {
        if (IsBusy || SelectedStudent is null) return;
        IsBusy = true;
        try
        {
            var result = await _api.GetAsync<WhatsAppConsentStudent>(
                $"api/whatsapp-consents/student/{SelectedStudent.Id}");
            Contacts.Clear();
            SelectedContact = null;
            ParentPermissionConfirmed = false;
            if (result.Ok && result.Data is not null)
            {
                foreach (var contact in result.Data.Contacts)
                    Contacts.Add(contact);
                ConsentFeedback = Contacts.Count == 0
                    ? "This student has no valid father/mother number."
                    : $"{result.Data.StudentName}: select the recipient. A parent opt-out always takes precedence.";
            }
            else
            {
                ConsentFeedback = result.Error;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task GrantConsentAsync() => ChangeConsentAsync(grant: true);

    [RelayCommand]
    private Task RevokeConsentAsync() => ChangeConsentAsync(grant: false);

    private async Task ChangeConsentAsync(bool grant)
    {
        if (IsBusy || SelectedStudent is null || SelectedContact is null) return;
        if (grant && !ParentPermissionConfirmed)
        {
            ConsentFeedback = "Confirm that this recipient explicitly agreed before recording permission.";
            return;
        }

        var verb = grant ? "record permission for" : "revoke permission for";
        if (MessageBox.Show(
                $"{SelectedStudent.FullName}: {verb} {SelectedContact.Phone}?",
                "WhatsApp recipient permission",
                MessageBoxButton.YesNo,
                grant ? MessageBoxImage.Question : MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            var result = await _api.PostAsync<object>("api/whatsapp-consents", new
            {
                studentId = SelectedStudent.Id,
                phone = SelectedContact.Phone,
                granted = grant,
                consentConfirmed = grant && ParentPermissionConfirmed
            });
            ConsentFeedback = result.Ok ? result.Msg : result.Error;
        }
        finally
        {
            IsBusy = false;
        }
        await LoadContactsAsync();
    }

    private static ImageSource? DecodeDataImage(string dataUrl)
    {
        try
        {
            var comma = dataUrl.IndexOf(',');
            if (comma < 0) return null;
            var bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
