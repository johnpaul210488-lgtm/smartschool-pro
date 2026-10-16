import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'core/config.dart';
import 'core/fcm_service.dart';
import 'core/signalr_service.dart';
import 'providers/auth_provider.dart';
import 'providers/children_provider.dart';
import 'providers/fees_provider.dart';
import 'providers/locale_provider.dart';
import 'providers/notifications_provider.dart';
import 'providers/settings_provider.dart';
import 'screens/home_shell.dart';
import 'screens/login_screen.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const ParentApp());
}

class ParentApp extends StatelessWidget {
  const ParentApp({super.key});

  @override
  Widget build(BuildContext context) {
    final auth = AuthProvider();
    return MultiProvider(
      providers: [
        ChangeNotifierProvider(create: (_) => LocaleProvider()),
        ChangeNotifierProvider.value(value: auth),
        Provider.value(value: auth.api),
        ChangeNotifierProvider(create: (_) => ChildrenProvider(auth.api)),
        ChangeNotifierProvider(create: (_) => FeesProvider(auth.api)),
        ChangeNotifierProvider(create: (_) => NotificationsProvider(auth.api)),
        ChangeNotifierProvider(create: (_) => SettingsProvider(auth.api)),
      ],
      child: const _Root(),
    );
  }
}

class _Root extends StatefulWidget {
  const _Root();

  @override
  State<_Root> createState() => _RootState();
}

class _RootState extends State<_Root> {
  final _messengerKey = GlobalKey<ScaffoldMessengerState>();
  SignalRService? _signalR;
  bool _booted = false;
  bool _loggedIn = false;
  String? _lastPushKey;

  @override
  void initState() {
    super.initState();
    _boot();
    // Foreground FCM messages → in-app banner.
    FcmService.instance.lastMessage.addListener(_showForegroundPush);
  }

  @override
  void dispose() {
    FcmService.instance.lastMessage.removeListener(_showForegroundPush);
    super.dispose();
  }

  void _showForegroundPush() {
    final msg = FcmService.instance.lastMessage.value;
    final key = '${msg?.sentTime}-${msg?.messageId}';
    if (msg == null || key == _lastPushKey) return;
    _lastPushKey = key;
    final title = msg.notification?.title ?? msg.data['title'] ?? 'SmartSchool';
    final body = msg.notification?.body ?? msg.data['body'] ?? '';
    _messengerKey.currentState?.showSnackBar(SnackBar(
      content: Text('$title\n$body'),
      behavior: SnackBarBehavior.floating,
      duration: const Duration(seconds: 5),
    ));
    context.read<ChildrenProvider>().refresh();
  }

  Future<void> _boot() async {
    final loc = context.read<LocaleProvider>();
    final auth = context.read<AuthProvider>();
    await loc.load();
    await AppConfig.loadServerOverride();
    await auth.restore();
    _loggedIn = auth.isLoggedIn;
    if (_loggedIn) _startLive();
    if (mounted) setState(() => _booted = true);
  }

  void _startLive() {
    final children = context.read<ChildrenProvider>();
    final notif = context.read<NotificationsProvider>();
    final api = context.read<AuthProvider>().api;

    _signalR ??= SignalRService(
      onAlert: (_, student) {
        children.onBusEvent();
        notif.bump();
      },
      onBusLocation: children.onBusLocation,
      onAnnouncement: notif.bump,
    );
    _signalR!.start();

    children.refresh().then((_) {
      notif.refresh(children.children.map((c) => c.studentId).toList());
    });
    context.read<FeesProvider>().refresh();
    context.read<SettingsProvider>().refresh();
    FcmService.instance.init(api);
  }

  void _onLoggedIn() => setState(() {
        _loggedIn = true;
        _startLive();
      });

  Future<void> _onLoggedOut() async {
    await _signalR?.stop();
    _signalR = null;
    setState(() => _loggedIn = false);
  }

  @override
  Widget build(BuildContext context) {
    final loc = context.watch<LocaleProvider>();

    return MaterialApp(
      title: loc.t('app_name'),
      debugShowCheckedModeBanner: false,
      scaffoldMessengerKey: _messengerKey,
      theme: ThemeData(
        useMaterial3: true,
        colorScheme: ColorScheme.fromSeed(seedColor: Colors.indigo),
      ),
      home: !_booted
          ? const Scaffold(body: Center(child: CircularProgressIndicator()))
          : _loggedIn
              ? HomeShell(onLoggedOut: _onLoggedOut)
              : LoginScreen(onLoggedIn: _onLoggedIn),
    );
  }
}
