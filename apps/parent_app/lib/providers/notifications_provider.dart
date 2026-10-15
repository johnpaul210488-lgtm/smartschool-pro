import 'package:flutter/material.dart';

import '../core/api_client.dart';
import '../core/models.dart';

/// Notifications tab = (a) pick/drop alert history of all children merged,
/// (b) school announcements. SignalR events just call bump() to refresh.
class NotificationsProvider extends ChangeNotifier {
  final ApiClient api;
  NotificationsProvider(this.api);

  List<AlertItem> alerts = [];
  List<AnnouncementItem> announcements = [];
  bool loading = false;
  String? error;

  /// Unread badge handled simply: count since last open (session-only).
  int unseenCount = 0;

  Future<void> refresh(List<int> childIds) async {
    loading = true;
    error = null;
    notifyListeners();
    try {
      final perChild = await Future.wait(
          childIds.map((id) => api.getStudentEvents(id).catchError((_) => <AlertItem>[])));
      alerts = perChild.expand((l) => l).toList()
        ..sort((a, b) => b.id.compareTo(a.id));

      announcements = await api.getAnnouncements().catchError((_) => <AnnouncementItem>[]);
    } on ApiException catch (e) {
      error = e.message;
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
}
