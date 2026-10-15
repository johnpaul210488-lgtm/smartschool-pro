import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';

import 'api_client.dart';

/// Firebase Cloud Messaging wiring.
///
/// NOTE: push needs native config (google-services.json / GoogleService-Info.plist)
/// — see apps/parent_app/README.md "Firebase setup". Without it the app still
/// runs fully; init() just no-ops (try/catch) and SignalR keeps alerts live.
class FcmService {
  FcmService._();
  static final FcmService instance = FcmService._();

  bool _available = false;
  String? _token;

  /// Optional: screens can listen and show an in-app banner for foreground pushes.
  final ValueNotifier<RemoteMessage?> lastMessage = ValueNotifier(null);

  @pragma('vm:entry-point')
  static Future<void> _backgroundHandler(RemoteMessage message) async {
    // Nothing to render in background — Android/iOS shows the system
    // notification automatically because the server sets "notification".
  }

  Future<void> init(ApiClient api) async {
    try {
      await Firebase.initializeApp();
      final messaging = FirebaseMessaging.instance;

      await messaging.requestPermission();
      FirebaseMessaging.onBackgroundMessage(_backgroundHandler);

      _token = await messaging.getToken();
      if (_token != null) await _safeRegister(api, _token!);

      messaging.onTokenRefresh.listen((t) {
        _token = t;
        _safeRegister(api, t);
      });

      FirebaseMessaging.onMessage.listen((message) => lastMessage.value = message);

      _available = true;
      debugPrint('[FCM] ready, token registered');
    } catch (e) {
      // No firebase config in this build — app stays fully functional.
      _available = false;
      debugPrint('[FCM] disabled ($e)');
    }
  }

  Future<void> _safeRegister(ApiClient api, String token) async {
    try {
      final platform = defaultTargetPlatform == TargetPlatform.iOS ? 'ios' : 'android';
      await api.registerDevice(token, platform);
    } catch (e) {
      debugPrint('[FCM] register failed: $e');
    }
  }

  Future<void> unregister(ApiClient api) async {
    if (!_available || _token == null) return;
    try {
      await api.unregisterDevice(_token!);
    } catch (_) {/* server may be offline at logout; fine */}
  }
}
