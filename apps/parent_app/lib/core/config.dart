/// Build-time config. Set the API address for YOUR network at build/run time:
///   flutter run --dart-define=API_BASE=http://192.168.1.10:5000
/// (use the LAN IP of the PC running SmartSchool.API for real phones).
class AppConfig {
  static const String apiBase = String.fromEnvironment(
    'API_BASE',
    defaultValue: 'http://192.168.1.10:5000',
  );

  static String get hubUrl => '$apiBase/hubs/notifications';
}
