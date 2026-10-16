import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:provider/provider.dart';

import '../core/app_updater.dart';
import '../core/models.dart';
import '../providers/children_provider.dart';
import '../providers/locale_provider.dart';

Color statusColor(String status) => switch (status) {
      'OnBus' => Colors.blue,
      'AtSchool' => Colors.green,
      'DroppedHome' => Colors.teal,
      _ => Colors.grey,
    };

IconData statusIcon(String status) => switch (status) {
      'OnBus' => Icons.directions_bus,
      'AtSchool' => Icons.school,
      'DroppedHome' => Icons.home,
      _ => Icons.hourglass_bottom,
    };

class DashboardScreen extends StatefulWidget {
  const DashboardScreen({super.key});

  @override
  State<DashboardScreen> createState() => _DashboardScreenState();
}

class _DashboardScreenState extends State<DashboardScreen> {
  int? _expandedMapFor; // studentId whose live map is open

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      context.read<ChildrenProvider>().refresh();
      // Self-update check (silent unless a newer release exists).
      AppUpdater.promptIfUpdate(context);
    });
  }

  @override
  Widget build(BuildContext context) {
    final loc = context.watch<LocaleProvider>();
    final children = context.watch<ChildrenProvider>();

    return Scaffold(
      appBar: AppBar(title: Text(loc.t('my_children'))),
      body: RefreshIndicator(
        onRefresh: () => context.read<ChildrenProvider>().refresh(),
        child: children.loading && children.children.isEmpty
            ? const Center(child: CircularProgressIndicator())
            : children.error != null && children.children.isEmpty
                ? _CenteredNote(text: children.error!, icon: Icons.cloud_off,
                    onRetry: () => context.read<ChildrenProvider>().refresh(),
                    retryLabel: loc.t('refresh'))
                : children.children.isEmpty
                    ? _CenteredNote(text: loc.t('no_children'), icon: Icons.family_restroom,
                        onRetry: () => context.read<ChildrenProvider>().refresh(),
                        retryLabel: loc.t('refresh'))
                    : ListView.builder(
                        padding: const EdgeInsets.all(12),
                        itemCount: children.children.length,
                        itemBuilder: (context, i) => _ChildCardView(
                          child: children.children[i],
                          live: children.locationFor(children.children[i]),
                          mapExpanded: _expandedMapFor == children.children[i].studentId,
                          onToggleMap: () => setState(() {
                            final id = children.children[i].studentId;
                            _expandedMapFor = _expandedMapFor == id ? null : id;
                          }),
                        ),
                      ),
      ),
    );
  }
}

class _CenteredNote extends StatelessWidget {
  const _CenteredNote({required this.text, required this.icon,
      required this.onRetry, required this.retryLabel});
  final String text;
  final IconData icon;
  final VoidCallback onRetry;
  final String retryLabel;

  @override
  Widget build(BuildContext context) => ListView(
        children: [
          const SizedBox(height: 120),
          Icon(icon, size: 56, color: Colors.grey),
          const SizedBox(height: 12),
          Text(text, textAlign: TextAlign.center),
          const SizedBox(height: 12),
          Center(child: OutlinedButton(onPressed: onRetry, child: Text(retryLabel))),
        ],
      );
}

class _ChildCardView extends StatelessWidget {
  const _ChildCardView({
    required this.child,
    required this.live,
    required this.mapExpanded,
    required this.onToggleMap,
  });

  final ChildCard child;
  final BusLocation? live;
  final bool mapExpanded;
  final VoidCallback onToggleMap;

  @override
  Widget build(BuildContext context) {
    final loc = context.watch<LocaleProvider>();

    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      clipBehavior: Clip.antiAlias,
      child: Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(14),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _Avatar(name: child.fullName, photoUrl: child.photoUrl),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(child.fullName,
                          style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
                      const SizedBox(height: 4),
                      Text('${loc.t('class_lbl')}: ${child.className}   •   '
                          '${loc.t('bus_lbl')}: ${child.busNumber ?? loc.t('no_bus')}'),
                      const SizedBox(height: 10),
                      Row(
                        children: [
                          Chip(
                            avatar: Icon(statusIcon(child.todayStatus),
                                size: 18, color: Colors.white),
                            label: Text(loc.t('status_${child.todayStatus}'),
                                style: const TextStyle(color: Colors.white)),
                            backgroundColor: statusColor(child.todayStatus),
                          ),
                          if (child.lastEventTimePakistan != null) ...[
                            const SizedBox(width: 8),
                            Text(child.lastEventTimePakistan!,
                                style: const TextStyle(fontSize: 13, color: Colors.grey)),
                          ],
                        ],
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          if (child.busId != null)
            InkWell(
              onTap: onToggleMap,
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
                child: Row(
                  children: [
                    const Icon(Icons.map, size: 18),
                    const SizedBox(width: 6),
                    Text(loc.t('live_bus')),
                    const Spacer(),
                    Icon(mapExpanded ? Icons.expand_less : Icons.expand_more),
                  ],
                ),
              ),
            ),
          if (mapExpanded && child.busId != null)
            SizedBox(
              height: 220,
              child: live == null
                  ? Center(child: Text(loc.t('waiting_gps')))
                  : GoogleMap(
                      initialCameraPosition: CameraPosition(
                        target: LatLng(live!.latitude, live!.longitude),
                        zoom: 15,
                      ),
                      markers: {
                        Marker(
                          markerId: MarkerId('bus-${child.busId}'),
                          position: LatLng(live!.latitude, live!.longitude),
                          infoWindow: InfoWindow(
                              title: '${loc.t('bus_lbl')} #${child.busNumber}',
                              snippet: live!.timePakistan),
                        ),
                      },
                      myLocationButtonEnabled: false,
                      zoomControlsEnabled: false,
                    ),
            ),
        ],
      ),
    );
  }
}

class _Avatar extends StatelessWidget {
  const _Avatar({required this.name, required this.photoUrl});
  final String name;
  final String? photoUrl;

  @override
  Widget build(BuildContext context) {
    final initials = name.trim().isEmpty
        ? '?'
        : name.trim().split(RegExp(r'\s+')).take(2).map((w) => w[0]).join().toUpperCase();

    if (photoUrl != null && photoUrl!.isNotEmpty) {
      return CircleAvatar(
        radius: 30,
        backgroundImage: CachedNetworkImageProvider(photoUrl!),
        onBackgroundImageError: (_, __) {},
      );
    }
    return CircleAvatar(radius: 30, child: Text(initials, style: const TextStyle(fontSize: 20)));
  }
}
