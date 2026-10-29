import 'package:shared_preferences/shared_preferences.dart';

/// App-wide config. The API base can be baked in at build time
/// (--dart-define=API_BASE=...) AND overridden on the login screen — the
/// override persists, so update-APKs (built with a generic default) never
/// break an already-configured phone.
///
/// - Same PC emulator:        http://10.0.2.2:5000   (Android emulator → host PC)
/// - Physical phone (Wi-Fi):  http://192.168.1.10:5000  (server's LAN IP)
/// - Production:              https://api.yourschool.pk
class AppConfig {
  static const String _defaultBase = String.fromEnvironment(
    'API_BASE',
    defaultValue: 'https://zia.tail758a5c.ts.net',
  );

  static const _kBaseOverride = 'api_base_override';

  /// Effective API base (override, else baked-in default). Mutated once at boot.
  static String apiBaseUrl = _defaultBase;

  /// Load any saved override — call before the first network call (app boot).
  static Future<void> loadServerOverride() async {
    final prefs = await SharedPreferences.getInstance();
    final saved = prefs.getString(_kBaseOverride);
    if (saved != null && saved.trim().isNotEmpty) {
      // Purana LAN/emulator default save ho to hata dein, taake naya public address use ho.
      if (saved.trim() == 'http://192.168.1.10:5000' || saved.trim() == 'http://10.0.2.2:5000') {
        await prefs.remove(_kBaseOverride);
      } else {
        apiBaseUrl = saved.trim();
      }
    }
  }

  /// Called from the login screen when the driver edits the server address.
  static Future<void> saveServerOverride(String baseUrl) async {
    final prefs = await SharedPreferences.getInstance();
    final value = baseUrl.trim();
    if (value.isEmpty || value == _defaultBase) {
      await prefs.remove(_kBaseOverride);
      apiBaseUrl = _defaultBase;
    } else {
      await prefs.setString(_kBaseOverride, value);
      apiBaseUrl = value;
    }
  }

  /// GPS uplink interval while a trip is active.
  static const Duration gpsPushInterval = Duration(seconds: 1);

  /// How often the offline outbox retries when the network is back.
  static const Duration syncInterval = Duration(seconds: 45);

  /// Persistent notification (background trip tracking).
  static const int foregroundNotificationId = 888;
  static const String notificationChannelId = 'smartschool_trip_channel';
  static const String notificationTitle = 'SmartSchool: Trip in progress';
  static const String notificationBody = 'Tracking active — parents & admin live dekh rahe hain';
}
