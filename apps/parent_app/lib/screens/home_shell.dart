import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../providers/locale_provider.dart';
import '../providers/notifications_provider.dart';
import 'dashboard_screen.dart';
import 'fees_screen.dart';
import 'notifications_screen.dart';
import 'settings_screen.dart';

/// Bottom navigation: Home (children + live bus), Fees, Alerts, Settings.
class HomeShell extends StatefulWidget {
  const HomeShell({super.key, required this.onLoggedOut});
  final VoidCallback onLoggedOut;

  @override
  State<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends State<HomeShell> {
  int _index = 0;

  @override
  Widget build(BuildContext context) {
    final loc = context.watch<LocaleProvider>();
    final unseen = context.watch<NotificationsProvider>().unseenCount;

    final pages = [
      const DashboardScreen(),
      const FeesScreen(),
      const NotificationsScreen(),
      SettingsScreen(onLoggedOut: widget.onLoggedOut),
    ];

    return Scaffold(
      body: IndexedStack(index: _index, children: pages),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _index,
        onDestinationSelected: (i) => setState(() => _index = i),
        destinations: [
          NavigationDestination(icon: const Icon(Icons.home), label: loc.t('home')),
          NavigationDestination(icon: const Icon(Icons.payments), label: loc.t('fees')),
          NavigationDestination(
            icon: Badge(
              isLabelVisible: unseen > 0,
              label: Text('$unseen'),
              child: const Icon(Icons.notifications),
            ),
            label: loc.t('alerts'),
          ),
          NavigationDestination(icon: const Icon(Icons.settings), label: loc.t('settings')),
        ],
      ),
    );
  }
}
