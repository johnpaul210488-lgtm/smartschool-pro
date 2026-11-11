import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

import '../models/models.dart';

/// Persists the JWT session (survives app restarts).
class SessionService {
  static const _kLogin = 'ss_login';

  Future<void> save(LoginResult login) async {
    final sp = await SharedPreferences.getInstance();
    await sp.setString(_kLogin, jsonEncode(login.toJson()));
  }

  Future<LoginResult?> load() async {
    final sp = await SharedPreferences.getInstance();
    await sp.reload(); // <-- CHANGED
    final raw = sp.getString(_kLogin);
    if (raw == null) return null;
    try {
      return LoginResult.fromJson(jsonDecode(raw) as Map<String, dynamic>);
    } catch (_) {
      await clear();
      return null;
    }
  }

  Future<void> clear() async {
    final sp = await SharedPreferences.getInstance();
    await sp.remove(_kLogin);
    await sp.remove('ss_active_trip'); // <-- CHANGED
  }

  Future<void> saveActiveTrip(int? tripId) async { // <-- CHANGED
    final sp = await SharedPreferences.getInstance();
    if (tripId == null) {
      await sp.remove('ss_active_trip');
    } else {
      await sp.setInt('ss_active_trip', tripId);
    }
  }

  Future<int?> loadActiveTrip() async { // <-- CHANGED
    final sp = await SharedPreferences.getInstance();
    await sp.reload();
    return sp.getInt('ss_active_trip');
  }

  Future<bool> claimNotificationPrompt() async { // <-- CHANGED
    final sp = await SharedPreferences.getInstance();
    if (sp.getBool('ss_notification_prompted') == true) return false;
    await sp.setBool('ss_notification_prompted', true);
    return true;
  }
}
