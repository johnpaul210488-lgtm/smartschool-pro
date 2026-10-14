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
  }
}
