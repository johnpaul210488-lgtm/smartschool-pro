import 'package:shared_preferences/shared_preferences.dart';

/// Build-time config. The school server is BAKED INTO THE RELEASE APK — it is
/// the Tailscale Funnel public HTTPS URL (port 443, Let's Encrypt), which
/// works from any network (Wi-Fi / 4G) and survives PC restarts, ISP changes
/// and phone reboots — no Tailscale app is needed on the phones. Phones no longer need
/// (or see) any server setting. Stale per-phone overrides saved by older
/// builds are ignored and cleaned up at boot.
///
/// (Can still be overridden at build time with --dart-define=API_BASE=...)
class AppConfig {
  static const String _defaultBase = String.fromEnvironment(
    'API_BASE',
    defaultValue: 'https://zia.tail758a5c.ts.net',
  );

  static const _kBaseOverride = 'api_base_override';

  /// Effective API base (override, else baked-in default).
  static String apiBase = _defaultBase;

  static String get hubUrl => '$apiBase/hubs/notifications';

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
}
