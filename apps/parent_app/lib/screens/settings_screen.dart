import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../core/config.dart';
import '../core/session.dart';
import '../providers/auth_provider.dart';
import '../providers/locale_provider.dart';
import '../providers/settings_provider.dart';

class SettingsScreen extends StatefulWidget {
  const SettingsScreen({super.key, required this.onLoggedOut});
  final VoidCallback onLoggedOut;

  @override
  State<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends State<SettingsScreen> {
  final _phone = TextEditingController();

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      await context.read<SettingsProvider>().refresh();
      if (mounted) _phone.text = context.read<SettingsProvider>().settings?.phone ?? '';
    });
  }

  @override
  void dispose() {
    _phone.dispose();
    super.dispose();
  }

  Future<void> _savePhone() async {
    final settings = context.read<SettingsProvider>();
    final err = await settings.save(phone: _phone.text.trim());
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(err ?? context.read<LocaleProvider>().t('saved'))));
  }

  @override
  Widget build(BuildContext context) {
    final loc = context.watch<LocaleProvider>();
    final settings = context.watch<SettingsProvider>();
    final s = settings.settings;

    return Scaffold(
      appBar: AppBar(title: Text(loc.t('settings'))),
      body: s == null
          ? const Center(child: CircularProgressIndicator())
          : ListView(
              padding: const EdgeInsets.all(16),
              children: [
                // Account header
                ListTile(
                  leading: const CircleAvatar(child: Icon(Icons.person)),
                  title: Text(Session.fullName ?? s.username,
                      style: const TextStyle(fontWeight: FontWeight.bold)),
                  subtitle: Text('@${s.username}'),
                ),
                const Divider(),

                // Language
                SwitchListTile(
                  secondary: const Icon(Icons.language),
                  title: Text(loc.t('language')),
                  subtitle: Text(loc.isUrdu ? loc.t('urdu') : loc.t('english')),
                  value: loc.isUrdu,
                  onChanged: (urdu) async {
                    await loc.setLang(urdu ? 'ur' : 'en');
                    // Push preference so server-side messages follow the app.
                    await context
                        .read<SettingsProvider>()
                        .save(preferredLanguage: urdu ? 'ur' : 'en');
                  },
                ),

                // WhatsApp alerts toggle
                SwitchListTile(
                  secondary: const Icon(Icons.chat),
                  title: Text(loc.t('whatsapp_alerts')),
                  value: s.receiveWhatsApp,
                  onChanged: (v) async {
                    final err = await context
                        .read<SettingsProvider>()
                        .save(receiveWhatsApp: v);
                    if (err != null && context.mounted) {
                      ScaffoldMessenger.of(context)
                          .showSnackBar(SnackBar(content: Text(err)));
                    }
                  },
                ),

                // Phone number
                Padding(
                  padding: const EdgeInsets.only(left: 16, right: 8),
                  child: TextField(
                    controller: _phone,
                    keyboardType: TextInputType.phone,
                    decoration: InputDecoration(
                      labelText: loc.t('phone_number'),
                      prefixIcon: const Icon(Icons.phone),
                      suffixIcon: IconButton(
                        icon: settings.saving
                            ? const SizedBox(
                                width: 18,
                                height: 18,
                                child: CircularProgressIndicator(strokeWidth: 2))
                            : const Icon(Icons.save),
                        onPressed: settings.saving ? null : _savePhone,
                      ),
                      helperText: '03001234567',
                      border: const OutlineInputBorder(),
                    ),
                  ),
                ),

                const SizedBox(height: 24),
                OutlinedButton.icon(
                  onPressed: () async {
                    await context.read<AuthProvider>().logout();
                    widget.onLoggedOut();
                  },
                  icon: const Icon(Icons.logout, color: Colors.red),
                  label: Text(loc.t('logout'), style: const TextStyle(color: Colors.red)),
                ),

                const SizedBox(height: 16),
                Text('${loc.t('server')}: ${AppConfig.apiBase}',
                    textAlign: TextAlign.center,
                    style: const TextStyle(color: Colors.grey, fontSize: 12)),
              ],
            ),
    );
  }
}
