import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import 'package:provider/provider.dart';

import '../../providers/app_state.dart';

/// Top banners: offline bar + outbox counter + error + flash (auto-hides).
class StatusBanners extends StatelessWidget {
  const StatusBanners({super.key, required this.state});
  final AppState state;

  @override
  Widget build(BuildContext context) {
    // Auto-clear flash after a moment
    if (state.lastFlash != null) {
      final msg = state.lastFlash!;
      Future.delayed(const Duration(seconds: 4), () {
        if (state.lastFlash == msg) state.clearFlash();
      });
    }

    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        if (!state.online)
          Container(
            width: double.infinity,
            color: Colors.red.shade700,
            padding: const EdgeInsets.symmetric(vertical: 6),
            child: const Text('📡 OFFLINE — taps queue ho rahi hain, net aane par sync',
                textAlign: TextAlign.center,
                style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 14)),
          ),
        if (state.error != null)
          Container(
            width: double.infinity,
            color: Colors.red.shade900,
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
            child: Row(children: [
              const Icon(Icons.error_outline, color: Colors.white, size: 22),
              const SizedBox(width: 8),
              Expanded(
                child: Text(state.error!,
                    style: const TextStyle(
                        color: Colors.white, fontWeight: FontWeight.bold, fontSize: 15)),
              ),
              IconButton(
                tooltip: 'Band karein',
                padding: EdgeInsets.zero,
                constraints: const BoxConstraints(minWidth: 36, minHeight: 36),
                icon: const Icon(Icons.close, color: Colors.white, size: 20),
                onPressed: () => context.read<AppState>().clearError(),
              ),
            ]),
          ),
        if (state.backgroundLimited)
          Container(
            width: double.infinity,
            color: Colors.orange.shade800,
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
            child: Row(children: [
              const Icon(Icons.location_searching, color: Colors.white, size: 22),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                    'Background GPS ke liye "Allow all the time" chahiye — warna app minimize hone par tracking band ho jayegi.',
                    style: const TextStyle(
                        color: Colors.white, fontWeight: FontWeight.bold, fontSize: 13)),
              ),
              TextButton(
                onPressed: Geolocator.openAppSettings,
                style: TextButton.styleFrom(
                  foregroundColor: Colors.white,
                  backgroundColor: Colors.white.withValues(alpha: 0.2),
                  padding: const EdgeInsets.symmetric(horizontal: 10),
                ),
                child: const Text('SETTINGS', style: TextStyle(fontWeight: FontWeight.bold)),
              ),
            ]),
          ),
        if (state.pendingOutbox > 0)
          Container(
            width: double.infinity,
            color: Colors.orange.shade700,
            padding: const EdgeInsets.symmetric(vertical: 6),
            child: Text('📥 ${state.pendingOutbox} event(s) sync pending',
                textAlign: TextAlign.center,
                style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 14)),
          ),
        if (state.lastFlash != null)
          AnimatedContainer(
            duration: const Duration(milliseconds: 250),
            width: double.infinity,
            color: state.lastFlash!.startsWith('✅') ? Colors.green.shade700 : Colors.blue.shade700,
            padding: const EdgeInsets.all(10),
            child: Text(state.lastFlash!,
                textAlign: TextAlign.center,
                style: const TextStyle(
                    color: Colors.white, fontWeight: FontWeight.bold, fontSize: 17)),
          ),
        if (state.busy)
          const LinearProgressIndicator(minHeight: 3),
      ],
    );
  }
}
