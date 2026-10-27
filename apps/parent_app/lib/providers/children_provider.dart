import 'dart:async';

import 'package:flutter/material.dart';

import '../core/api_client.dart';
import '../core/models.dart';

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
      // Pehle se chal rahi bus ki position bhi turant dikhayein (SignalR ka intezar na ho).
      try {
        for (final loc in await api.getLiveBuses()) {
          liveBus[loc.busId] = loc;
        }
      } catch (_) {
        // Live snapshot na mile to bhi cards dikhayein.
      }
    } on ApiException catch (e) {
      error = e.message;
    } catch (e) {
      error = 'Load failed: $e';
    } finally {
      loading = false;
      notifyListeners();
    }
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
