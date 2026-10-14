import 'package:flutter_tts/flutter_tts.dart';

/// Optional voice confirmation ("Ahmed picked up") after each tap.
class TtsService {
  TtsService._();
  static final TtsService instance = TtsService._();

  final FlutterTts _tts = FlutterTts();
  bool enabled = true; // Home screen par toggle

  Future<void> _init() async {
    await _tts.setLanguage('en-US'); // English names speak best on most devices
    await _tts.setSpeechRate(0.48);
    await _tts.setVolume(1.0);
    await _tts.setPitch(1.0);
  }

  bool _ready = false;

  Future<void> speak(String text) async {
    if (!enabled) return;
    try {
      if (!_ready) {
        await _init();
        _ready = true;
      }
      await _tts.stop();
      await _tts.speak(text);
    } catch (_) {
      // Voice is a nicety — never let it break the flow.
    }
  }
}
