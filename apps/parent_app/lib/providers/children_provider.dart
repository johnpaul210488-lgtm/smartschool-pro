import 'dart:async';

import 'package:flutter/material.dart';

import '../core/api_client.dart';
import '../core/models.dart';
import '../core/offline_db.dart';

/// Children cards + today's status + live bus positions (fed by SignalR).
class ChildrenProvider extends ChangeNotifier {
  final ApiClient api;

  ChildrenProvider(this.api);

  List<ChildCard> children = [];
  bool loading = false;
  String? error;

  /// busId → latest live location (from the hub's UpdateBusLocation events).
  final Map<int, BusLocation> liveBus = {};

  Future<void> refresh() async {
    loading = true;
    error = null;
    notifyListeners();
    try {
      children = await api.getChildren();
      // Cache for offline use.
      await OfflineDb.instance.cacheChildren(
        children.map((c) => {
          'id': c.studentId,
          'studentId': c.studentId,
          'fullName': c.fullName,
          'photoUrl': c.photoUrl,
          'className': c.className,
          'busId': c.busId,
          'busNumber': c.busNumber,
          'todayStatus': c.todayStatus,
          'lastEventText': c.lastEventText,
          'lastEventTimePakistan': c.lastEventTimePakistan,
        }).toList(),
      );
      // Pehle se chal rahi bus ki position bhi turant dikhayein (SignalR ka intezar na ho).
      try {
        for (final loc in await api.getLiveBuses()) {
          liveBus[loc.busId] = loc;
        }
      } catch (_) {
        // Live snapshot na mile to bhi cards dikhayein.
      }
    } on ApiException catch (e) {
      // Fall back to local cache so the home screen opens offline.
      error = e.message;
      final cached = await OfflineDb.instance.getCachedChildren();
      if (cached.isNotEmpty) {
        children = cached.map((m) => ChildCard.fromJson(_decodeMap(m))).toList();
        error = '${error}\\n(Server offline — purana data dikhaya ja raha hai)';
      } else {
        children = [];
      }
    } catch (e) {
      error = 'Load failed: $e';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Map<String, dynamic> _decodeMap(Map<String, dynamic> m) {
    return {
      'studentId': m['studentId'] as int,
      'fullName': m['fullName'] as String? ?? '',
      'photoUrl': m['photoUrl'] as String?,
      'className': m['className'] as String? ?? '',
      'busId': m['busId'] as int?,
      'busNumber': m['busNumber'] as String?,
      'todayStatus': m['todayStatus'] as String? ?? 'NotBoarded',
      'lastEventText': m['lastEventText'] as String?,
      'lastEventTimePakistan': m['lastEventTimePakistan'] as String?,
    };
  }

  /// Called by SignalRService when a pick/drop alert arrives for my child.
  void onBusEvent() => unawaited(refresh());

  void onBusLocation(BusLocation loc) {
    liveBus[loc.busId] = loc;
    notifyListeners();
  }

  BusLocation? locationFor(ChildCard child) =>
      child.busId == null ? null : liveBus[child.busId!];
}
