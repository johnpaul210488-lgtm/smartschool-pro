import 'package:geolocator/geolocator.dart';

/// Wraps device GPS: permission handling + one-shot high-accuracy reads.
class GpsService {
  /// Max time to wait for a GPS fix before giving up (prevents a "dead" button).
  static const Duration fixTimeout = Duration(seconds: 15);

  /// Ensures location services + permissions, then returns the current fix.
  /// Throws a readable [StateError] when unavailable.
  Future<Position> currentFix() async {
    if (!await Geolocator.isLocationServiceEnabled()) {
      throw StateError('Phone ka GPS band hai — Settings se Location ON karein.');
    }

    var permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied) {
      permission = await Geolocator.requestPermission();
    }
    if (permission == LocationPermission.denied) {
      throw StateError('Location permission nahi mili — phone Settings → Apps → SmartSchool Driver → Permissions → Location se Allow karein.');
    }
    if (permission == LocationPermission.deniedForever) {
      throw StateError('Location permission permanently blocked — phone Settings → Apps → SmartSchool Driver → Permissions → Location se Allow karein.');
    }
    // NOTE: Android 10+ grants "Allow all the time" (background) only from the
    // system settings UI — a runtime request can never produce it. A
    // whileInUse fix still works for the foreground part of the trip; the
    // caller surfaces the background limitation separately via
    // [isBackgroundLimited].

    try {
      return await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.high,
          distanceFilter: 0,
        ),
      ).timeout(
        fixTimeout,
        onTimeout: () => throw StateError(
            'GPS ka signal nahi mil raha — phone khuli jagah (open sky / window ke paas) rakhein, GPS off/on karein, phir try karein.'),
      );
    } on StateError {
      rethrow;
    } catch (_) {
      throw StateError('GPS fix nahi le saka — GPS off/on karke dobara try karein.');
    }
  }

  /// True when only "while in use" is granted — background tracking will be
  /// limited once the app is minimized. Android 10+ needs "Allow all the time",
  /// which only the system settings page can grant.
  Future<bool> isBackgroundLimited() async {
    final permission = await Geolocator.checkPermission();
    return permission == LocationPermission.whileInUse;
  }
}
