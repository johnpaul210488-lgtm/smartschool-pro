// <-- CHANGED: targeted driver trip recovery tests
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:smartschool_driver/config/app_config.dart';
import 'package:smartschool_driver/services/api_client.dart';
import 'package:smartschool_driver/services/session_service.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  test('trip and notification prompt state survive service instances', () async {
    SharedPreferences.setMockInitialValues({});
    final session = SessionService();
    expect(await session.loadActiveTrip(), isNull);
    await session.saveActiveTrip(42);
    expect(await SessionService().loadActiveTrip(), 42);
    expect(await session.claimNotificationPrompt(), isTrue);
    expect(await SessionService().claimNotificationPrompt(), isFalse);
    await session.clear();
    expect(await session.loadActiveTrip(), isNull);
    expect(await session.claimNotificationPrompt(), isFalse);
  });

  group('trip start and recovery', () {
    late HttpServer server;
    late ApiClient api;
    late String oldBase;
    late List<Map<String, dynamic>> active;
    late int starts;
    late bool failActive;

    setUp(() async {
      SharedPreferences.setMockInitialValues({});
      HttpOverrides.global = null;
      oldBase = AppConfig.apiBaseUrl;
      active = [];
      starts = 0;
      failActive = false;
      server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      AppConfig.apiBaseUrl = 'http://127.0.0.1:${server.port}';
      server.listen((request) async {
        Object? data;
        var success = true;
        var message = '';
        if (request.uri.path == '/api/auth/login') {
          data = {
            'token': 'test-token',
            'expiresAtUtc': DateTime.now().toUtc().add(const Duration(hours: 1)).toIso8601String(),
            'user': {'id': 1, 'username': 'driver', 'fullName': 'Driver', 'role': 'Driver'},
          };
        } else if (request.uri.path == '/api/trips/active') {
          if (failActive) {
            request.response.statusCode = 503;
            success = false;
            message = 'Unavailable';
          } else {
            data = active;
          }
        } else if (request.uri.path == '/api/trips/start') {
          starts++;
          data = {'id': 8, 'busId': 3, 'busNumber': '3', 'route': 'School'};
        } else {
          request.response.statusCode = 404;
          success = false;
        }
        request.response.headers.contentType = ContentType.json;
        request.response.write(jsonEncode({'success': success, 'message': message, 'data': data}));
        await request.response.close();
      });
      api = ApiClient(SessionService());
      await api.login('driver', 'password');
    });

    tearDown(() async {
      AppConfig.apiBaseUrl = oldBase;
      await server.close(force: true);
    });

    test('resumes the active trip for the assigned bus', () async {
      active = [
        {'id': 4, 'busId': 2, 'busNumber': '2', 'route': 'Other'},
        {'id': 7, 'busId': 3, 'busNumber': '3', 'route': 'School'},
      ];
      final trip = await api.startTrip(3);
      expect(trip.id, 7);
      expect(starts, 0);
    });

    test('creates a trip only when the bus has no active trip', () async {
      final trip = await api.startTrip(3);
      expect(trip.id, 8);
      expect(starts, 1);
    });

    test('does not create a duplicate if active-trip lookup fails', () async {
      failActive = true;
      await expectLater(api.startTrip(3), throwsA(isA<Exception>()));
      expect(starts, 0);
    });
  });
}
