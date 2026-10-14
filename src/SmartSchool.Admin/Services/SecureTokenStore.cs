using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmartSchool.Admin.Services;

/// <summary>
/// Persists the JWT (and optionally credentials for auto-refresh) to an encrypted
/// local file under %LocalAppData% using Windows DPAPI (CurrentUser scope).
/// Only decryptable by the same Windows user on the same PC.
/// </summary>
public sealed class SecureTokenStore
{
    private static readonly string DirPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartSchoolAdmin");
    private static readonly string FilePath = Path.Combine(DirPath, "session.bin");

    private sealed record StoredSession(string Token, DateTime ExpiresAtUtc, string? Username, string? Password,
        int UserId, string FullName, string Role);

    public void Save(string token, DateTime expiresAtUtc, int userId, string fullName, string role,
        string? username = null, string? password = null)
    {
        var json = JsonSerializer.Serialize(new StoredSession(token, expiresAtUtc, username, password,
            userId, fullName, role));
        var raw = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(DirPath);
        File.WriteAllBytes(FilePath, encrypted);
    }

    public (string Token, DateTime ExpiresAtUtc, string? Username, string? Password,
        int UserId, string FullName, string Role)? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var encrypted = File.ReadAllBytes(FilePath);
            var raw = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var s = JsonSerializer.Deserialize<StoredSession>(Encoding.UTF8.GetString(raw));
            return s is null ? null : (s.Token, s.ExpiresAtUtc, s.Username, s.Password, s.UserId, s.FullName, s.Role);
        }
        catch
        {
            return null; // corrupt or other user's file
        }
    }

    public void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { /* ignore */ }
    }
}
