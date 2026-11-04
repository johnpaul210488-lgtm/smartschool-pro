import 'dart:convert';

import 'package:flutter/material.dart';

import '../core/api_client.dart';
import '../core/models.dart';
import '../core/offline_db.dart';

/// Notifications tab = (a) pick/drop alert history of all children merged,
/// (b) school announcements. SignalR events just call bump() to refresh.
class NotificationsProvider extends ChangeNotifier {
  final ApiClient api;
  NotificationsProvider(this.api);

  List<AlertItem> alerts = [];
  List<AnnouncementItem> announcements = [];
  bool loading = false;
  String? error;
  bool _showingCache = false;

  /// Unread badge handled simply: count since last open (session-only).
  int unseenCount = 0;

  /// True when current data came from local cache (server offline).
  bool get showingCache => _showingCache;

  Future<void> refresh(List<int> childIds) async {
    loading = true;
    error = null;
    _showingCache = false;
    notifyListeners();
    try {
      final perChild = await Future.wait(
          childIds.map((id) => api.getStudentEvents(id).catchError((_) => <AlertItem>[])));
      alerts = perChild.expand((l) => l).toList()
        ..sort((a, b) => b.id.compareTo(a.id));

      announcements = await api.getAnnouncements().catchError((_) => <AnnouncementItem>[]);

      // Cache for offline use.
      await OfflineDb.instance.cacheAlerts(
          alerts.map((a) => a.toMap()).toList());
      await OfflineDb.instance.cacheAnnouncements(
          announcements.map((a) => a.toMap()).toList());
      _showingCache = false;
    } on ApiException catch (e) {
      error = e.message;
      // Fall back to local cache.
      final cachedAlerts = await OfflineDb.instance.getCachedAlerts();
      final cachedAnnouncements = await OfflineDb.instance.getCachedAnnouncements();
      if (cachedAlerts.isNotEmpty || cachedAnnouncements.isNotEmpty) {
        alerts = cachedAlerts.map((m) => AlertItem.fromMap(m)).toList()
          ..sort((a, b) => b.id.compareTo(a.id));
        announcements = cachedAnnouncements.map((m) => AnnouncementItem.fromMap(m)).toList()
          ..sort((a, b) => b.id.compareTo(a.id));
        _showingCache = true;
        error = '${error}\n(Server offline — purana data dikhaya ja raha hai)';
      } else {
        alerts = [];
        announcements = [];
        _showingCache = false;
      }
    } catch (e) {
      error = 'Load failed: $e';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  void bump() {
    unseenCount++;
    notifyListeners();
  }

  void markSeen() {
    unseenCount = 0;
    notifyListeners();
  }

  /// Cache a single incoming SignalR alert locally so it survives
  /// server restarts and is visible offline immediately.
  Future<void> cacheIncomingAlert(Map<String, dynamic> payload) async {
    try {
      await OfflineDb.instance.cacheAlerts([
        {
          'id': payload['id'] ?? 0,
          'studentId': payload['studentId'] ?? 0,
          'studentName': payload['studentName'] ?? '',
          'className': payload['className'] ?? '',
          'busNumber': payload['busNumber'] ?? '',
          'eventType': payload['eventType'] ?? payload['type'] ?? '',
          'timePakistan': payload['timePakistan'] ?? '',
          'location': payload['location'],
        },
      ]);
    } catch (_) {}
  }
}
