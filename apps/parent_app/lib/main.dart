import 'dart:async';

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

class _RootState extends State<_Root> with WidgetsBindingObserver {
  final _messengerKey = GlobalKey<ScaffoldMessengerState>();
  final Set<String> _shownAlertKeys = <String>{};
  SignalRService? _signalR;
  bool _booted = false;
  bool _loggedIn = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _boot();
    // Foreground FCM messages → in-app banner.
    FcmService.instance.lastMessage.addListener(_showForegroundPush);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    FcmService.instance.lastMessage.removeListener(_showForegroundPush);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed && _loggedIn && mounted) {
      final auth = context.read<AuthProvider>();
      if (auth.loggingOut || !auth.isLoggedIn) return;
      // Retry failed registration and re-check notification permission after the
      // parent returns from Android/iOS settings.
      unawaited(FcmService.instance.init(auth.api));
    }
  }

  bool _showBanner(String? key, String title, String body) {
    final messenger = _messengerKey.currentState;
    if (!_loggedIn || messenger == null) return false;
    if (key != null && !_shownAlertKeys.add(key)) return false;
    if (_shownAlertKeys.length > 100) _shownAlertKeys.remove(_shownAlertKeys.first);
    messenger.showSnackBar(SnackBar(
      content: Text('$title\n$body'),
      behavior: SnackBarBehavior.floating,
      duration: const Duration(seconds: 5),
    ));
    return true;
  }

  void _showForegroundPush() {
    final msg = FcmService.instance.lastMessage.value;
    if (msg == null || !_loggedIn) return;
    final type = msg.data['type'];
    final id = type == 'bus_event' ? msg.data['eventId'] : msg.data['id'];
    final key = type != null && id != null ? '$type:$id' : msg.messageId;
    final title = msg.notification?.title ?? msg.data['title'] ?? 'Queen International School';
    final body = msg.notification?.body ?? msg.data['body'] ?? '';
    final children = context.read<ChildrenProvider>();
    final notif = context.read<NotificationsProvider>();
    if (_showBanner(key, title, body)) notif.bump();
    unawaited(children.refresh());
    unawaited(notif.refresh(children.children.map((c) => c.studentId).toList()));
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
      onAlert: (eventType, payload) {
        children.onBusEvent();
        final id = payload['eventId']?.toString();
        final loc = context.read<LocaleProvider>();
        final label = loc.t(eventType == 'PickIn' ? 'pick_in' : 'drop_out');
        final name = payload['studentName']?.toString() ?? '';
        final bus = payload['busNumber']?.toString() ?? '';
        final time = payload['timePakistan']?.toString() ?? '';
        if (_showBanner(id == null ? null : 'bus_event:$id', '🚌 $label', '$name — Bus #$bus\n$time')) {
          notif.bump();
        }
        unawaited(notif.refresh(children.children.map((c) => c.studentId).toList()));
      },
      onBusLocation: children.onBusLocation,
      onAnnouncement: (payload) {
        final id = payload['id']?.toString();
        if (_showBanner(id == null ? null : 'announcement:$id',
            payload['title']?.toString() ?? 'SmartSchool', payload['body']?.toString() ?? '')) {
          notif.bump();
        }
        unawaited(notif.refresh(children.children.map((c) => c.studentId).toList()));
      },
    );
    _signalR!.start();

    children.refresh().then((_) {
      notif.refresh(children.children.map((c) => c.studentId).toList());
    });
    context.read<FeesProvider>().refresh();
    context.read<SettingsProvider>().refresh();
    unawaited(FcmService.instance.init(api));
  }

  void _onLoggedIn() => setState(() {
        _loggedIn = true;
        _startLive();
      });

  Future<void> _onLoggedOut() async {
    await _signalR?.stop();
    _signalR = null;
    _shownAlertKeys.clear();
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
