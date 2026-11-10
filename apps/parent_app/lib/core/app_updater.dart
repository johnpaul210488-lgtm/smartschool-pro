import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:open_filex/open_filex.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:path_provider/path_provider.dart';

import '../config/app_config.dart';
import 'session.dart';

/// In-app self-update: asks the school server for the newest RELEASE version
/// (the server proxies GitHub Releases). If newer → 1-tap confirm → downloads
/// the APK → Android's install prompt. Runs silently when nothing is new.
class AppUpdater {
  static bool _checkedThisSession = false;

  /// Call from the home screen. Safe to call every build — guarded to once per session.
  static void promptIfUpdate(BuildContext context) {
    if (_checkedThisSession) return;
    _checkedThisSession = true;
    _run(context);
  }

  static Future<void> _run(BuildContext context) async {
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
          icon: const Icon(Icons.system_update_alt, size: 44),
          title: const Text('UPDATE AVAILABLE', style: TextStyle(fontSize: 22)),
          content: Text('Naya version $latestVersion ($size).\nAbhi install karein?',
              style: const TextStyle(fontSize: 19)),
          actions: [
            TextButton(
                onPressed: () => Navigator.pop(ctx, false),
                child: const Text('BAAD MEIN', style: TextStyle(fontSize: 18))),
            FilledButton(
                onPressed: () => Navigator.pop(ctx, true),
                child: const Text('UPDATE NOW', style: TextStyle(fontSize: 18))),
          ],
        ),
      );
      if (go != true || !context.mounted) return;
      await _downloadAndInstall(context);
    } catch (_) {
      // best-effort; ignore
    }
  }

  static Future<Map<String, dynamic>?> _fetchLatest() async {
    final token = Session.token;
    final res = await http
        .get(
          Uri.parse('${AppConfig.apiBase}/api/app-updates/parent'),
          headers: {if (token != null) 'Authorization': 'Bearer $token'},
        )
        .timeout(const Duration(seconds: 15));
    if (res.statusCode != 200) return null;
    final env = jsonDecode(res.body) as Map<String, dynamic>;
    if (env['success'] != true) return null;
    return env['data'] as Map<String, dynamic>?;
  }

  /// Streams the APK straight to disk (no whole-file-in-RAM) with a progress
  /// dialog so the user knows it is still working on slow connections.
  static Future<void> _downloadAndInstall(BuildContext context) async {
    var downloaded = 0;
    var total = 0;

    void refreshDialog() {
      final pct = total > 0 ? (downloaded * 100 ~/ total) : 0;
      showDialog(
        context: context,
        barrierDismissible: false,
        builder: (_) => AlertDialog(
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const CircularProgressIndicator(),
              const SizedBox(height: 12),
              Text('Download ho rahi hai… $pct%',
                  style: const TextStyle(fontSize: 16)),
              Text('$downloaded / $total KB',
                  style: const TextStyle(fontSize: 12, color: Colors.grey)),
            ],
          ),
        ),
      );
    }

    refreshDialog();

    try {
      final token = Session.token;
      final uri = Uri.parse('${AppConfig.apiBase}/api/app-updates/parent/download');

      // Streamed request: download directly to file, not into RAM.
      final request = http.Request('GET', uri)
        ..headers.addAll({if (token != null) 'Authorization': 'Bearer $token'});
      final streamedResponse = await http.Client().send(request).timeout(
        const Duration(minutes: 20),
      );

      if (streamedResponse.statusCode != 200) {
        throw StateError('download failed (${streamedResponse.statusCode})');
      }

      final contentLength = streamedResponse.contentLength ?? 0;
      total = (contentLength ~/ 1024);

      final dir = await getTemporaryDirectory();
      final file = File('${dir.path}/smartschool-driver-update.apk');

      var sink = file.openWrite(mode: FileMode.write);
      await for (var bytes in streamedResponse.stream) {
        await sink.add(bytes);
        downloaded += bytes.length;
        if (context.mounted) {
          refreshDialog();
        }
      }
      await sink.flush();
      await sink.close();

      if (context.mounted) Navigator.of(context, rootNavigator: true).pop();

      // Verify file size before installing.
      final stat = await file.stat();
      if (stat.size < 1_000_000) {
        throw StateError('download incomplete (${stat.size} bytes)');
      }

      await OpenFilex.open(file.path);
    } catch (e) {
      if (context.mounted) {
        Navigator.of(context, rootNavigator: true).pop();
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Update fail: $e', style: const TextStyle(fontSize: 18))),
        );
      }
      _checkedThisSession = false;
    }
  }

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
