import 'dart:async';
import 'dart:ui';

import 'package:flutter/widgets.dart';
import 'package:flutter_background_service/flutter_background_service.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:geolocator/geolocator.dart';
import 'package:http/http.dart' as http;
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
    service.invoke('start', {'tripId': tripId});
  }

  Future<void> stopTrip() async {
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

    Future<void> pushGps() async {
      if (activeTripId == null) return;
      try {
        final pos = await Geolocator.getCurrentPosition(
          locationSettings: const LocationSettings(accuracy: LocationAccuracy.high),
        );
        final login = await SessionService().load();
        if (login == null) return;

        await http
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
      } catch (_) {
        // Background GPS push must be silent — offline = skip this tick;
        // admin map just shows the last known fix.
      }
    }

    service.on('start').listen((data) {
      activeTripId = (data?['tripId'] as num?)?.toInt();
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
