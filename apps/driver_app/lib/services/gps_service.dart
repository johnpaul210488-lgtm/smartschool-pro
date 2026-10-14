import 'package:geolocator/geolocator.dart';

/// Wraps device GPS: permission handling + one-shot high-accuracy reads.
class GpsService {
  /// Ensures location services + permissions, then returns the current fix.
  /// Throws a readable message when unavailable.
  Future<Position> currentFix() async {
    if (!await Geolocator.isLocationServiceEnabled()) {
      throw StateError('Location services band hain — Settings se GPS on karein.');
    }

    var permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied) {
      permission = await Geolocator.requestPermission();
    }
    if (permission == LocationPermission.denied) {
      throw StateError('Location permission nahi mili — app settings check karein.');
    }
    if (permission == LocationPermission.deniedForever) {
      throw StateError('Location permission permanently blocked — Settings kholein.');
    }
    // Background tracking needs "allow all the time" on Android 10+
    if (permission == LocationPermission.whileInUse) {
      permission = await Geolocator.requestPermission();
    }

    return Geolocator.getCurrentPosition(
      locationSettings: const LocationSettings(
        accuracy: LocationAccuracy.high,
        distanceFilter: 0,
      ),
    );
  }
}
