import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../providers/app_state.dart';
import '../services/app_updater.dart';
import '../services/tts_service.dart';
import 'trip_screen.dart';
import 'widgets/gps_status_line.dart';
import 'widgets/status_banner.dart';

/// Home: assigned bus card + big START/END TRIP button + manifest preview.
class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final state = context.watch<AppState>();
    final bus = state.bus;
    final trip = state.activeTrip;

    // Self-update check (guarded to once per session, fully silent if none).
    WidgetsBinding.instance.addPostFrameCallback(
        (_) => AppUpdater.promptIfUpdate(context, state.api));

    return Scaffold(
      appBar: AppBar(
        title: const Text('SmartSchool Driver', style: TextStyle(fontWeight: FontWeight.bold)),
        actions: [
          // Online/offline chip
          Padding(
            padding: const EdgeInsets.only(right: 4),
            child: Chip(
              avatar: Icon(state.online ? Icons.wifi : Icons.wifi_off,
                  size: 18, color: Colors.white),
              label: Text(state.online ? 'Online' : 'Offline',
                  style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
              backgroundColor: state.online ? Colors.green : Colors.red,
            ),
          ),
          IconButton(
            tooltip: 'Voice: ON/OFF',
            onPressed: () {
              TtsService.instance.enabled = !TtsService.instance.enabled;
              ScaffoldMessenger.of(context).showSnackBar(SnackBar(
                content: Text(
                    TtsService.instance.enabled ? '🔊 Voice ON' : '🔇 Voice OFF',
                    style: const TextStyle(fontSize: 18)),
                duration: const Duration(seconds: 1),
              ));
            },
            icon: Icon(TtsService.instance.enabled ? Icons.volume_up : Icons.volume_off),
          ),
          IconButton(
            tooltip: 'Refresh',
            onPressed: state.busy ? null : () => context.read<AppState>().refreshBusAndManifest(),
            icon: const Icon(Icons.refresh),
          ),
          IconButton(
            tooltip: 'Logout',
            onPressed: () => _confirmLogout(context),
            icon: const Icon(Icons.logout),
          ),
        ],
      ),
      body: SafeArea(
        child: state.error != null && bus == null
            ? _ErrorState(message: state.error!)
            : RefreshIndicator(
                onRefresh: () => context.read<AppState>().refreshBusAndManifest(),
                child: ListView(
                  padding: const EdgeInsets.all(16),
                  children: [
                    StatusBanners(state: state),

                    // ---- Bus card ----
                    if (bus != null)
                      Card(
                        elevation: 3,
                        shape:
                            RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
                        child: Padding(
                          padding: const EdgeInsets.all(20),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              Row(children: [
                                const Icon(Icons.directions_bus_filled,
                                    size: 40, color: Color(0xFF7C4DFF)),
                                const SizedBox(width: 14),
                                Expanded(
                                  child: Text('Bus #${bus.busNumber}',
                                      style: const TextStyle(
                                          fontSize: 28, fontWeight: FontWeight.bold)),
                                ),
                              ]),
                              const SizedBox(height: 8),
                              Text(bus.route,
                                  style: const TextStyle(fontSize: 18, color: Colors.black87)),
                              const SizedBox(height: 6),
                              Text('${bus.vehicleNo}  •  ${bus.studentCount} students',
                                  style: const TextStyle(fontSize: 16, color: Colors.black54)),
                            ],
                          ),
                        ),
                      ),

                    const SizedBox(height: 18),

                    // ---- GPS status (sirf trip ke dauran) ----
                    if (trip != null) const GpsStatusLine(),

                    // ---- Trip button ----
                    if (trip == null)
                      FilledButton.icon(
                        onPressed: (bus == null || state.busy)
                            ? null
                            : () => _startTrip(context),
                        icon: const Icon(Icons.play_arrow_rounded, size: 34),
                        label: const Padding(
                          padding: EdgeInsets.symmetric(vertical: 20),
                          child: Text('START TRIP',
                              style: TextStyle(fontSize: 24, letterSpacing: 1.2)),
                        ),
                        style: FilledButton.styleFrom(
                          backgroundColor: Colors.green.shade700,
                          shape: RoundedRectangleBorder(
                              borderRadius: BorderRadius.circular(14)),
                        ),
                      )
                    else
                      Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          Container(
                            padding: const EdgeInsets.all(14),
                            decoration: BoxDecoration(
                              color: Colors.green.shade50,
                              border: Border.all(color: Colors.green.shade300),
                              borderRadius: BorderRadius.circular(12),
                            ),
                            child: Row(children: [
                              const Icon(Icons.radio_button_checked,
                                  color: Colors.red, size: 26),
                              const SizedBox(width: 10),
                              Expanded(
                                child: Text(
                                  'TRIP #${trip.id} ACTIVE — GPS live',
                                  style: TextStyle(
                                      fontSize: 18,
                                      fontWeight: FontWeight.bold,
                                      color: Colors.green.shade800),
                                ),
                              ),
                            ]),
                          ),
                          const SizedBox(height: 14),
                          // Go to student list
                          FilledButton.icon(
                            onPressed: () => Navigator.of(context).push(
                                MaterialPageRoute(builder: (_) => const TripScreen())),
                            icon: const Icon(Icons.groups, size: 32),
                            label: const Padding(
                              padding: EdgeInsets.symmetric(vertical: 20),
                              child: Text('STUDENT LIST (PICK/DROP)',
                                  style: TextStyle(fontSize: 20)),
                            ),
                            style: FilledButton.styleFrom(
                              shape: RoundedRectangleBorder(
                                  borderRadius: BorderRadius.circular(14)),
                            ),
                          ),
                          const SizedBox(height: 12),
                          OutlinedButton.icon(
                            onPressed: () => _endTrip(context),
                            icon: const Icon(Icons.stop_circle_outlined, size: 30),
                            label: const Padding(
                              padding: EdgeInsets.symmetric(vertical: 16),
                              child: Text('END TRIP', style: TextStyle(fontSize: 20)),
                            ),
                            style: OutlinedButton.styleFrom(
                              foregroundColor: Colors.red.shade700,
                              side: BorderSide(color: Colors.red.shade300),
                              shape: RoundedRectangleBorder(
                                  borderRadius: BorderRadius.circular(14)),
                            ),
                          ),
                        ],
                      ),

                    const SizedBox(height: 18),

                    // ---- Manifest preview ----
                    if (bus != null) ...[
                      Text('Bus ka safar-list (${state.students.length})',
                          style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
                      const SizedBox(height: 6),
                      ...state.students.take(8).map((s) => ListTile(
                            dense: true,
                            leading: _statusIcon(s.status),
                            title: Text(s.fullName, style: const TextStyle(fontSize: 17)),
                            subtitle: Text(s.className, style: const TextStyle(fontSize: 14)),
                          )),
                      if (state.students.length > 8)
                        Padding(
                          padding: const EdgeInsets.only(left: 16, top: 4),
                          child: Text('… aur ${state.students.length - 8} students',
                              style: const TextStyle(color: Colors.black54)),
                        ),
                    ],
                  ],
                ),
              ),
      ),
    );
  }

  Widget _statusIcon(status) => switch (status.toString()) {
        'PickStatus.picked' => const Icon(Icons.check_circle, color: Colors.green),
        'PickStatus.dropped' => const Icon(Icons.flag, color: Colors.orange),
        _ => const Icon(Icons.circle_outlined, color: Colors.grey),
      };

  Future<void> _startTrip(BuildContext context) async {
    final state = context.read<AppState>();
    final ok = await state.startTrip();
    if (!context.mounted) return;
    if (ok) {
      Navigator.of(context).push(MaterialPageRoute(builder: (_) => const TripScreen()));
    } else {
      // Pehle error screen par nahi dikhta tha: ab wajah saaf dikhayi jati hai.
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        duration: const Duration(seconds: 8),
        backgroundColor: Colors.red.shade800,
        content: Text(state.error ?? 'Trip shuru nahi ho saka. Dobara koshish karein.',
            style: const TextStyle(fontSize: 15)),
      ));
    }
  }

  Future<void> _endTrip(BuildContext context) async {
    final sure = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('Trip khatam?'),
        content: const Text('Tracking band ho jayegi. Sab students drop ho chuke hain?'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(c, false), child: const Text('NAHI')),
          FilledButton(onPressed: () => Navigator.pop(c, true), child: const Text('HAAN, END')),
        ],
      ),
    );
    if (sure == true && context.mounted) await context.read<AppState>().endTrip();
  }

  void _confirmLogout(BuildContext context) async {
    final state = context.read<AppState>();
    if (state.activeTrip != null) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text('⚠️ Pehle trip END karein, phir logout.',
              style: TextStyle(fontSize: 16))));
      return;
    }
    await state.doLogout();
  }
}

class _ErrorState extends StatelessWidget {
  const _ErrorState({required this.message});
  final String message;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(28),
        child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
          const Icon(Icons.error_outline, size: 64, color: Colors.redAccent),
          const SizedBox(height: 14),
          Text(message,
              textAlign: TextAlign.center,
              style: const TextStyle(fontSize: 18, color: Colors.black87)),
          const SizedBox(height: 18),
          FilledButton.icon(
            onPressed: () => context.read<AppState>().refreshBusAndManifest(),
            icon: const Icon(Icons.refresh),
            label: const Text('RETRY', style: TextStyle(fontSize: 18)),
          ),
        ]),
      ),
    );
  }
}
