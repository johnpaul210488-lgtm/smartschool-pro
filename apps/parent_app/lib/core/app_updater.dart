import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:open_filex/open_filex.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:path_provider/path_provider.dart';

import 'config.dart';
import 'session.dart';

/// In-app self-update: asks the school server for the newest RELEASE version
/// (the server proxies GitHub Releases). If newer → 1-tap confirm → downloads
/// the APK → Android's install prompt. Runs silently when nothing is new.
class AppUpdater {
  static bool _checkedThisSession = false;

  /// Call once after login (e.g. dashboard init). Never throws.
  static Future<void> promptIfUpdate(BuildContext context) async {
    if (_checkedThisSession) return;
    _checkedThisSession = true;
    try {
      final info = await PackageInfo.fromPlatform();
      final latest = await _fetchLatest();
      if (latest == null) return;
      final latestVersion = latest['latestVersion'] as String?;
      if (latestVersion == null || !_isNewer(latestVersion, info.version)) return;

      if (!context.mounted) return;
      final size = latest['sizeHuman'] as String? ?? '';
      final go = await showDialog<bool>(
        context: context,
        builder: (ctx) => AlertDialog(
          icon: const Icon(Icons.system_update_alt, size: 40),
          title: const Text('Update Available'),
          content: Text('Naya version $latestVersion aa gaya hai ($size).\n'
              'Abhi update karein? (app band ho kar nayi app khul jayegi)'),
          actions: [
            TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Baad mein')),
            FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Update Now')),
          ],
        ),
      );
      if (go != true || !context.mounted) return;
      await _downloadAndInstall(context);
    } catch (_) {
      // Update check is best-effort — app keeps working offline/old-server.
    }
  }

  static Future<Map<String, dynamic>?> _fetchLatest() async {
    final res = await http
        .get(
          Uri.parse('${AppConfig.apiBase}/api/app-updates/parent'),
          headers: {if (Session.token != null) 'Authorization': 'Bearer ${Session.token}'},
        )
        .timeout(const Duration(seconds: 15));
    if (res.statusCode != 200) return null;
    final env = jsonDecode(res.body) as Map<String, dynamic>;
    if (env['success'] != true) return null;
    return env['data'] as Map<String, dynamic>?;
  }

  static Future<void> _downloadAndInstall(BuildContext context) async {
    showDialog(
      context: context,
      barrierDismissible: false,
      builder: (_) => const AlertDialog(
        content: Row(
          children: [
            CircularProgressIndicator(),
            SizedBox(width: 18),
            Expanded(child: Text('Update download ho rahi hai…\n(kuch minute lag sakte hain)')),
          ],
        ),
      ),
    );

    try {
      final res = await http
          .get(
            Uri.parse('${AppConfig.apiBase}/api/app-updates/parent/download'),
            headers: {if (Session.token != null) 'Authorization': 'Bearer ${Session.token}'},
          )
          .timeout(const Duration(minutes: 10));
      if (res.statusCode != 200 || res.bodyBytes.length < 1000000) {
        throw StateError('download failed (${res.statusCode})');
      }

      final dir = await getTemporaryDirectory();
      final file = File('${dir.path}/smartschool-parent-update.apk');
      await file.writeAsBytes(res.bodyBytes, flush: true);

      if (context.mounted) Navigator.of(context, rootNavigator: true).pop();
      await OpenFilex.open(file.path);
    } catch (e) {
      if (context.mounted) {
        Navigator.of(context, rootNavigator: true).pop();
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Update fail: $e')),
        );
      }
      _checkedThisSession = false; // allow retry on next open
    }
  }

  /// Loose SemVer compare: 1.10 > 1.9.
  static bool _isNewer(String latest, String current) {
    List<int> parse(String v) => v
        .split('+').first
        .split('-').first
        .split('.')
        .map((s) => int.tryParse(s.replaceAll(RegExp('[^0-9]'), '')) ?? 0)
        .toList();
    final a = parse(latest), b = parse(current);
    for (var i = 0; i < (a.length > b.length ? a.length : b.length); i++) {
      final x = i < a.length ? a[i] : 0;
      final y = i < b.length ? b[i] : 0;
      if (x != y) return x > y;
    }
    return false;
  }
}
