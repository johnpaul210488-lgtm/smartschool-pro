import 'package:flutter/material.dart';

import '../../providers/app_state.dart';

/// Top banners: offline bar + outbox counter + flash message (auto-hides).
class StatusBanners extends StatelessWidget {
  const StatusBanners({super.key, required this.state});
  final AppState state;

  @override
  Widget build(BuildContext context) {
    // Auto-clear flash after a moment
    if (state.lastFlash != null) {
      final msg = state.lastFlash!;
      Future.delayed(const Duration(seconds: 3), () {
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
