import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../core/config.dart';
import '../providers/auth_provider.dart';
import '../providers/locale_provider.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key, this.onLoggedIn});
  final VoidCallback? onLoggedIn;

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _user = TextEditingController();
  final _pass = TextEditingController();
  bool _obscure = true;
  // Server address: phone par ek baar set hota hai aur yaad rehta hai.
  late final TextEditingController _serverCtrl =
      TextEditingController(text: AppConfig.apiBase);
  bool _showServer = false;

  @override
  void dispose() {
    _user.dispose();
    _pass.dispose();
    _serverCtrl.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final auth = context.read<AuthProvider>();
    // Login request se pehle server address save karein.
    await AppConfig.saveServerOverride(_serverCtrl.text);
    final err = await auth.login(_user.text, _pass.text);
    if (!mounted) return;
    if (err == null) {
      widget.onLoggedIn?.call();
    } else {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(err)));
    }
  }

  @override
  Widget build(BuildContext context) {
    final loc = context.watch<LocaleProvider>();
    final auth = context.watch<AuthProvider>();

    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Icon(Icons.school, size: 72, color: Colors.indigo),
                  const SizedBox(height: 12),
                  Text(loc.t('app_name'),
                      textAlign: TextAlign.center,
                      style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                          fontWeight: FontWeight.bold)),
                  const SizedBox(height: 8),
                  // Language toggle also available pre-login.
                  SegmentedButton<String>(
                    segments: [
                      ButtonSegment(value: 'en', label: Text(loc.t('english'))),
                      ButtonSegment(value: 'ur', label: Text(loc.t('urdu'))),
                    ],
                    selected: {loc.lang},
                    onSelectionChanged: (s) => loc.setLang(s.first),
                  ),
                  const SizedBox(height: 28),
                  TextField(
                    controller: _user,
                    keyboardType: TextInputType.text,
                    autofillHints: const [AutofillHints.username],
                    decoration: InputDecoration(
                      labelText: loc.t('username'),
                      prefixIcon: const Icon(Icons.person),
                      border: const OutlineInputBorder(),
                    ),
                  ),
                  const SizedBox(height: 16),
                  TextField(
                    controller: _pass,
                    obscureText: _obscure,
                    autofillHints: const [AutofillHints.password],
                    onSubmitted: (_) => _submit(),
                    decoration: InputDecoration(
                      labelText: loc.t('password'),
                      prefixIcon: const Icon(Icons.lock),
                      suffixIcon: IconButton(
                        icon: Icon(_obscure ? Icons.visibility : Icons.visibility_off),
                        onPressed: () => setState(() => _obscure = !_obscure),
                      ),
                      border: const OutlineInputBorder(),
                    ),
                  ),
                  const SizedBox(height: 10),
                  TextButton.icon(
                    onPressed: () => setState(() => _showServer = !_showServer),
                    icon: Icon(_showServer ? Icons.expand_less : Icons.dns, size: 18),
                    label: Text(_showServer ? 'Server chhupayen' : 'Server settings',
                        style: const TextStyle(fontSize: 13)),
                  ),
                  if (_showServer)
                    TextField(
                      controller: _serverCtrl,
                      keyboardType: TextInputType.url,
                      style: const TextStyle(fontSize: 16),
                      decoration: const InputDecoration(
                        labelText: 'Server (example: https://zia.tail758a5c.ts.net)',
                        prefixIcon: Icon(Icons.dns),
                        border: OutlineInputBorder(),
                      ),
                    ),
                  const SizedBox(height: 24),
                  FilledButton.icon(
                    onPressed: auth.busy ? null : _submit,
                    style: FilledButton.styleFrom(
                        padding: const EdgeInsets.symmetric(vertical: 16),
                        textStyle: const TextStyle(fontSize: 18)),
                    icon: auth.busy
                        ? const SizedBox(
                            width: 20, height: 20,
                            child: CircularProgressIndicator(strokeWidth: 2))
                        : const Icon(Icons.login),
                    label: Text(auth.busy ? loc.t('logging_in') : loc.t('login')),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
