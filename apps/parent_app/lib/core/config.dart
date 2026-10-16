import 'package:shared_preferences/shared_preferences.dart';

/// Build-time config. Set the API address for YOUR network at build/run time:
///   flutter run --dart-define=API_BASE=http://192.168.1.10:5000
/// (use the LAN IP of the PC running SmartSchool.API for real phones).
/// The Settings screen can also change it — that override persists and
/// update-APKs (built with a generic default) never break a configured phone.
class AppConfig {
  static const String _defaultBase = String.fromEnvironment(
    'API_BASE',
    defaultValue: 'http://192.168.1.10:5000',
  );

  static const _kBaseOverride = 'api_base_override';

  /// Effective API base (override, else baked-in default).
  static String apiBase = _defaultBase;

  static String get hubUrl => '$apiBase/hubs/notifications';

  /// Load any saved override — call before the first network call (app boot).
  static Future<void> loadServerOverride() async {
    final prefs = await SharedPreferences.getInstance();
    final saved = prefs.getString(_kBaseOverride);
    if (saved != null && saved.trim().isNotEmpty) apiBase = saved.trim();
  }

  /// Change the server from the Settings screen; persists across update installs.
  static Future<void> saveServerOverride(String baseUrl) async {
    final prefs = await SharedPreferences.getInstance();
    final value = baseUrl.trim();
    if (value.isEmpty || value == _defaultBase) {
      await prefs.remove(_kBaseOverride);
      apiBase = _defaultBase;
    } else {
      await prefs.setString(_kBaseOverride, value);
      apiBase = value;
    }
  }
}
