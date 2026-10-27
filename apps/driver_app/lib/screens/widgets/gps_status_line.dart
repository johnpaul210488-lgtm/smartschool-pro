import 'dart:async';

import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Trip ke dauran dikhata hai ke background GPS server ko location bhej raha hai ya nahi.
/// Background service har push ka nateeja SharedPreferences ('gps_status') mein likhti hai.
class GpsStatusLine extends StatefulWidget {
  const GpsStatusLine({super.key});

  @override
  State<GpsStatusLine> createState() => _GpsStatusLineState();
}

class _GpsStatusLineState extends State<GpsStatusLine> {
  String _status = 'GPS: abhi tak koi push nahi hua (15 second intezar karein)';
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _read();
    _timer = Timer.periodic(const Duration(seconds: 3), (_) => _read());
  }

  Future<void> _read() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.reload();
    final s = prefs.getString('gps_status');
    if (!mounted || s == null) return;
    setState(() => _status = 'GPS: $s');
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final isErr = _status.contains('ERR');
    return Container(
      margin: const EdgeInsets.only(top: 12),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: isErr ? Colors.red.shade50 : Colors.green.shade50,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: isErr ? Colors.red : Colors.green),
      ),
      child: Row(children: [
        Icon(isErr ? Icons.gps_off : Icons.gps_fixed,
            color: isErr ? Colors.red : Colors.green),
        const SizedBox(width: 10),
        Expanded(
          child: Text(_status, style: const TextStyle(fontSize: 15)),
        ),
      ]),
    );
  }
}
