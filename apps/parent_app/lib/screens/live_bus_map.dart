import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart' as ll;

import '../core/models.dart';

/// Bus ka live map (OpenStreetMap, bina API key). Bus ke har update par map us ke pass rehta hai
/// (follow mode). User drag/zoom kare to follow band; "Bus par jao" button se wapas.
class LiveBusMap extends StatefulWidget {
  const LiveBusMap({super.key, required this.loc, required this.title, this.fullscreen = false});

  final BusLocation loc;
  final String title;
  final bool fullscreen;

  @override
  State<LiveBusMap> createState() => _LiveBusMapState();
}

class _LiveBusMapState extends State<LiveBusMap> {
  final _controller = MapController();
  bool _follow = true;

  ll.LatLng get _point => ll.LatLng(widget.loc.latitude, widget.loc.longitude);

  @override
  void didUpdateWidget(covariant LiveBusMap oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (_follow && (oldWidget.loc.latitude != widget.loc.latitude || oldWidget.loc.longitude != widget.loc.longitude)) {
      _controller.move(_point, _controller.camera.zoom);
    }
  }

  void _openFullscreen() {
    Navigator.of(context).push(MaterialPageRoute(
      builder: (_) => Scaffold(
        appBar: AppBar(title: Text(widget.title)),
        body: LiveBusMap(loc: widget.loc, title: widget.title, fullscreen: true),
      ),
    ));
  }

  @override
  Widget build(BuildContext context) {
    final loc = widget.loc;
    final busLabel = '🚌 #${loc.busNumber ?? loc.busId}';
    final speed = loc.speedKmh == null ? '' : '  ${loc.speedKmh!.toStringAsFixed(0)} km/h';
    final driver = (loc.driverName ?? '').trim();

    return Stack(
      children: [
        FlutterMap(
          mapController: _controller,
          options: MapOptions(
            initialCenter: _point,
            initialZoom: 16,
            interactionOptions: const InteractionOptions(
              flags: InteractiveFlag.all,
            ),
            onPositionChanged: (camera, hasGesture) {
              if (hasGesture && _follow) setState(() => _follow = false);
            },
          ),
          children: [
            TileLayer(
              urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
              userAgentPackageName: 'smartschool_parent',
            ),
            MarkerLayer(markers: [
              Marker(
                point: _point,
                width: 200,
                height: 80,
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                      decoration: BoxDecoration(
                        color: const Color(0xFFFFC107),
                        borderRadius: BorderRadius.circular(8),
                        border: Border.all(color: Colors.black54),
                      ),
                      child: Text(
                        '$busLabel${driver.isEmpty ? '' : ' • $driver'}$speed',
                        style: const TextStyle(fontSize: 12, fontWeight: FontWeight.bold),
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                    const Icon(Icons.directions_bus, color: Color(0xFFFFA000), size: 34),
                  ],
                ),
              ),
            ]),
            RichAttributionWidget(
              attributions: [TextSourceAttribution('© OpenStreetMap contributors')],
            ),
          ],
        ),
        Positioned(
          right: 8,
          top: 8,
          child: Column(children: [
            if (!widget.fullscreen)
              FloatingActionButton.small(
                heroTag: null,
                tooltip: 'Bari screen par dekhein',
                onPressed: _openFullscreen,
                child: const Icon(Icons.fullscreen),
              ),
            const SizedBox(height: 6),
            FloatingActionButton.small(
              heroTag: null,
              tooltip: 'Bus par jao',
              onPressed: () {
                setState(() => _follow = true);
                _controller.move(_point, _controller.camera.zoom);
              },
              child: const Icon(Icons.my_location),
            ),
          ]),
        ),
      ],
    );
  }
}
