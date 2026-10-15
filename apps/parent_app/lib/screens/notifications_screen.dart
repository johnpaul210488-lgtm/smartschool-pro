import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../core/models.dart';
import '../providers/children_provider.dart';
import '../providers/locale_provider.dart';
import '../providers/notifications_provider.dart';

/// Two tabs: pick/drop alert history + school announcements.
class NotificationsScreen extends StatefulWidget {
  const NotificationsScreen({super.key});

  @override
  State<NotificationsScreen> createState() => _NotificationsScreenState();
}

class _NotificationsScreenState extends State<NotificationsScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _reload());
    context.read<NotificationsProvider>().markSeen();
  }

  Future<void> _reload() {
    final childIds =
        context.read<ChildrenProvider>().children.map((c) => c.studentId).toList();
    return context.read<NotificationsProvider>().refresh(childIds);
  }

  @override
  Widget build(BuildContext context) {
    final loc = context.watch<LocaleProvider>();
    final notif = context.watch<NotificationsProvider>();

    return DefaultTabController(
      length: 2,
      child: Scaffold(
        appBar: AppBar(
          title: Text(loc.t('alerts')),
          bottom: TabBar(
            tabs: [
              Tab(icon: const Icon(Icons.directions_bus), text: loc.t('pick_alerts')),
              Tab(icon: const Icon(Icons.campaign), text: loc.t('announcements')),
            ],
          ),
        ),
        body: TabBarView(
          children: [
            RefreshIndicator(
              onRefresh: _reload,
              child: notif.alerts.isEmpty
                  ? _EmptyList(text: loc.t('no_alerts'))
                  : ListView.builder(
                      itemCount: notif.alerts.length,
                      itemBuilder: (context, i) => _AlertTile(item: notif.alerts[i], loc: loc),
                    ),
            ),
            RefreshIndicator(
              onRefresh: _reload,
              child: notif.announcements.isEmpty
                  ? _EmptyList(text: loc.t('no_announcements'))
                  : ListView.builder(
                      itemCount: notif.announcements.length,
                      itemBuilder: (context, i) =>
                          _AnnouncementTile(item: notif.announcements[i]),
                    ),
            ),
          ],
        ),
      ),
    );
  }
}

class _EmptyList extends StatelessWidget {
  const _EmptyList({required this.text});
  final String text;
  @override
  Widget build(BuildContext context) => ListView(
        children: [
          const SizedBox(height: 120),
          const Icon(Icons.inbox, size: 56, color: Colors.grey),
          const SizedBox(height: 8),
          Text(text, textAlign: TextAlign.center),
        ],
      );
}

class _AlertTile extends StatelessWidget {
  const _AlertTile({required this.item, required this.loc});
  final AlertItem item;
  final LocaleProvider loc;

  @override
  Widget build(BuildContext context) {
    final picked = item.eventType == 'PickIn';
    return ListTile(
      leading: CircleAvatar(
        backgroundColor: picked ? Colors.blue : Colors.teal,
        child: Icon(picked ? Icons.login : Icons.logout, color: Colors.white, size: 20),
      ),
      title: Text('${item.studentName} — ${picked ? loc.t('pick_in') : loc.t('drop_out')}',
          style: const TextStyle(fontWeight: FontWeight.w600)),
      subtitle: Text('${item.timePakistan}${item.location != null ? '\n${item.location}' : ''}'),
      isThreeLine: item.location != null,
    );
  }
}

class _AnnouncementTile extends StatelessWidget {
  const _AnnouncementTile({required this.item});
  final AnnouncementItem item;

  @override
  Widget build(BuildContext context) => Card(
        margin: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(item.title,
                  style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16)),
              if (item.createdByName != null)
                Text('${item.createdByName}  •  ${item.timePakistan}',
                    style: const TextStyle(color: Colors.grey, fontSize: 12)),
              const SizedBox(height: 6),
              Text(item.body),
            ],
          ),
        ),
      );
}
