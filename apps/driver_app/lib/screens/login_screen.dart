import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../providers/app_state.dart';

/// Simple, big-target login for drivers (phone = username).
class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _userCtrl = TextEditingController(text: 'driver1');
  final _passCtrl = TextEditingController();
  bool _obscure = true;

  @override
  void dispose() {
    _userCtrl.dispose();
    _passCtrl.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final state = context.read<AppState>();
    final ok = await state.doLogin(_userCtrl.text, _passCtrl.text);
    if (ok && mounted) {
      // HomeScreen appears automatically via Consumer in main.dart
    }
  }

  @override
  Widget build(BuildContext context) {
    final state = context.watch<AppState>();
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(28),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Icon(Icons.directions_bus_rounded, size: 82, color: Color(0xFF7C4DFF)),
                  const SizedBox(height: 10),
                  const Text('SmartSchool Driver',
                      textAlign: TextAlign.center,
                      style: TextStyle(fontSize: 28, fontWeight: FontWeight.bold)),
                  const Text('Bus Driver Login',
                      textAlign: TextAlign.center,
                      style: TextStyle(fontSize: 16, color: Colors.black54)),
                  const SizedBox(height: 32),
                  TextField(
                    controller: _userCtrl,
                    style: const TextStyle(fontSize: 20),
                    decoration: const InputDecoration(
                      labelText: 'Phone / Username',
                      prefixIcon: Icon(Icons.person),
                      border: OutlineInputBorder(),
                    ),
                  ),
                  const SizedBox(height: 16),
                  TextField(
                    controller: _passCtrl,
                    obscureText: _obscure,
                    style: const TextStyle(fontSize: 20),
                    decoration: InputDecoration(
                      labelText: 'Password',
                      prefixIcon: const Icon(Icons.lock),
                      border: const OutlineInputBorder(),
                      suffixIcon: IconButton(
                        icon: Icon(_obscure ? Icons.visibility : Icons.visibility_off),
                        onPressed: () => setState(() => _obscure = !_obscure),
                      ),
                    ),
                    onSubmitted: (_) => _submit(),
                  ),
                  const SizedBox(height: 22),
                  if (state.error != null)
                    Container(
                      padding: const EdgeInsets.all(12),
                      margin: const EdgeInsets.only(bottom: 16),
                      decoration: BoxDecoration(
                        color: Colors.red.shade50,
                        border: Border.all(color: Colors.red.shade300),
                        borderRadius: BorderRadius.circular(8),
                      ),
                      child: Text(state.error!,
                          style: TextStyle(color: Colors.red.shade800, fontSize: 16)),
                    ),
                  FilledButton.icon(
                    onPressed: state.busy ? null : _submit,
                    icon: const Icon(Icons.login, size: 28),
                    label: Padding(
                      padding: const EdgeInsets.symmetric(vertical: 16),
                      child: Text(state.busy ? 'WAIT…' : 'LOGIN',
                          style: const TextStyle(fontSize: 22, letterSpacing: 1)),
                    ),
                    style: FilledButton.styleFrom(
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                    ),
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
