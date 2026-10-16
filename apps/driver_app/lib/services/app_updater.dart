import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:open_filex/open_filex.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:path_provider/path_provider.dart';

import '../config/app_config.dart';
import 'api_client.dart';

/// In-app self-update: asks the school server for the newest RELEASE version
/// (the server proxies GitHub Releases). If newer → 1-tap confirm → downloads
/// the APK → Android's install prompt. Runs silently when nothing is new.
class AppUpdater {
  static bool _checkedThisSession = false;

  /// Call from the home screen. Safe to call every build — guarded to once per session.
  static void promptIfUpdate(BuildContext context, ApiClient api) {
    if (_checkedThisSession) return;
    _checkedThisSession = true;
    _run(context, api);
  }

  static Future<void> _run(BuildContext context, ApiClient api) async {
    try {
      final info = await PackageInfo.fromPlatform();
      final latest = await _fetchLatest(api);
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
      await _downloadAndInstall(context, api);
    } catch (_) {
      // best-effort; ignore
    }
  }

  static Future<Map<String, dynamic>?> _fetchLatest(ApiClient api) async {
    final token = api.currentLogin?.token;
    final res = await http
        .get(
          Uri.parse('${AppConfig.apiBaseUrl}/api/app-updates/driver'),
          headers: {if (token != null) 'Authorization': 'Bearer $token'},
        )
        .timeout(const Duration(seconds: 15));
    if (res.statusCode != 200) return null;
    final env = jsonDecode(res.body) as Map<String, dynamic>;
    if (env['success'] != true) return null;
    return env['data'] as Map<String, dynamic>?;
  }

  static Future<void> _downloadAndInstall(BuildContext context, ApiClient api) async {
    showDialog(
      context: context,
      barrierDismissible: false,
      builder: (_) => const AlertDialog(
        content: Row(
          children: [
            CircularProgressIndicator(),
            SizedBox(width: 18),
            Expanded(
                child: Text('Download ho rahi hai…\nWi-Fi par karein',
                    style: TextStyle(fontSize: 19))),
          ],
        ),
      ),
    );

    try {
      final token = api.currentLogin?.token;
      final res = await http
          .get(
            Uri.parse('${AppConfig.apiBaseUrl}/api/app-updates/driver/download'),
            headers: {if (token != null) 'Authorization': 'Bearer $token'},
          )
          .timeout(const Duration(minutes: 10));
      if (res.statusCode != 200 || res.bodyBytes.length < 1000000) {
        throw StateError('download failed (${res.statusCode})');
      }

      final dir = await getTemporaryDirectory();
      final file = File('${dir.path}/smartschool-driver-update.apk');
      await file.writeAsBytes(res.bodyBytes, flush: true);

      if (context.mounted) Navigator.of(context, rootNavigator: true).pop();
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
