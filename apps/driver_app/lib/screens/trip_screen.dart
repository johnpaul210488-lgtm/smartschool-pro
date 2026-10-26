import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../models/models.dart';
import '../providers/app_state.dart';
import 'widgets/status_banner.dart';

/// Trip screen: one big card per student with PICK-IN / DROP-OUT buttons.
/// Designed for use while driving: huge hit targets, loud colors, voice.
class TripScreen extends StatelessWidget {
  const TripScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final state = context.watch<AppState>();
    final trip = state.activeTrip;

    if (trip == null) {
      return Scaffold(
        appBar: AppBar(title: const Text('Students')),
        body: const Center(
          child: Padding(
            padding: EdgeInsets.all(24),
            child: Text('Koi active trip nahi. Home par START TRIP dabaein.',
                textAlign: TextAlign.center, style: TextStyle(fontSize: 20)),
          ),
        ),
      );
    }

    final picked = state.students.where((s) => s.status != PickStatus.none).length;

    return Scaffold(
      appBar: AppBar(
        title: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Bus #${trip.busNumber} — Trip #${trip.id}',
                style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
            Text('$picked/${state.students.length} done',
                style: const TextStyle(fontSize: 13, color: Colors.white70)),
          ],
        ),
        actions: [
          IconButton(
            tooltip: 'Refresh list',
            onPressed: () => context.read<AppState>().loadManifest(),
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: SafeArea(
        child: Column(
          children: [
            StatusBanners(state: state),
            Expanded(
              child: state.students.isEmpty
                  ? const Center(child: Text('Is bus mein koi student nahi.',
                      style: TextStyle(fontSize: 20, color: Colors.black54)))
                  : ListView.builder(
                      padding: const EdgeInsets.all(12),
                      itemCount: state.students.length,
                      itemBuilder: (_, i) => _StudentCard(student: state.students[i]),
                    ),
            ),
          ],
        ),
      ),
    );
  }
}

class _StudentCard extends StatelessWidget {
  const _StudentCard({required this.student});
  final ManifestStudent student;

  @override
  Widget build(BuildContext context) {
    final state = context.read<AppState>();

    return Card(
      elevation: 2,
      margin: const EdgeInsets.only(bottom: 12),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          children: [
            Row(
              children: [
                // Photo
                CircleAvatar(
                  radius: 34,
                  backgroundColor: const Color(0xFFEDE7F6),
                  backgroundImage: student.photoUrl != null
                      ? CachedNetworkImageProvider(student.photoUrl!)
                      : null,
                  child: student.photoUrl == null
                      ? Text(
                          student.fullName.isNotEmpty ? student.fullName[0].toUpperCase() : '?',
                          style: const TextStyle(
                              fontSize: 28, fontWeight: FontWeight.bold, color: Color(0xFF7C4DFF)),
                        )
                      : null,
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(student.fullName,
                          style: const TextStyle(fontSize: 21, fontWeight: FontWeight.bold)),
                      Text(student.className,
                          style: const TextStyle(fontSize: 15, color: Colors.black54)),
                      if (student.address.isNotEmpty)
                        Text('📍 ${student.address}',
                            maxLines: 2, overflow: TextOverflow.ellipsis,
                            style: const TextStyle(fontSize: 14, color: Colors.black87)),
                      if (student.status == PickStatus.picked)
                        const Text('✅ PICKED', style: TextStyle(
                            fontSize: 15, fontWeight: FontWeight.bold, color: Colors.green)),
                      if (student.status == PickStatus.dropped)
                        const Text('🏁 DROPPED', style: TextStyle(
                            fontSize: 15, fontWeight: FontWeight.bold, color: Colors.orange)),
                    ],
                  ),
                ),
                IconButton(
                  tooltip: 'Father ko call',
                  onPressed: () {
                    // url_launcher avoided to keep deps lean — yahan sirf dialog
                    showDialog(
                      context: context,
                      builder: (c) => AlertDialog(
                        title: Text(student.fatherName),
                        content: Text('📞 ${student.fatherPhone}',
                            style: const TextStyle(fontSize: 24, fontWeight: FontWeight.bold)),
                        actions: [
                          TextButton(onPressed: () => Navigator.pop(c), child: const Text('BAND')),
                        ],
                      ),
                    );
                  },
                  icon: const Icon(Icons.phone, size: 30, color: Color(0xFF7C4DFF)),
                ),
              ],
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                // PICK-IN
                Expanded(
                  child: AnimatedOpacity(
                    opacity: student.canPickIn ? 1 : 0.45,
                    duration: const Duration(milliseconds: 200),
                    child: ElevatedButton.icon(
                      onPressed: student.canPickIn && !state.busy
                          ? () => state.recordEvent(student, 'pick-in')
                          : null,
                      icon: Icon(
                          student.status == PickStatus.none ? Icons.add_location_alt : Icons.check,
                          size: 30),
                      label: Padding(
                        padding: const EdgeInsets.symmetric(vertical: 18),
                        child: Text(
                          student.status == PickStatus.none ? 'PICK-IN' : 'PICKED ✅',
                          style: const TextStyle(fontSize: 19, fontWeight: FontWeight.bold),
                        ),
                      ),
                      style: ElevatedButton.styleFrom(
                        backgroundColor: Colors.green.shade700,
                        foregroundColor: Colors.white,
                        disabledBackgroundColor: Colors.green.shade200,
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                      ),
                    ),
                  ),
                ),
                const SizedBox(width: 12),
                // DROP-OUT
                Expanded(
                  child: AnimatedOpacity(
                    opacity: student.canDropOut ? 1 : 0.45,
                    duration: const Duration(milliseconds: 200),
                    child: ElevatedButton.icon(
                      onPressed: student.canDropOut && !state.busy
                          ? () => state.recordEvent(student, 'drop-out')
                          : null,
                      icon: Icon(
                          student.status == PickStatus.dropped ? Icons.flag : Icons.outlined_flag,
                          size: 30),
                      label: Padding(
                        padding: const EdgeInsets.symmetric(vertical: 18),
                        child: Text(
                          student.status == PickStatus.dropped ? 'DROPPED ✅' : 'DROP-OUT',
                          style: const TextStyle(fontSize: 19, fontWeight: FontWeight.bold),
                        ),
                      ),
                      style: ElevatedButton.styleFrom(
                        backgroundColor: Colors.red.shade700,
                        foregroundColor: Colors.white,
                        disabledBackgroundColor: Colors.red.shade200,
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
