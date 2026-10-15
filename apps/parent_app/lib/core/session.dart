import 'package:shared_preferences/shared_preferences.dart';

/// JWT + profile persistence (survives app restarts for auto-login).
class Session {
  static const _kToken = 'jwt';
  static const _kUsername = 'username';
  static const _kFullName = 'full_name';
  static const _kExpires = 'expires_utc';

  static String? token;
  static String? username;
  static String? fullName;
  static DateTime? expiresAtUtc;

  static bool get isLoggedIn =>
      token != null &&
      (expiresAtUtc == null || expiresAtUtc!.isAfter(DateTime.now().toUtc()));

  static Future<void> load() async {
    final prefs = await SharedPreferences.getInstance();
    token = prefs.getString(_kToken);
    username = prefs.getString(_kUsername);
    fullName = prefs.getString(_kFullName);
    final exp = prefs.getString(_kExpires);
    expiresAtUtc = exp == null ? null : DateTime.tryParse(exp)?.toUtc();
  }

  static Future<void> save({
    required String token,
    required String username,
    required String fullName,
    DateTime? expiresAtUtc,
  }) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_kToken, token);
    await prefs.setString(_kUsername, username);
    await prefs.setString(_kFullName, fullName);
    if (expiresAtUtc != null) {
      await prefs.setString(_kExpires, expiresAtUtc.toIso8601String());
    }
    Session.token = token;
    Session.username = username;
    Session.fullName = fullName;
    Session.expiresAtUtc = expiresAtUtc;
  }

  static Future<void> clear() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_kToken);
    await prefs.remove(_kUsername);
    await prefs.remove(_kFullName);
    await prefs.remove(_kExpires);
    token = username = fullName = null;
    expiresAtUtc = null;
  }
}
