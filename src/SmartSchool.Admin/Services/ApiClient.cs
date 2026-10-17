using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSchool.Admin.Models;

namespace SmartSchool.Admin.Services;

/// <summary>
/// Thin typed wrapper over HttpClient:
///  - base URL from appsettings.json
///  - JWT attached on every request
///  - silently refreshes the token (re-login) when near expiry
///  - unwraps the { success, message, data, errors } envelope
/// </summary>
public sealed class ApiClient
{
    private static readonly Lazy<ApiClient> _lazy = new(() => new ApiClient());
    public static ApiClient Instance => _lazy.Value;

    private readonly HttpClient _http;
    private readonly SecureTokenStore _tokenStore = new();
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string BaseUrl { get; }

    public ApiClient()
    {
        // appsettings.json is OPTIONAL: standalone exe (unpackaged) falls back to localhost API.
        BaseUrl = "http://localhost:5000";
        try
        {
            var cfgFrom = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (!File.Exists(cfgFrom)) cfgFrom = "appsettings.json";
            if (File.Exists(cfgFrom))
            {
                var cfg = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(cfgFrom));
                BaseUrl = cfg.GetProperty("Api").GetProperty("BaseUrl").GetString() ?? BaseUrl;
            }
        }
        catch { /* bad/missing config → keep default */ }
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl.TrimEnd('/') + "/") };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public SecureTokenStore TokenStore => _tokenStore;

    /// <summary>Raw file download with progress (update assets). False on any failure.</summary>
    public async Task<bool> DownloadFileAsync(string url, string savePath, IProgress<int>? progress = null)
    {
        try
        {
            AttachToken();
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return false;

            var total = response.Content.Headers.ContentLength ?? -1;
            await using var src = await response.Content.ReadAsStreamAsync();
            await using var dst = File.Create(savePath);

            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n));
                read += n;
                if (total > 0) progress?.Report((int)(read * 100 / total));
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void AttachToken()
    {
        var token = AppSession.Current.Token;
        _http.DefaultRequestHeaders.Authorization =
            string.IsNullOrEmpty(token) ? null : new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>Re-login using stored credentials when the JWT is about to expire.</summary>
    public async Task<bool> EnsureFreshTokenAsync()
    {
        if (!AppSession.Current.IsExpired) return true;
        var stored = _tokenStore.Load();
        if (stored is null) return false;
        var s = stored.Value;
        if (s.Username is null || s.Password is null) return false;

        var (ok, login, _) = await LoginAsync(s.Username, s.Password, remember: true);
        return ok && login is not null;
    }

    public async Task<(bool Ok, LoginResponse? Data, string Error)> LoginAsync(
        string username, string password, bool remember)
    {
        var response = await SendAsync<LoginResponse>(HttpMethod.Post, "api/auth/login",
            new LoginRequest { Username = username, Password = password });
        if (response.Ok && response.Data is not null)
        {
            AppSession.Current.SignIn(response.Data);
            var u = response.Data.User!;
            _tokenStore.Save(response.Data.Token, response.Data.ExpiresAtUtc, u.Id, u.FullName, u.Role,
                remember ? username : null, remember ? password : null);
        }
        return (response.Ok, response.Data, response.Error);
    }

    // ---- generic envelope-aware calls ----
    private async Task<(bool Ok, T? Data, string Error, string Message)> SendAsync<T>(
        HttpMethod method, string url, object? body = null)
    {
        try
        {
            if (AppSession.Current.IsLoggedIn) await EnsureFreshTokenAsync();
            AttachToken();

            using var request = new HttpRequestMessage(method, url);
            if (body is not null)
                request.Content = JsonContent.Create(body, options: _json);

            using var response = await _http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();

            ApiResponse<T>? envelope;
            try { envelope = JsonSerializer.Deserialize<ApiResponse<T>>(text, _json); }
            catch { return (false, default, $"Server error {(int)response.StatusCode}: {response.ReasonPhrase}", ""); }

            if (envelope is null)
                return (false, default, $"Unexpected server reply ({(int)response.StatusCode}).", "");

            if (!response.IsSuccessStatusCode || !envelope.Success)
            {
                var err = envelope.Errors.Count > 0
                    ? string.Join(Environment.NewLine, envelope.Errors)
                    : envelope.Message;
                return (false, envelope.Data, string.IsNullOrWhiteSpace(err) ? $"HTTP {(int)response.StatusCode}" : err,
                    envelope.Message);
            }
            return (true, envelope.Data, "", envelope.Message);
        }
        catch (HttpRequestException ex)
        {
            return (false, default, $"API se rabta nahi ho saka ({BaseUrl}) — kya backend chal raha hai?\n{ex.Message}", "");
        }
        catch (TaskCanceledException)
        {
            return (false, default, "Request time-out — API slow hai ya band hai.", "");
        }
    }

    // ---- typed endpoint helpers ----
    public Task<(bool Ok, T? Data, string Error, string Msg)> GetAsync<T>(string url)
        => SendAsync<T>(HttpMethod.Get, url);
    public Task<(bool Ok, T? Data, string Error, string Msg)> PostAsync<T>(string url, object body)
        => SendAsync<T>(HttpMethod.Post, url, body);
    public Task<(bool Ok, T? Data, string Error, string Msg)> PutAsync<T>(string url, object body)
        => SendAsync<T>(HttpMethod.Put, url, body);
    public async Task<(bool Ok, string Error)> DeleteAsync(string url)
    {
        var r = await SendAsync<object>(HttpMethod.Delete, url);
        return (r.Ok, r.Error);
    }

    // Convenience queries
    public Task<(bool Ok, DashboardSummary? Data, string Error, string Msg)> DashboardAsync()
        => GetAsync<DashboardSummary>("api/dashboard/summary");

    public Task<(bool Ok, PagedResult<StudentListItem>? Data, string Error, string Msg)> StudentsAsync(
        string? search = null, int? classId = null, int page = 1, int pageSize = 50)
    {
        var q = $"api/students?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search)) q += $"&search={Uri.EscapeDataString(search)}";
        if (classId is not null) q += $"&classId={classId}";
        return GetAsync<PagedResult<StudentListItem>>(q);
    }

    public Task<(bool Ok, List<ClassListItem>? Data, string Error, string Msg)> ClassesAsync()
        => GetAsync<List<ClassListItem>>("api/classes");

    public Task<(bool Ok, List<BusListItem>? Data, string Error, string Msg)> BusesAsync()
        => GetAsync<List<BusListItem>>("api/buses");

    public Task<(bool Ok, List<DriverListItem>? Data, string Error, string Msg)> DriversAsync()
        => GetAsync<List<DriverListItem>>("api/drivers");

    public Task<(bool Ok, PagedResult<FeeListItem>? Data, string Error, string Msg)> FeesAsync(
        int? studentId = null, string? month = null, string? status = null, int page = 1, int pageSize = 50)
    {
        var q = $"api/fees?page={page}&pageSize={pageSize}";
        if (studentId is not null) q += $"&studentId={studentId}";
        if (!string.IsNullOrWhiteSpace(month)) q += $"&month={Uri.EscapeDataString(month)}";
        if (!string.IsNullOrWhiteSpace(status)) q += $"&status={Uri.EscapeDataString(status)}";
        return GetAsync<PagedResult<FeeListItem>>(q);
    }

    public Task<(bool Ok, DuesSummaryResponse? Data, string Error, string Msg)> DuesAsync()
        => GetAsync<DuesSummaryResponse>("api/fees/dues");

    public Task<(bool Ok, List<BusEventListItem>? Data, string Error, string Msg)> TodayAttendanceAsync(
        int? busId = null)
    {
        var q = "api/bus-attendance/today";
        if (busId is not null) q += $"?busId={busId}";
        return GetAsync<List<BusEventListItem>>(q);
    }
}
