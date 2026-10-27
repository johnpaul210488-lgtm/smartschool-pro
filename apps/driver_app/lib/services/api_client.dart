import 'dart:convert';
import 'dart:io';

import 'package:http/http.dart' as http;

import '../config/app_config.dart';
import '../models/models.dart';
import 'session_service.dart';

/// Envelope-aware typed API client with JWT + auto login-refresh.
class ApiClient {
  ApiClient(this._session);

  final SessionService _session;
  LoginResult? _login;
  final _http = http.Client();

  LoginResult? get currentLogin => _login;

  Future<LoginResult?> restoreSession() async {
    _login = await _session.load();
    // Treat a token expiring in the next 5 min as expired (forces silent re-login).
    if (_login != null &&
        _login!.expiresAtUtc.isBefore(DateTime.now().toUtc().add(const Duration(minutes: 5)))) {
      _login = null;
    }
    return _login;
  }

  Future<LoginResult> login(String username, String password) async {
    final env = await _post<LoginResult>(
      'api/auth/login',
      {'username': username, 'password': password},
      (d) => LoginResult.fromJson(d as Map<String, dynamic>),
      anonymous: true,
    );
    _login = env.data!;
    await _session.save(_login!);
    return _login!;
  }

  Future<void> logout() async {
    _login = null;
    await _session.clear();
  }

  // ---------- typed endpoint helpers ----------

  Future<Bus> myBus() async {
    final env = await _get<Bus>('api/buses/mine', (d) => Bus.fromJson(d as Map<String, dynamic>));
    return env.data!;
  }

  Future<List<ManifestStudent>> manifest(int busId) async {
    final env = await _get<List<ManifestStudent>>(
      'api/buses/$busId/manifest',
      (d) => (d as List<dynamic>)
          .map((e) => ManifestStudent.fromJson(e as Map<String, dynamic>))
          .toList(),
    );
    return env.data ?? [];
  }

  /// Driver ki active trips server se (restart ke baad bhi wahi trip pehchanne ke liye).
  Future<List<Trip>> myActiveTrips() async {
    final env = await _get<List<Trip>>(
      'api/trips/active',
      (d) => (d as List).map((e) => Trip.fromJson(e as Map<String, dynamic>)).toList(),
    );
    return env.data ?? const [];
  }

  Future<Trip> startTrip(int busId, {int type = 1 /* ToSchool */}) async {
    final env = await _post<Trip>(
      'api/trips/start',
      {'busId': busId, 'type': type},
      (d) => Trip.fromJson(d as Map<String, dynamic>),
    );
    return env.data!;
  }

  Future<void> endTrip(int tripId) async {
    await _post<Map<String, dynamic>>('api/trips/$tripId/end', {}, (d) => const {});
  }

  /// GPS uplink during an active trip (REST, not SignalR — safer for background isolates).
  Future<void> pushLocation(int tripId, double lat, double lng, double? speedKmh) async {
    await _post<Map<String, dynamic>>(
      'api/trips/$tripId/location',
      {'latitude': lat, 'longitude': lng, if (speedKmh != null) 'speedKmh': speedKmh},
      (d) => const {},
    );
  }

  /// Replays one queued event. 'pick-in' | 'drop-out'.
  Future<void> postBusEvent(PendingEvent e) async {
    await _post<Map<String, dynamic>>('api/bus-attendance/${e.type}', e.toApiBody(), (d) => const {});
  }

  // ---------- core plumbing ----------

  Future<Envelope<T>> _get<T>(String path, T Function(Object?) parse) =>
      _send('GET', path, null, parse);

  Future<Envelope<T>> _post<T>(String path, Object body, T Function(Object?) parse,
          {bool anonymous = false}) =>
      _send('POST', path, body, parse, anonymous: anonymous);

  Future<Envelope<T>> _send<T>(String method, String path, Object? body,
      T Function(Object?) parse,
      {bool anonymous = false}) async {
    final uri = Uri.parse('${AppConfig.apiBaseUrl}/$path');
    final headers = <String, String>{
      'Content-Type': 'application/json',
      'Accept': 'application/json',
      if (!anonymous && _login != null) 'Authorization': 'Bearer ${_login!.token}',
    };

    http.Response resp;
    try {
      resp = method == 'GET'
          ? await _http.get(uri, headers: headers).timeout(const Duration(seconds: 12))
          : await _http
              .post(uri, headers: headers, body: jsonEncode(body))
              .timeout(const Duration(seconds: 12));
    } on SocketException {
      throw ApiException('Internet nahi hai (offline mode).');
    } on HttpException catch (e) {
      throw ApiException('Network error: ${e.message}');
    } catch (_) {
      throw ApiException('Server se jawab nahi mila (timeout). API chal rahi hai?');
    }

    Map<String, dynamic> env;
    try {
      env = jsonDecode(resp.body) as Map<String, dynamic>;
    } catch (_) {
      throw ApiException('Server error ${resp.statusCode}');
    }

    final success = env['success'] as bool? ?? false;
    final message = env['message'] as String? ?? '';
    final errors =
        (env['errors'] as List<dynamic>?)?.map((e) => e.toString()).toList() ?? const [];

    if (!resp.statusCode.toString().startsWith('2') || !success) {
      throw ApiException(message.isNotEmpty ? message : 'HTTP ${resp.statusCode}', errors: errors);
    }

    return Envelope<T>(success: success, message: message, data: parse(env['data']), errors: errors);
  }
}
