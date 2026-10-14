/// App-wide config. CHANGE the BASE URL to point at the school server running
/// SmartSchool.API.
///
/// - Same PC emulator:        http://10.0.2.2:5000   (Android emulator → host PC)
/// - Physical phone (Wi-Fi):  http://192.168.1.10:5000  (server's LAN IP)
/// - Production:              https://api.yourschool.pk
class AppConfig {
  static const String apiBaseUrl = 'http://10.0.2.2:5000';

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
