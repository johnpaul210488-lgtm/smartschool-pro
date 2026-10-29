import 'package:geolocator/geolocator.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Wraps device GPS: permission handling + one-shot high-accuracy reads.
class GpsService {
  static const _kBgAsked = 'bg_location_asked_v1';

  /// Ensures location services + permissions, then returns the current fix.
  /// Pick-up/drop jaldi (max ~6 second) tak fix le; fix na mile to last known position.
  Future<Position> currentFix() async {
    if (!await Geolocator.isLocationServiceEnabled()) {
      throw StateError('Location services band hain — Settings se GPS on karein.');
    }

    await _ensurePermission();

    try {
      return await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.high,
          distanceFilter: 0,
          timeLimit: Duration(seconds: 6),
        ),
      );
    } catch (_) {
      // Cold GPS / indoor: pehle wali (taaza) position se kaam chalayein taake pick-up na atke.
      final last = await Geolocator.getLastKnownPosition();
      if (last != null) return last;
      rethrow;
    }
  }

  /// Permission ka prompt baar-baar nahi: sirf zaroorat par aur background ke liye ek dafa.
  Future<void> _ensurePermission() async {
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

    // Background tracking ke liye "Allow all the time" — sirf ek dafa poochein.
    if (permission == LocationPermission.whileInUse) {
      final prefs = await SharedPreferences.getInstance();
      if (!(prefs.getBool(_kBgAsked) ?? false)) {
        await prefs.setBool(_kBgAsked, true);
        await Geolocator.requestPermission();
      }
    }
  }
}
