import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'providers/app_state.dart';
import 'screens/home_screen.dart';
import 'screens/login_screen.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(
    ChangeNotifierProvider(
      create: (_) => AppState()..boot(),
      child: const SmartSchoolDriverApp(),
    ),
  );
}

class SmartSchoolDriverApp extends StatelessWidget {
  const SmartSchoolDriverApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Queen International School Driver',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        useMaterial3: true,
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFF7C4DFF),
          brightness: Brightness.light,
        ),
        textTheme: const TextTheme(
          bodyLarge: TextStyle(fontSize: 18),
          bodyMedium: TextStyle(fontSize: 18),
        ),
      ),
      home: Consumer<AppState>(
        builder: (_, state, __) => switch (state.authStatus) {
          AuthStatus.loggedIn => const HomeScreen(),
          AuthStatus.loggedOut => const LoginScreen(),
          AuthStatus.unknown => const Scaffold(
              body: Center(child: CircularProgressIndicator()),
            ),
        },
      ),
    );
  }
}
