import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:flutter/foundation.dart';

import '../config/app_config.dart';
import '../models/models.dart';
import '../services/api_client.dart';
import '../services/background_gps_service.dart';
import '../services/gps_service.dart';
import '../services/offline_outbox.dart';
import '../services/session_service.dart';
import '../services/tts_service.dart';

enum AuthStatus { unknown, loggedOut, loggedIn }

/// Root app state: auth + bus + trip + manifest + sync. One ChangeNotifier —
/// small app, keeps everything straightforward for junior maintenance.
class AppState extends ChangeNotifier {
  AppState()
      : _api = ApiClient(SessionService());

  final ApiClient _api;
  final GpsService _gps = GpsService();

  // derived state
  AuthStatus authStatus = AuthStatus.unknown;
  LoginResult? login;
  Bus? bus;
  Trip? activeTrip;
  List<ManifestStudent> students = [];
  int pendingOutbox = 0;
  bool online = true;
  bool busy = false;
  String? error;
  String? lastFlash; // transient success message ("✅ Ahmed picked!")

  StreamSubscription<List<ConnectivityResult>>? _connSub;
  Timer? _syncTimer;

  ApiClient get api => _api;

  // =============== boot ===============

  Future<void> boot() async {
    await AppConfig.loadServerOverride();
    final restored = await _api.restoreSession();
    if (restored == null) {
      authStatus = AuthStatus.loggedOut;
      notifyListeners();
      return;
    }
    login = restored;
    authStatus = AuthStatus.loggedIn;
    notifyListeners();
    await refreshBusAndManifest();
    _startConnectivityWatch();
  }

  void _startConnectivityWatch() {
    _connSub?.cancel();
    _connSub = Connectivity().onConnectivityChanged.listen((results) {
      final wasOnline = online;
      online = results.any((r) => r != ConnectivityResult.none);
      if (!wasOnline && online) {
        _flushOutbox(); // network wapas aya — pending sab bhejo
      }
      notifyListeners();
    });
    _syncTimer?.cancel();
    _syncTimer = Timer.periodic(const Duration(seconds: 45), (_) => _flushOutbox());
  }

  // =============== auth ===============

