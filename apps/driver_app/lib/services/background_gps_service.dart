import 'dart:async';
import 'dart:convert';
import 'dart:ui';

import 'package:flutter/widgets.dart';
import 'package:flutter_background_service/flutter_background_service.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:geolocator/geolocator.dart';
import 'package:http/http.dart' as http;
import 'package:intl/intl.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../config/app_config.dart';
import '../services/session_service.dart';

/// Android foreground service that keeps a one-second live-location heartbeat
/// running while a trip is active, including when the Flutter UI is minimized.
///
/// The active trip id is persisted before the service starts. If Android
/// recreates the foreground-service isolate, it resumes the same trip without
/// waiting for the minimized app to send another message.
class BackgroundGpsService {
  BackgroundGpsService._();
  static final BackgroundGpsService instance = BackgroundGpsService._();

  static const _activeTripKey = 'background_gps_active_trip_id';
  static const _gpsStatusKey = 'gps_status';
  static final SharedPreferencesAsync _preferences = SharedPreferencesAsync();

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
        // If the phone/service process is recreated, _onStart reads the durable
        // trip id. With no active trip it immediately stops itself.
        autoStartOnBoot: true,
        isForegroundMode: true,
        foregroundServiceTypes: const [AndroidForegroundType.location],
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

  Future<bool> isRunning() => FlutterBackgroundService().isRunning();

  Future<void> startTrip(int tripId) async {
    await initialize();
    // Persist first: the service can recover even if its start event races with
    // isolate startup or Android recreates it after the app is minimized.
    await _preferences.setInt(_activeTripKey, tripId);
    final service = FlutterBackgroundService();
    if (!(await service.isRunning())) await service.startService();
    service.invoke('start', {'tripId': tripId});
    // The handler is idempotent; repeat the signal while the isolate comes up.
    unawaited(_resendStart(service, tripId));
    unawaited(_verifyRunning(service));
  }

  static Future<void> _verifyRunning(FlutterBackgroundService service) async {
    await Future.delayed(const Duration(seconds: 8));
    final running = await service.isRunning();
    if (!running && await _preferences.getInt(_activeTripKey) != null) {
      await writeGpsStatus(
          'ERR: background service start nahi hui. Battery settings mein app ko Unrestricted karein.');
    }
  }

  /// Status line ke liye (main isolate aur service dono use kar sakte hain).
  static Future<void> writeGpsStatus(String text) async {
    try {
      await _preferences.setString(
          _gpsStatusKey, '$text ${DateFormat('HH:mm:ss').format(DateTime.now())}');
    } catch (_) {}
  }

  static int _startToken = 0;

  static Future<void> _resendStart(FlutterBackgroundService service, int tripId) async {
    final myToken = ++_startToken;
    for (final wait in const [2, 3, 5]) {
      await Future.delayed(Duration(seconds: wait));
      if (myToken != _startToken || await _preferences.getInt(_activeTripKey) != tripId) return;
      service.invoke('start', {'tripId': tripId});
    }
  }

  Future<void> stopTrip() async {
    _startToken++;
    // Clear first so a simultaneous Android restart cannot revive an ended trip.
    await _preferences.remove(_activeTripKey);
    FlutterBackgroundService().invoke('stop');
  }

  bool _onIosBackground(ServiceInstance service) {
    WidgetsFlutterBinding.ensureInitialized();
    return true;
  }

  @pragma('vm:entry-point')
  static void _onStart(ServiceInstance service) async {
    DartPluginRegistrant.ensureInitialized();

    Timer? heartbeat;
    StreamSubscription<Position>? positionSubscription;
    Position? latestPosition;
    int? activeTripId;
    bool loadedOverride = false;
    bool pushing = false;
    var transition = Future<void>.value();

    Future<void> writeStatus(String text) => writeGpsStatus(text);

    Future<void> stopTracking({required bool stopService}) async {
      heartbeat?.cancel();
      heartbeat = null;
      await positionSubscription?.cancel();
      positionSubscription = null;
      latestPosition = null;
      activeTripId = null;
      await _preferences.remove(_activeTripKey);
      if (stopService) service.stopSelf();
    }

    Future<void> pushGps([Position? fixed]) async {
      final tripId = activeTripId;
      if (tripId == null || pushing) return;
      pushing = true;
      try {
        if (!loadedOverride) {
          await AppConfig.loadServerOverride();
          loadedOverride = true;
        }
        final pos = fixed ?? latestPosition ??
            await Geolocator.getCurrentPosition(
              locationSettings: const LocationSettings(
                accuracy: LocationAccuracy.high,
                timeLimit: Duration(seconds: 8),
              ),
            );
        latestPosition = pos;
        final login = await SessionService().load();
        if (login == null) {
          await writeStatus('ERR: login session nahi mila');
          return;
        }

        final res = await http
            .post(
              Uri.parse('${AppConfig.apiBaseUrl}/api/trips/$tripId/location'),
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
          await writeStatus('OK: background se server ko bheja');
        } else if (res.statusCode == 404 || res.statusCode == 409) {
          // The server says this trip no longer exists/is active. Do not leave a
          // silent foreground tracker running forever after an office-side end.
          await writeStatus('Trip server par band hai; tracking stop');
          await stopTracking(stopService: true);
        } else {
          await writeStatus('ERR: server ${res.statusCode}');
        }
      } catch (e) {
        // Keep the service alive through temporary GPS/network failures.
        await writeStatus('ERR: ${e.toString().split('\n').first}');
      } finally {
        pushing = false;
      }
    }

    Future<void> beginTracking(int tripId) async {
      if (tripId <= 0) return;
      if (activeTripId == tripId && heartbeat?.isActive == true && positionSubscription != null) {
        await _preferences.setInt(_activeTripKey, tripId);
        return;
      }
      heartbeat?.cancel();
      await positionSubscription?.cancel();
      activeTripId = tripId;
      latestPosition = null;
      await _preferences.setInt(_activeTripKey, tripId);
      await writeStatus('Trip #$tripId active, 1-second background tracking ON');

      // The stream keeps the latest high-accuracy fix warm. A separate one-second
      // heartbeat sends that latest fix even while the vehicle is stationary.
      positionSubscription = Geolocator.getPositionStream(
        locationSettings: AndroidSettings(
          accuracy: LocationAccuracy.high,
          distanceFilter: 0,
          intervalDuration: AppConfig.gpsPushInterval,
        ),
      ).listen(
        (position) => latestPosition = position,
        onError: (Object e) => unawaited(
            writeStatus('ERR: GPS stream: ${e.toString().split('\n').first}')),
      );

      unawaited(pushGps()); // immediate first fix/uplink
      heartbeat = Timer.periodic(AppConfig.gpsPushInterval, (_) {
        unawaited(pushGps(latestPosition));
      });
    }

    void queueStart(int tripId) {
      transition = transition.then((_) => beginTracking(tripId)).catchError((Object e) async {
        await writeStatus('ERR: tracking start: ${e.toString().split('\n').first}');
      });
    }

    service.on('start').listen((data) {
      final tripId = (data?['tripId'] as num?)?.toInt();
      if (tripId != null) queueStart(tripId);
    });

    service.on('stop').listen((_) {
      transition = transition.then((_) => stopTracking(stopService: true));
    });

    final savedTripId = await _preferences.getInt(_activeTripKey);
    if (savedTripId == null) {
      await writeStatus('Koi active trip nahi; background service stop');
      service.stopSelf();
      return;
    }
    queueStart(savedTripId);
  }
}
