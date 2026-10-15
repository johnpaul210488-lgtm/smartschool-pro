import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:signalr_netcore/signalr_client.dart';

import 'config.dart';
import 'models.dart';
import 'session.dart';

/// Live channel (/hubs/notifications). Server auto-joins the parent to
/// ParentGroup_{studentId}, their children's BusGroup_{busId} and ParentsGroup —
/// so events simply arrive here.
class SignalRService {
  SignalRService({
    required this.onAlert,
    required this.onBusLocation,
    required this.onAnnouncement,
  });

  /// (eventType, studentId) — ChildrenProvider refreshes on this.
  final void Function(String eventType, int studentId) onAlert;
  final void Function(BusLocation location) onBusLocation;
  final void Function() onAnnouncement;

  HubConnection? _hub;
  bool get isConnected => _hub?.state == HubConnectionState.Connected;

  Future<void> start() async {
    if (_hub != null && isConnected) return;
    try {
      _hub ??= HubConnectionBuilder()
          .withUrl(
            AppConfig.hubUrl,
            options: HttpConnectionOptions(
              accessTokenFactory: () async => Session.token ?? '',
            ),
          )
          .withAutomaticReconnect(retryDelays: [0, 2000, 5000, 10000, 30000])
          .build();

      _hub!.on('SendPickInAlert', (args) {
        final m = _first(args);
        if (m != null) onAlert('PickIn', m['studentId'] as int? ?? 0);
      });
      _hub!.on('SendDropOutAlert', (args) {
        final m = _first(args);
        if (m != null) onAlert('DropOut', m['studentId'] as int? ?? 0);
      });
      _hub!.on('UpdateBusLocation', (args) {
        final m = _first(args);
        if (m != null) onBusLocation(BusLocation.fromJson(m));
      });
      _hub!.on('ReceiveAnnouncement', (_) => onAnnouncement());

      _hub!.onclose(({error}) => debugPrint('[SignalR] closed: $error'));
      _hub!.onreconnecting(({error}) => debugPrint('[SignalR] reconnecting…'));
      _hub!.onreconnected(({connectionId}) => debugPrint('[SignalR] back online'));

      await _hub!.start();
      debugPrint('[SignalR] connected');
    } catch (e) {
      debugPrint('[SignalR] start failed: $e (server offline? alerts still arrive on refresh)');
    }
  }

  Map<String, dynamic>? _first(List<Object?>? args) =>
      (args != null && args.isNotEmpty && args.first is Map)
          ? (args.first as Map).cast<String, dynamic>()
          : null;

  Future<void> stop() async {
    await _hub?.stop();
    _hub = null;
  }
}
