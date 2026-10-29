import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

import '../core/api_client.dart';
import '../core/fcm_service.dart';
import '../core/session.dart';

/// Login lifecycle + push/realtime services wiring.
class AuthProvider extends ChangeNotifier {
  final ApiClient api = ApiClient();

  bool busy = false;
  bool loggingOut = false;
  String? error;
  bool get isLoggedIn => Session.isLoggedIn;

  Future<void> restore() => Session.load();

  /// Returns null on success, else the error text.
  Future<String?> login(String username, String password) async {
    busy = true;
    error = null;
    notifyListeners();
    try {
      await api.login(username, password);
      // The root starts push registration after navigating; login must not hang
      // while Firebase or the device-registration endpoint is unavailable.
      return null;
    } on ApiException catch (e) {
      error = e.message;
      return e.message;
    } catch (e) {
      error = 'Login failed: $e';
      return error;
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  /// Post-login wiring: register FCM token (re-run after login screen).
  Future<void> linkDevice() => FcmService.instance.init(api);

  Future<void> logout() async {
    loggingOut = true;
    try {
      await FcmService.instance.unregister(api);
      await Session.clear();
      notifyListeners();
    } finally {
      loggingOut = false;
    }
  }
}
