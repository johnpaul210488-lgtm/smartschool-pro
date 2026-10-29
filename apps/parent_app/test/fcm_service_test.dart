import 'dart:async';

import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:smartschool_parent/core/api_client.dart';
import 'package:smartschool_parent/core/fcm_service.dart';
import 'package:smartschool_parent/core/session.dart';

class FakeFcmPlatform implements FcmPlatform {
  int setupCount = 0;
  int permissionRequests = 0;
  Object? setupError;
  String? token = 'token-1';
  AuthorizationStatus permission = AuthorizationStatus.authorized;
  final refreshes = StreamController<String>.broadcast(sync: true);
  final messages = StreamController<RemoteMessage>.broadcast(sync: true);

  @override
  Future<void> setup() async {
    setupCount++;
    if (setupError != null) throw setupError!;
  }
  @override
  Future<AuthorizationStatus> requestPermission() async { permissionRequests++; return permission; }
  @override
  Future<AuthorizationStatus> currentPermission() async => permission;
  @override
  Future<String?> getToken() async => token;
  @override
  Stream<String> get tokenRefresh => refreshes.stream;
  @override
  Stream<RemoteMessage> get foregroundMessages => messages.stream;

  Future<void> dispose() async {
    await refreshes.close();
    await messages.close();
  }
}

class FakeApi extends ApiClient {
  final List<String> calls = [];
  bool failNext = false;
  Completer<void>? registrationGate;
  Completer<void>? registrationStarted;

  @override
  Future<void> registerDevice(String token, String platform) async {
    calls.add('register:$token');
    registrationStarted?.complete();
    final gate = registrationGate;
    if (gate != null) await gate.future;
    if (failNext) {
      failNext = false;
      throw ApiException('offline');
    }
  }

  @override
  Future<void> unregisterDevice(String token) async {
    calls.add('unregister:$token');
  }
}

void main() {
  setUp(() {
    Session.token = 'test-jwt';
    Session.expiresAtUtc = DateTime.now().toUtc().add(const Duration(hours: 1));
  });
  tearDown(() {
    Session.token = null;
    Session.expiresAtUtc = null;
  });

  test('coalesces setup and retries a failed server registration', () async {
    final platform = FakeFcmPlatform();
    final api = FakeApi()..failNext = true;
    final service = FcmService.forTesting(platform);

    await Future.wait([service.init(api), service.init(api)]);
    expect(platform.setupCount, 1);
    expect(platform.permissionRequests, 1);
    expect(api.calls, ['register:token-1']);
    expect(service.status.value, PushStatus.registrationFailed);

    await service.init(api);
    expect(service.status.value, PushStatus.registered);
    expect(platform.setupCount, 1);
    expect(api.calls, ['register:token-1', 'register:token-1']);
    await platform.dispose();
  });

  test('unavailable Firebase does not throw and setup can be retried', () async {
    final platform = FakeFcmPlatform()..setupError = StateError('missing Firebase config');
    final service = FcmService.forTesting(platform);
    final api = FakeApi();
    await service.init(api);
    expect(service.status.value, PushStatus.unavailable);
    expect(api.calls, isEmpty);
    platform.setupError = null;
    await service.init(api);
    expect(service.status.value, PushStatus.registered);
    expect(platform.setupCount, 2);
    await platform.dispose();
  });

  test('permission status is rechecked when returning to the app', () async {
    final platform = FakeFcmPlatform()..permission = AuthorizationStatus.denied;
    final service = FcmService.forTesting(platform);
    final api = FakeApi();
    await service.init(api);
    expect(service.status.value, PushStatus.permissionDenied);
    platform.permission = AuthorizationStatus.authorized;
    await service.init(api);
    expect(service.status.value, PushStatus.registered);
    expect(platform.permissionRequests, 1);
    await platform.dispose();
  });

  test('refresh cleans up the old token and registers the new token', () async {
    final platform = FakeFcmPlatform();
    final service = FcmService.forTesting(platform);
    final api = FakeApi();
    await service.init(api);
    platform.token = 'token-2';
    platform.refreshes.add('token-2');
    // Wait for the queued unregister/register operations.
    await Future<void>.delayed(Duration.zero);
    expect(api.calls, ['register:token-1', 'unregister:token-1', 'register:token-2']);
    await service.unregister(api);
    expect(api.calls.last, 'unregister:token-2');
    await platform.dispose();
  });

  test('logout waits for an in-flight registration and removes all its tokens', () async {
    final platform = FakeFcmPlatform();
    final api = FakeApi()
      ..registrationStarted = Completer<void>()
      ..registrationGate = Completer<void>();
    final service = FcmService.forTesting(platform);
    final initializing = service.init(api);
    await api.registrationStarted!.future;
    // A token refresh races with the first registration and logout.
    platform.token = 'token-2';
    platform.refreshes.add('token-2');
    final loggingOut = service.unregister(api);
    api.registrationGate!.complete();
    await Future.wait([initializing, loggingOut]);
    expect(api.calls, containsAll(['register:token-1', 'unregister:token-1', 'unregister:token-2']));
    expect(api.calls, isNot(contains('register:token-2')));
    final count = api.calls.length;
    platform.refreshes.add('token-3');
    await Future<void>.delayed(Duration.zero);
    expect(api.calls.length, count); // No re-registration while logged out.
    await platform.dispose();
  });
}
