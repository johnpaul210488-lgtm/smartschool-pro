import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../core/strings.dart';

/// App-wide language (en ⇄ ur). Persisted locally; ALSO pushed to the API
/// (preferredLanguage) so server messages (WhatsApp/push) match the app.
class LocaleProvider extends ChangeNotifier {
  static const _kLang = 'app_lang';
  String _lang = 'en';

  String get lang => _lang;
  bool get isUrdu => _lang == 'ur';
  Locale get locale => Locale(_lang == 'ur' ? 'ur' : 'en');

  String t(String key) => S.tr(_lang, key);

  Future<void> load() async {
    final prefs = await SharedPreferences.getInstance();
    _lang = prefs.getString(_kLang) ?? 'en';
    notifyListeners();
  }

  Future<void> setLang(String lang) async {
    if (lang != 'en' && lang != 'ur') return;
    _lang = lang;
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_kLang, lang);
    notifyListeners();
  }
}
