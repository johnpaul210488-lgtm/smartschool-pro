import 'dart:async';

import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart'
    show FlutterLocalNotificationsPlugin, AndroidFlutterLocalNotificationsPlugin, AndroidNotificationChannel, Importance;

import 'api_client.dart';
import 'session.dart';

/// Registration status only: even a registered device still needs the API's
/// Firebase credentials, an internet connection and OS notification permission.
enum PushStatus { checking, registered, permissionDenied, registrationFailed, unavailable }

@pragma('vm:entry-point')
Future<void> _firebaseMessagingBackgroundHandler(RemoteMessage message) async {
  // Required when Android starts a fresh Dart isolate for a terminated app.
  // The notification payload itself is rendered by FCM; initialization keeps
  // data/background handling valid across FlutterFire versions.
  if (Firebase.apps.isEmpty) await Firebase.initializeApp();
}

/// Platform seam so the registration lifecycle can be tested without Firebase.
abstract class FcmPlatform {
  Future<void> setup();
  Future<AuthorizationStatus> requestPermission();
  Future<AuthorizationStatus> currentPermission();
  Future<String?> getToken();
  Stream<String> get tokenRefresh;
  Stream<RemoteMessage> get foregroundMessages;
}

class _FirebaseFcmPlatform implements FcmPlatform {
  @override
  Future<void> setup() async {
    if (Firebase.apps.isEmpty) await Firebase.initializeApp();
    // The server targets this Android 8+ notification channel.
    try {
      await FlutterLocalNotificationsPlugin()
          .resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>()
          ?.createNotificationChannel(const AndroidNotificationChannel(
            'smartschool_alerts',
            'School alerts',
            description: 'Bus pick-up, drop-off and school announcements.',
            importance: Importance.high,
          ));
    } catch (e) {
      // Channel failure should not prevent token registration or SignalR.
      debugPrint('[FCM] notification channel setup failed: $e');
    }
    FirebaseMessaging.onBackgroundMessage(_firebaseMessagingBackgroundHandler);
  }

  @override
  Future<AuthorizationStatus> requestPermission() async =>
      (await FirebaseMessaging.instance.requestPermission()).authorizationStatus;
  @override
  Future<AuthorizationStatus> currentPermission() async =>
      (await FirebaseMessaging.instance.getNotificationSettings()).authorizationStatus;
  @override
  Future<String?> getToken() => FirebaseMessaging.instance.getToken();
  @override
  Stream<String> get tokenRefresh => FirebaseMessaging.instance.onTokenRefresh;
  @override
  Stream<RemoteMessage> get foregroundMessages => FirebaseMessaging.onMessage;
}

/// Firebase Cloud Messaging wiring. Native Firebase configuration is required;
/// see apps/parent_app/README.md. SignalR remains available without it.
class FcmService {
  FcmService._(this._platform);
  @visibleForTesting
  FcmService.forTesting(FcmPlatform platform) : this._(platform);
  static final FcmService instance = FcmService._(_FirebaseFcmPlatform());

  final FcmPlatform _platform;

  bool _setupComplete = false;
  bool _sessionActive = false;
  bool _permissionDenied = false;
  String? _token;
  final Set<String> _registeredTokens = <String>{};
  Future<void>? _initializing;
  Future<void> _registrations = Future<void>.value();

  final ValueNotifier<PushStatus> status = ValueNotifier(PushStatus.checking);
  final ValueNotifier<RemoteMessage?> lastMessage = ValueNotifier(null);

  /// Safe to call after login, on restore, and when the app resumes. Concurrent
  /// calls share one setup; listeners are installed once and registration is
  /// retried even when an earlier network request failed.
  Future<void> init(ApiClient api) {
    _sessionActive = true;
    final pending = _initializing;
    if (pending != null) return pending;
    final future = _initialize(api);
    _initializing = future;
    return future.whenComplete(() => _initializing = null);
  }

  Future<void> _initialize(ApiClient api) async {
    if (_sessionActive) status.value = PushStatus.checking;
    try {
      if (!_setupComplete) {
        await _platform.setup();
        final permission = await _platform.requestPermission();
        _permissionDenied = permission == AuthorizationStatus.denied ||
            permission == AuthorizationStatus.notDetermined;
        _platform.tokenRefresh.listen((token) {
          if (_sessionActive) unawaited(_updateToken(api, token));
        });
        _platform.foregroundMessages.listen((message) {
          if (_sessionActive) lastMessage.value = message;
        });
        _setupComplete = true;
      } else {
        final permission = await _platform.currentPermission();
        _permissionDenied = permission == AuthorizationStatus.denied ||
            permission == AuthorizationStatus.notDetermined;
      }

      final token = await _platform.getToken();
      if (!_sessionActive) return;
      if (token == null || token.isEmpty) {
        status.value = _permissionDenied ? PushStatus.permissionDenied : PushStatus.registrationFailed;
        debugPrint('[FCM] device token unavailable; will retry on resume');
        return;
      }
      await _updateToken(api, token);
    } catch (e) {
      if (_sessionActive) {
        status.value = _setupComplete && _permissionDenied
            ? PushStatus.permissionDenied
            : PushStatus.unavailable;
      }
      // Missing Firebase config or network errors must not block parent login.
      debugPrint('[FCM] setup failed: $e');
    }
  }

  Future<void> _updateToken(ApiClient api, String token) {
    final previous = _token;
    _token = token;
    // Serialize refresh/login/logout so a late refresh cannot re-register a
    // device for a user who has just logged out.
    _registrations = _registrations.then((_) async {
      if (!_sessionActive || !Session.isLoggedIn) return;
      if (previous != null && previous != token) {
        try {
          await api.unregisterDevice(previous);
          _registeredTokens.remove(previous);
        } catch (e) {
          _registeredTokens.add(previous); // Retry cleanup on logout.
          debugPrint('[FCM] old token cleanup failed: $e');
        }
      }
      if (!_sessionActive || _token != token) return;
      try {
        final platform = defaultTargetPlatform == TargetPlatform.iOS ? 'ios' : 'android';
        await api.registerDevice(token, platform);
        // Keep track even if logout started while the request was in flight.
        _registeredTokens.add(token);
        if (_sessionActive) {
          status.value = _permissionDenied ? PushStatus.permissionDenied : PushStatus.registered;
          debugPrint('[FCM] device registered with API');
        }
      } catch (e) {
        if (_sessionActive) status.value = PushStatus.registrationFailed;
        debugPrint('[FCM] device registration failed: $e');
      }
    });
    return _registrations;
  }

  Future<void> unregister(ApiClient api) async {
    _sessionActive = false;
    final pending = _initializing;
    if (pending != null) await pending;
    await _registrations;
    final tokens = <String>{..._registeredTokens, if (_token != null) _token!};
    for (final token in tokens) {
      try {
        await api.unregisterDevice(token);
      } catch (e) {
        debugPrint('[FCM] device unregister failed: $e');
      }
    }
    _registeredTokens.clear();
    lastMessage.value = null;
    status.value = PushStatus.checking;
  }
}
