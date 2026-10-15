import 'dart:convert';

import 'package:http/http.dart' as http;

import 'config.dart';
import 'models.dart';
import 'session.dart';

class ApiException implements Exception {
  ApiException(this.message, {this.statusCode});
  final String message;
  final int? statusCode;
  @override
  String toString() => message;
}

/// REST wrapper: unwraps the API envelope {success, message, data}.
class ApiClient {
  final http.Client _http = http.Client();

  Future<Map<String, dynamic>> _send(
    String method,
    String path, {
    Map<String, dynamic>? body,
    bool auth = true,
  }) async {
    final uri = Uri.parse('${AppConfig.apiBase}$path');
    final headers = <String, String>{'Content-Type': 'application/json'};
    if (auth && Session.token != null) {
      headers['Authorization'] = 'Bearer ${Session.token}';
    }

    http.Response res;
    try {
      switch (method) {
        case 'GET':
          res = await _http.get(uri, headers: headers).timeout(const Duration(seconds: 20));
          break;
        case 'POST':
          res = await _http
              .post(uri, headers: headers, body: jsonEncode(body))
              .timeout(const Duration(seconds: 20));
          break;
        case 'PATCH':
          res = await _http
              .patch(uri, headers: headers, body: jsonEncode(body))
              .timeout(const Duration(seconds: 20));
          break;
        default:
          throw ApiException('Unsupported method $method');
      }
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Server unreachable (${AppConfig.apiBase}). $e');
    }

    Map<String, dynamic> envelope;
    try {
      envelope = jsonDecode(res.body) as Map<String, dynamic>;
    } catch (_) {
      throw ApiException('Unexpected server response (${res.statusCode})');
    }

    if (res.statusCode == 401) throw ApiException('Login expired — dubara login karein', statusCode: 401);
    if (envelope['success'] != true) {
      throw ApiException(envelope['message'] as String? ?? 'Request failed', statusCode: res.statusCode);
    }
    return envelope;
  }

  // ---------- Auth ----------
  Future<void> login(String username, String password) async {
    final env = await _send('POST', '/api/auth/login',
        body: {'username': username.trim(), 'password': password}, auth: false);
    final data = env['data'] as Map<String, dynamic>;
    final user = data['user'] as Map<String, dynamic>? ?? {};
    await Session.save(
      token: data['token'] as String,
      username: user['username'] as String? ?? username.trim(),
      fullName: user['fullName'] as String? ?? username.trim(),
      expiresAtUtc: DateTime.tryParse(data['expiresAtUtc'] as String? ?? ''),
    );
  }

  // ---------- Parent dashboard ----------
  Future<List<ChildCard>> getChildren() async {
    final env = await _send('GET', '/api/parents/me/children');
    return (env['data'] as List? ?? [])
        .map((e) => ChildCard.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<ParentSettings> getSettings() async {
    final env = await _send('GET', '/api/parents/me/settings');
    return ParentSettings.fromJson(env['data'] as Map<String, dynamic>);
  }

  Future<ParentSettings> updateSettings({
    String? phone,
    String? preferredLanguage,
    bool? receiveWhatsApp,
  }) async {
    final env = await _send('PATCH', '/api/parents/me/settings', body: {
      if (phone != null) 'phone': phone,
      if (preferredLanguage != null) 'preferredLanguage': preferredLanguage,
      if (receiveWhatsApp != null) 'receiveWhatsApp': receiveWhatsApp,
    });
    return ParentSettings.fromJson(env['data'] as Map<String, dynamic>);
  }

  // ---------- Fees ----------
  Future<List<FeeRow>> getMyFees() async {
    final env = await _send('GET', '/api/fees/my');
    return (env['data'] as List? ?? [])
        .map((e) => FeeRow.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<ReceiptData> getReceipt(int feeId) async {
    final env = await _send('GET', '/api/fees/my/$feeId/receipt');
    return ReceiptData.fromJson(env['data'] as Map<String, dynamic>);
  }

  // ---------- Alerts / announcements ----------
  Future<List<AlertItem>> getStudentEvents(int studentId) async {
    final env = await _send('GET', '/api/bus-attendance/student/$studentId?page=1&pageSize=30');
    final data = env['data'] as Map<String, dynamic>? ?? {};
    return (data['items'] as List? ?? [])
        .map((e) => AlertItem.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<List<AnnouncementItem>> getAnnouncements() async {
    final env = await _send('GET', '/api/announcements?page=1&pageSize=50');
    final data = env['data'] as Map<String, dynamic>? ?? {};
    return (data['items'] as List? ?? [])
        .map((e) => AnnouncementItem.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  // ---------- Devices ----------
  Future<void> registerDevice(String token, String platform) =>
      _send('POST', '/api/devices/register', body: {'token': token, 'platform': platform});

  Future<void> unregisterDevice(String token) =>
      _send('POST', '/api/devices/unregister', body: {'token': token, 'platform': 'android'});
}
