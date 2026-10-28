import 'dart:async';
import 'dart:ui';

import 'package:flutter/widgets.dart';
import 'package:flutter_background_service/flutter_background_service.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:geolocator/geolocator.dart';
import 'package:http/http.dart' as http;
import 'package:intl/intl.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'dart:convert';

import '../config/app_config.dart';
import '../services/session_service.dart';

/// Foreground service that pushes the bus GPS every
/// [AppConfig.gpsPushInterval] while a trip is active.
///
/// Protocol with the main isolate:
/// - main → service:  'start' {tripId} | 'stop'
/// - service → main:  (none — fire & forget into the API)
class BackgroundGpsService {
  BackgroundGpsService._();
  static final BackgroundGpsService instance = BackgroundGpsService._();

  bool _initialized = false;

  Future<void> initialize() async {
    if (_initialized) return;
    _initialized = true;

    // Android 8+ needs an explicit notification channel for the foreground service.
    final plugin = FlutterLocalNotificationsPlugin();
    await plugin
        .resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>()
        ?.createNotificationChannel(const AndroidNotificationChannel(
          AppConfig.notificationChannelId,
          'Trip Tracking',
          description: 'Shows while a trip is active with live GPS.',
          importance: Importance.low,
        ));

    await FlutterBackgroundService().configure(
      androidConfiguration: AndroidConfiguration(
        onStart: _onStart,
        autoStart: false,
        isForegroundMode: true,
        notificationChannelId: AppConfig.notificationChannelId,
        initialNotificationTitle: AppConfig.notificationTitle,
        initialNotificationContent: AppConfig.notificationBody,
        foregroundServiceNotificationId: AppConfig.foregroundNotificationId,
      ),
      iosConfiguration: IosConfiguration(
        autoStart: false,
        onForeground: _onStart,
        onBackground: _onIosBackground,
      ),
    );
  }

  Future<void> startTrip(int tripId) async {
    await initialize();
    final service = FlutterBackgroundService();
    if (!(await service.isRunning())) await service.startService();
    // Service ka isolate turant tayyar nahi hota: 'start' event kho sakta hai.
    // Handler idempotent hai (pehle timer cancel karta hai), is liye kuch baar bhejna safe hai.
    service.invoke('start', {'tripId': tripId});
    // Dobara bhejna: pehle wali 'start' kho gayi ho to bhi GPS loop chal jaye.
    unawaited(_resendStart(service, tripId));
  }

  static int _startToken = 0;

  static Future<void> _resendStart(FlutterBackgroundService service, int tripId) async {
    final myToken = ++_startToken;
    for (final wait in const [2, 3, 5]) {
      await Future.delayed(Duration(seconds: wait));
      // Trip band ho gayi ya naya start hua to purani resend rok do.
      if (myToken != _startToken) return;
      service.invoke('start', {'tripId': tripId});
    }
  }

  Future<void> stopTrip() async {
    _startToken++;
    FlutterBackgroundService().invoke('stop');
  }

  bool _onIosBackground(ServiceInstance service) {
    WidgetsFlutterBinding.ensureInitialized();
    return true;
  }

  @pragma('vm:entry-point')
  static void _onStart(ServiceInstance service) async {
    DartPluginRegistrant.ensureInitialized();

    Timer? timer;
    int? activeTripId;

    Future<void> writeStatus(String text) async {
      try {
        final prefs = await SharedPreferences.getInstance();
        await prefs.setString(
            'gps_status', '$text ${DateFormat('HH:mm:ss').format(DateTime.now())}');
      } catch (_) {}
    }

    bool loadedOverride = false;

    Future<void> pushGps() async {
      if (activeTripId == null) return;
      try {
        // Background isolate ko bhi saved server address chahiye.
        if (!loadedOverride) {
          await AppConfig.loadServerOverride();
          loadedOverride = true;
        }
        final pos = await Geolocator.getCurrentPosition(
          locationSettings: const LocationSettings(accuracy: LocationAccuracy.high),
        );
        final login = await SessionService().load();
        if (login == null) {
          await writeStatus('ERR: login session nahi mila');
          return;
        }

        final res = await http
            .post(
              Uri.parse('${AppConfig.apiBaseUrl}/api/trips/$activeTripId/location'),
              headers: {
                'Content-Type': 'application/json',
                'Authorization': 'Bearer ${login.token}',
              },
              body: jsonEncode({
                'latitude': pos.latitude,
                'longitude': pos.longitude,
                'speedKmh': pos.speed < 0 ? 0 : pos.speed * 3.6,
              }),
            )
            .timeout(const Duration(seconds: 8));
        if (res.statusCode >= 200 && res.statusCode < 300) {
          await writeStatus('OK: server ko bheja');
        } else {
          await writeStatus('ERR: server ${res.statusCode}');
        }
      } catch (e) {
        // Background GPS push must be silent; error sirf status line mein dikhta hai.
        await writeStatus('ERR: ${e.toString().split('\n').first}');
      }
    }

    // Tracing: service isolate tayyar hone par pehla status (agar ye na dikhe to isolate hi nahi chala).
    await writeStatus('Service chal gayi, trip signal ka intezar');

    service.on('start').listen((data) {
      activeTripId = (data?['tripId'] as num?)?.toInt();
      writeStatus('Trip #$activeTripId signal mila, GPS loop shuru');
      timer?.cancel();
      pushGps(); // immediate first ping
      timer = Timer.periodic(AppConfig.gpsPushInterval, (_) => pushGps());
    });

    service.on('stop').listen((_) {
      timer?.cancel();
      activeTripId = null;
      service.stopSelf();
    });
  }
}