  Future<bool> doLogin(String username, String password) async {
    busy = true;
    error = null;
    notifyListeners();
    try {
      login = await _api.login(username.trim(), password);
      if (login!.role != 'Driver' && login!.role != 'Admin' && login!.role != 'SuperAdmin') {
        error = 'Yeh account driver ka nahi hai (${login!.role}).';
        await _api.logout();
        login = null;
        return false;
      }
      authStatus = AuthStatus.loggedIn;
      await refreshBusAndManifest();
      _startConnectivityWatch();
      return true;
    } on ApiException catch (e) {
      error = e.toString();
      return false;
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  Future<void> doLogout() async {
    await endTrip(silent: true);
    await BackgroundGpsService.instance.stopTrip();
    await _api.logout();
    login = null;
    bus = null;
    activeTrip = null;
    students = [];
    authStatus = AuthStatus.loggedOut;
    notifyListeners();
  }

  // =============== bus + manifest ===============

  /// Server par jo trip chal rahi hai use app mein wapas laata hai (app restart / pehle ki trip).
  Future<void> restoreActiveTrip() async {
    try {
      final trips = await _api.myActiveTrips();
      final previousId = activeTrip?.id;
      activeTrip = trips.isEmpty ? null : trips.first;
      if (activeTrip != null && activeTrip!.id != previousId) {
        await BackgroundGpsService.instance.startTrip(activeTrip!.id);
        _startFgPush(activeTrip!.id);
      }
    } catch (_) {
      // Offline: jo state hai wahi rehne do.
    }
    notifyListeners();
  }

  Future<void> refreshBusAndManifest() async {
    try {
      bus = await _api.myBus();
      error = null;
      await loadManifest();
      await restoreActiveTrip();
    } on ApiException catch (e) {
      // Offline par manifest pehle se hai to chalne do; warna error dikhao.
      if (students.isEmpty) error = e.toString();
    }
    notifyListeners();
  }

  Future<void> loadManifest() async {
    final b = bus;
    if (b == null) return;
    try {
      final fresh = await _api.manifest(b.id);
      students = fresh;
      error = null;
      await _refreshOutboxCount();
    } on ApiException {
      // offline — keep whatever we had
    }
    notifyListeners();
  }

  // =============== trip ===============

  // Fallback: jab app khuli ho to GPS khud bhi bhejta hai (background service na chale to bhi bus map par aaye).
  Timer? _fgTimer;

  void _startFgPush(int tripId) {
    _fgTimer?.cancel();
    Future<void> tick() async {
      try {
        final pos = await _gps.currentFix();
        await _api.pushLocation(tripId, pos.latitude, pos.longitude,
            pos.speed < 0 ? 0 : pos.speed * 3.6);
        await BackgroundGpsService.writeGpsStatus('OK: app khuli hai, server ko bheja');
      } catch (e) {
        await BackgroundGpsService.writeGpsStatus(
            'ERR (app): ${e.toString().split('\n').first}');
      }
    }

    unawaited(tick());
    _fgTimer = Timer.periodic(AppConfig.gpsPushInterval, (_) => tick());
  }

  void _stopFgPush() {
    _fgTimer?.cancel();
    _fgTimer = null;
  }

  Future<bool> startTrip() async {
    final b = bus;
    if (b == null) {
      error = 'Bus assign nahi hai.';
      notifyListeners();
      return false;
    }
    busy = true;
    error = null;
    notifyListeners();
    try {
      // GPS permission zaroor (permission na ho to trip nahi). Fix ka timeout trip ko na roke.
      try {
        await _gps.currentFix();
      } on StateError {
        rethrow;
      } catch (_) {
        // GPS fix late/timeout: trip shuru karein, tracking khud location bhejti rahegi.
      }

      activeTrip = await _api.startTrip(b.id);
      await BackgroundGpsService.instance.startTrip(activeTrip!.id);
      _startFgPush(activeTrip!.id);
      try {
        await loadManifest();
      } catch (_) {
        // Manifest ka masla trip ko fail nahi karta; refresh par dobara load hoga.
      }
      lastFlash = '🚌 Trip #${activeTrip!.id} started — tracking ON';
      return true;
    } on StateError catch (e) {
      error = e.message;
      return false;
    } on ApiException catch (e) {
      error = e.toString();
      if (e.toString().contains('active trip')) {
        // Server par pehle se trip chal rahi hai: use app mein le aayein taake END TRIP dikhe.
        await restoreActiveTrip();
        error = 'Is bus ki trip pehle se chal rahi hai. Neeche END TRIP dabayein, phir naya trip shuru karein.';
      }
      return false;
    } catch (e) {
      error = 'Trip shuru nahi ho saka: $e';
      return false;
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  Future<void> endTrip({bool silent = false}) async {
    final t = activeTrip;
    if (t == null) return;
    try {
      await _api.endTrip(t.id);
    } on ApiException catch (e) {
      // Pehle se band ho chuki trip: ise khatam hi samjhein.
      if (!e.toString().contains('already')) {
        // Band nahi hui to trip app mein rehne do, taake dobara END TRIP dabaya ja sake.
        error = 'Trip band nahi ho saki. Internet dekh kar dobara END TRIP dabayein.';
        notifyListeners();
        return;
      }
    }
    activeTrip = null;
    _stopFgPush();
    await BackgroundGpsService.instance.stopTrip();
    if (!silent) lastFlash = '🏁 Trip khatam.';
    notifyListeners();
  }

  // =============== pick / drop (offline-first) ===============

  /// Tap → GPS + timestamp capture → immediate POST; fail = outbox mein queue.
  Future<bool> recordEvent(ManifestStudent s, String type /* 'pick-in' | 'drop-out' */) async {
    final b = bus;
    final t = activeTrip;
    if (b == null || t == null) {
      error = 'Pehle trip start karein.';
      notifyListeners();
      return false;
    }
    if (type == 'pick-in' && !s.canPickIn) return false;
    if (type == 'drop-out' && !s.canDropOut) return false;

    busy = true;
    notifyListeners();

    try {
      final pos = await _gps.currentFix();
      final evt = PendingEvent(
        studentId: s.studentId,
        busId: b.id,
        tripId: t.id,
        type: type,
        latitude: pos.latitude,
        longitude: pos.longitude,
        timestampUtc: DateTime.now().toUtc().toIso8601String(),
      );

      bool sent = false;
      try {
        await _api.postBusEvent(evt);
        sent = true;
      } on ApiException {
        // Offline / server down → queue karo, auto-sync sambhal lega.
        await OfflineOutbox.instance.enqueue(evt);
      }

      // UI turant update (optimistic)
      s.status = type == 'pick-in' ? PickStatus.picked : PickStatus.dropped;
      await _refreshOutboxCount();

      final verb = type == 'pick-in' ? 'picked up' : 'dropped off';
      lastFlash = sent
          ? '✅ ${s.fullName} $verb!'
          : '📥 ${s.fullName} $verb — offline queue (${online ? "server sync hoga" : "internet aane par bhejeinge"})';

      await TtsService.instance.speak('${s.fullName} $verb');
      return true;
    } on StateError catch (e) {
      error = e.message;
      return false;
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  Future<void> _refreshOutboxCount() async {
    pendingOutbox = await OfflineOutbox.instance.pendingCount();
    notifyListeners();
  }

  /// Drains the outbox oldest-first; a failed item stays (counted attempts).
  Future<void> _flushOutbox() async {
    final items = await OfflineOutbox.instance.pending();
    if (items.isEmpty) return;
    for (final e in items) {
      try {
        await _api.postBusEvent(e);
        await OfflineOutbox.instance.remove(e.id!);
      } on ApiException catch (ex) {
        // 409 (already recorded server-side) → drop from outbox, sync finished
        if (ex.toString().toLowerCase().contains('already') ||
            ex.toString().toLowerCase().contains('duplicate')) {
          await OfflineOutbox.instance.remove(e.id!);
        } else {
          await OfflineOutbox.instance.bumpAttempts(e.id!);
          break; // network probably still down — next tick
        }
      }
    }
    await _refreshOutboxCount();
    await loadManifest(); // statuses reconcile
  }

  void clearFlash() {
    lastFlash = null;
  }

  @override
  void dispose() {
    _connSub?.cancel();
    _syncTimer?.cancel();
    super.dispose();
  }
}
