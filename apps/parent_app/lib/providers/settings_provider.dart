import 'package:flutter/material.dart';

import '../core/api_client.dart';
import '../core/models.dart';

class SettingsProvider extends ChangeNotifier {
  final ApiClient api;
  SettingsProvider(this.api);

  ParentSettings? settings;
  bool loading = false;
  bool saving = false;
  String? error;
  String? lastSaved;

  Future<void> refresh() async {
    loading = true;
    error = null;
    notifyListeners();
    try {
      settings = await api.getSettings();
    } on ApiException catch (e) {
      error = e.message;
    } catch (e) {
      error = 'Load failed: $e';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  /// Returns null on success, else error text.
  Future<String?> save({String? phone, String? preferredLanguage, bool? receiveWhatsApp}) async {
    saving = true;
    lastSaved = null;
    error = null;
    notifyListeners();
    try {
      settings = await api.updateSettings(
        phone: phone,
        preferredLanguage: preferredLanguage,
        receiveWhatsApp: receiveWhatsApp,
      );
      lastSaved = DateTime.now().toIso8601String();
      return null;
    } on ApiException catch (e) {
      error = e.message;
      return e.message;
    } catch (e) {
      error = 'Save failed: $e';
      return error;
    } finally {
      saving = false;
      notifyListeners();
    }
  }
}
