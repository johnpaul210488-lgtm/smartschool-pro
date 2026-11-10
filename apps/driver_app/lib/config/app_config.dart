import 'package:shared_preferences/shared_preferences.dart';

/// App-wide config. The school server is BAKED INTO THE RELEASE APK — it is
/// the Tailscale MagicDNS name, which works from any network (Wi-Fi / 4G) and
/// survives PC restarts, ISP changes and phone reboots. Phones no longer need
/// (or see) any server setting. Stale per-phone overrides saved by older
/// builds are ignored and cleaned up at boot.
///
/// (Can still be overridden at build time with --dart-define=API_BASE=...)
class AppConfig {
  static const String _defaultBase = String.fromEnvironment(
    'API_BASE',
    defaultValue: 'http://zia.tail758a5c.ts.net:5000',
  );

  static const _kBaseOverride = 'api_base_override';

  /// Effective API base (override, else baked-in default). Mutated once at boot.
  static String apiBaseUrl = _defaultBase;

  /// Older builds let each phone override the server. Those stale overrides
  /// are IGNORED now — the baked-in Tailscale address always wins — and the
  /// stale key is cleaned up so phones self-heal on this update.
  static Future<void> loadServerOverride() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.remove(_kBaseOverride);
    } catch (_) {}
  }

  /// No longer used — the server address is baked into the release build.
  /// Kept as a no-op so older code paths still compile.
  static Future<void> saveServerOverride(String baseUrl) async {}

  /// GPS uplink interval while a trip is active.
  static const Duration gpsPushInterval = Duration(seconds: 15);

  /// How often the offline outbox retries when the network is back.
  static const Duration syncInterval = Duration(seconds: 45);

  /// Persistent notification (background trip tracking).
  static const int foregroundNotificationId = 888;
  static const String notificationChannelId = 'smartschool_trip_channel';
  static const String notificationTitle = 'SmartSchool: Trip in progress';
  static const String notificationBody = 'Tracking active — parents & admin live dekh rahe hain';
}
