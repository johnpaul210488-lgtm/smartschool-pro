using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using SmartSchool.Admin.Models;

namespace SmartSchool.Admin.Services;

/// <summary>
/// Wraps the SignalR HubConnection to /hubs/notifications:
///  - auto-reconnect with backoff
///  - joins AdminGroup after connect
///  - publishes strongly-typed messages via WeakReferenceMessenger so any
///    ViewModel can subscribe (live grid, toast, map…)
/// </summary>
public sealed class SignalRClientService : IAsyncDisposable
{
    private HubConnection? _conn;
    private readonly string _hubUrl;
    private bool _started;

    public bool IsConnected => _conn?.State == HubConnectionState.Connected;

    public event Action<string>? ConnectionStateChanged; // human-readable status for the UI

    public SignalRClientService()
    {
        // Prefer the config next to the exe (works from any working directory).
        var cfgFrom = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        if (!File.Exists(cfgFrom)) cfgFrom = "appsettings.json";
        var cfg = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(cfgFrom));
        var baseUrl = cfg.GetProperty("Api").GetProperty("BaseUrl").GetString() ?? "http://localhost:5000";
        var hubPath = cfg.GetProperty("HubPath").GetString() ?? "/hubs/notifications";
        _hubUrl = baseUrl.TrimEnd('/') + hubPath;
    }

    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;

        _conn = new HubConnectionBuilder()
            .WithUrl(_hubUrl, opts => opts.AccessTokenProvider = () =>
                Task.FromResult<string?>(AppSession.Current.Token))
            .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
            .AddJsonProtocol()
            .Build();

        _conn.On<BusEventNotification>("SendPickInAlert", n =>
            Dispatch(() => WeakReferenceMessenger.Default.Send(new PickInAlertMessage(n))));
        _conn.On<BusEventNotification>("SendDropOutAlert", n =>
            Dispatch(() => WeakReferenceMessenger.Default.Send(new DropOutAlertMessage(n))));
        _conn.On<BusLocationUpdate>("UpdateBusLocation", u =>
            Dispatch(() => WeakReferenceMessenger.Default.Send(new BusLocationMessage(u))));
        _conn.On<TripNotification>("SendTripUpdate", t =>
            Dispatch(() => WeakReferenceMessenger.Default.Send(new TripUpdateMessage(t))));
        _conn.On<AdminAlertNotification>("SendAdminAlert", a =>
            Dispatch(() => WeakReferenceMessenger.Default.Send(new AdminAlertMessage(a))));

        _conn.Closed += _ => { Notify("⚠️ Live connection band — dobara koshish…"); return Task.CompletedTask; };
        _conn.Reconnecting += _ => { Notify("🔄 Dobara connect ho raha hai…"); return Task.CompletedTask; };
        _conn.Reconnected += _ => { Notify("✅ Live connection wapas aa gaya"); return Task.CompletedTask; };

        try
        {
            await _conn.StartAsync();
            Notify("✅ Real-time alerts ON (SignalR connected)");
        }
        catch (Exception ex)
        {
            Notify($"❌ SignalR connect fail: {ex.Message} (API chal rahi hai?)");
        }
    }

    private static void Dispatch(Action action)
        => Application.Current?.Dispatcher.Invoke(action);

    private void Notify(string status)
        => Application.Current?.Dispatcher.Invoke(() => ConnectionStateChanged?.Invoke(status));

    public async Task WatchBusAsync(int busId)
    {
        if (IsConnected) await _conn!.InvokeAsync("WatchBus", busId);
    }

    public async Task UnwatchBusAsync(int busId)
    {
        if (IsConnected) await _conn!.InvokeAsync("UnwatchBus", busId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_conn is not null) await _conn.DisposeAsync();
    }
}

// Strongly-typed messages shared through WeakReferenceMessenger.
public sealed record PickInAlertMessage(BusEventNotification Payload);
public sealed record DropOutAlertMessage(BusEventNotification Payload);
public sealed record BusLocationMessage(BusLocationUpdate Payload);
public sealed record TripUpdateMessage(TripNotification Payload);
public sealed record AdminAlertMessage(AdminAlertNotification Payload);
