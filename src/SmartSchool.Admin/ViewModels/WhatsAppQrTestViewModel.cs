using System.IO;
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

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        QrImage = null;
        TestPhone = "";
    }
}
