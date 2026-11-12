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

    // Android 8+ needs an explicit notification channel for the foreground service.
    final plugin = FlutterLocalNotificationsPlugin();
    final androidPlugin =
        plugin.resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>();
    // Android 13+: foreground service ki persistent notification ke liye
    // POST_NOTIFICATIONS runtime permission zaroori hai — manifest mein hone
    // se kaam nahi chalta. Deny hone par bhi trip fail nahi karni chahiye,
    // isliye intentionally ignore the result.
    try {
      if (androidPlugin != null && await SessionService().claimNotificationPrompt()) { // <-- CHANGED
        await androidPlugin.requestNotificationsPermission();
      }
    } catch (_) {}
    await androidPlugin?.createNotificationChannel(const AndroidNotificationChannel(
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
    _initialized = true; // <-- CHANGED
  }

  Future<void> startTrip(int tripId) async {
    await initialize();
    await SessionService().saveActiveTrip(tripId); // <-- CHANGED
    final service = FlutterBackgroundService();
    final ready = service.on('ready').first; // <-- CHANGED
    if (!(await service.isRunning()) && !await service.startService()) { // <-- CHANGED
      throw StateError('Background GPS service start nahi ho saki.');
    }
    final retry = Timer.periodic(const Duration(seconds: 1), (_) => service.invoke('ping')); // <-- CHANGED
    try {
      service.invoke('ping');
      await ready.timeout(const Duration(seconds: 30));
    } finally {
      retry.cancel();
    }
    final started = service.on('started').firstWhere( // <-- CHANGED
      (data) => (data?['tripId'] as num?)?.toInt() == tripId,
    );
    final startRetry = Timer.periodic(const Duration(seconds: 1), (_) { // <-- CHANGED
      service.invoke('start', {'tripId': tripId});
    });
    try {
      service.invoke('start', {'tripId': tripId});
      await started.timeout(const Duration(seconds: 30));
    } finally {
      startRetry.cancel();
    }
  }

  Future<void> stopTrip() async {
    await SessionService().saveActiveTrip(null); // <-- CHANGED
    final service = FlutterBackgroundService(); // <-- CHANGED
    if (!(await service.isRunning())) return;
    final ready = service.on('ready').first;
    final retry = Timer.periodic(const Duration(seconds: 1), (_) => service.invoke('ping'));
    try {
      service.invoke('ping');
      await ready.timeout(const Duration(seconds: 30));
    } finally {
      retry.cancel();
    }
    service.invoke('stop');
  }

  bool _onIosBackground(ServiceInstance service) {
    WidgetsFlutterBinding.ensureInitialized();
    return true;
  }

  @pragma('vm:entry-point')
  static void _onStart(ServiceInstance service) async {
    debugPrint('GPS_DIAG: background callback entered'); // <-- CHANGED
    DartPluginRegistrant.ensureInitialized();
    debugPrint('GPS_DIAG: plugins initialized'); // <-- CHANGED

    Timer? timer;
    int? activeTripId; // <-- CHANGED
    bool commandReceived = false; // <-- CHANGED
    bool sending = false; // <-- CHANGED

    Future<void> pushGps() async {
      final tripId = activeTripId; // <-- CHANGED
      if (tripId == null || sending) return; // <-- CHANGED
      sending = true; // <-- CHANGED
      try {
        final pos = await Geolocator.getCurrentPosition(
          locationSettings: const LocationSettings(accuracy: LocationAccuracy.high),
        ).timeout(const Duration(seconds: 12)); // <-- CHANGED
        final login = await SessionService().load();
        if (login == null || activeTripId != tripId) return; // <-- CHANGED

        await http
            .post(
              Uri.parse('${AppConfig.apiBaseUrl}/api/trips/$tripId/location'), // <-- CHANGED
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
      } finally {
        sending = false; // <-- CHANGED
      }
    }

    service.on('start').listen((data) {
      commandReceived = true; // <-- CHANGED
      final tripId = (data?['tripId'] as num?)?.toInt(); // <-- CHANGED
      if (tripId != activeTripId || timer == null) { // <-- CHANGED
        activeTripId = tripId;
        timer?.cancel();
        pushGps(); // immediate first ping
        timer = Timer.periodic(AppConfig.gpsPushInterval, (_) => pushGps());
      }
      service.invoke('started', {'tripId': activeTripId}); // <-- CHANGED
    });

    service.on('stop').listen((_) {
      commandReceived = true; // <-- CHANGED
      timer?.cancel();
      activeTripId = null;
      service.stopSelf();
    });
    service.on('ping').listen((_) { // <-- CHANGED
      debugPrint('GPS_DIAG: ping received');
      service.invoke('ready');
    });
    debugPrint('GPS_DIAG: listeners registered'); // <-- CHANGED
    try { // <-- CHANGED
      final restoredTripId = await SessionService().loadActiveTrip();
      if (!commandReceived && restoredTripId != null) {
        activeTripId = restoredTripId;
        pushGps();
        timer = Timer.periodic(AppConfig.gpsPushInterval, (_) => pushGps());
      }
    } catch (_) {
      // The main isolate can still send the active trip even if restore fails.
    }
  }
}
