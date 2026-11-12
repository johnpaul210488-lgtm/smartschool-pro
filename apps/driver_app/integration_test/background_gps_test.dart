// <-- CHANGED: exercise the real Android background service handshake.
import 'package:flutter_background_service/flutter_background_service.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:smartschool_driver/services/background_gps_service.dart';
import 'package:smartschool_driver/services/session_service.dart';

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('GPS service starts and stops with an existing trip', (tester) async {
    await BackgroundGpsService.instance.startTrip(32);
    expect(await FlutterBackgroundService().isRunning(), isTrue);
    expect(await SessionService().loadActiveTrip(), 32);
    await BackgroundGpsService.instance.startTrip(32); // <-- CHANGED
    expect(await FlutterBackgroundService().isRunning(), isTrue);
    await BackgroundGpsService.instance.stopTrip();
    expect(await SessionService().loadActiveTrip(), isNull);
  });
}
