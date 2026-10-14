using SmartSchool.Admin.Models;

namespace SmartSchool.Admin.Services;

/// <summary>
/// Holds the signed-in user + JWT for the whole app lifetime.
/// Static Current so any ViewModel/window can reach the session.
/// </summary>
public sealed class AppSession
{
    public static AppSession Current { get; set; } = new();

    public string Token { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public UserInfoDto? User { get; private set; }

    public bool IsLoggedIn => !string.IsNullOrEmpty(Token) && User is not null;
    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc.AddMinutes(-2);

    public void SignIn(LoginResponse login)
    {
        Token = login.Token;
        ExpiresAtUtc = login.ExpiresAtUtc;
        User = login.User;
    }

    public void SignOut()
    {
        Token = string.Empty;
        ExpiresAtUtc = default;
        User = null;
    }
}
