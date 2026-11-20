// Stand-in for the FurBrains parent app: proves the Unity AR game (FurBrainsAR) can run inside a Flutter app
// and that messages pass both ways. Two screens:
//   HomeScreen  - shows the coin count Unity last reported and a button that opens the game.
//   UnityScreen - the Unity game filling the screen (EmbedUnity widget), with a back button and a test button.
//
// Messages (see Assets/FurBrains/Scripts/Integration/FlutterBridge.cs on the Unity side):
//   Unity -> Flutter: JSON strings like {"type":"ready","coins":100} and {"type":"coins","coins":95}.
//   Flutter -> Unity: sendToUnity("FurBrains Game", "AddCoins", "10") runs FlutterBridge.AddCoins("10").

import 'dart:convert';

import 'package:flutter_embed_unity/flutter_embed_unity.dart';
import 'package:material_ui/material_ui.dart';

void main() {
  runApp(const FurBrainsHostApp());
}

/// The coin count Unity last reported. A ValueNotifier is a value plus a list of listeners told when it changes
/// (like a small observable variable); both screens rebuild from it. null = Unity hasn't reported yet.
final ValueNotifier<int?> unityCoins = ValueNotifier<int?>(null);

/// Handles one message from Unity.
void onMessageFromUnity(String message) {
  try {
    final data = jsonDecode(message) as Map<String, dynamic>;
    if (data['type'] == 'ready' || data['type'] == 'coins') {
      unityCoins.value = data['coins'] as int;
    }
  } catch (e) {
    debugPrint('FurBrains host: unexpected message from Unity: $message ($e)');
  }
}

class FurBrainsHostApp extends StatelessWidget {
  const FurBrainsHostApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'FurBrains (test host)',
      theme: ThemeData(colorScheme: ColorScheme.fromSeed(seedColor: Colors.deepPurple)),
      home: const HomeScreen(),
    );
  }
}

class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('FurBrains (test host)')),
      body: Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            // Rebuilds just this text whenever unityCoins changes.
            ValueListenableBuilder<int?>(
              valueListenable: unityCoins,
              builder: (context, coins, _) => Text(
                coins == null ? 'Coins: (open the game once)' : 'Coins: $coins',
                style: Theme.of(context).textTheme.headlineMedium,
              ),
            ),
            const SizedBox(height: 32),
            FilledButton.icon(
              icon: const Icon(Icons.pets),
              label: const Padding(
                padding: EdgeInsets.all(16),
                child: Text('Play with your FurBrain', style: TextStyle(fontSize: 22)),
              ),
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute<void>(builder: (_) => const UnityScreen()),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class UnityScreen extends StatelessWidget {
  const UnityScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: ValueListenableBuilder<int?>(
          valueListenable: unityCoins,
          builder: (context, coins, _) => Text(coins == null ? 'Loading...' : 'Coins: $coins'),
        ),
        actions: [
          // Test of the Flutter -> Unity direction.
          TextButton(
            onPressed: () => sendToUnity('FurBrains Game', 'AddCoins', '10'),
            child: const Text('+10 coins'),
          ),
        ],
      ),
      // Unity starts the first time this widget is shown, and pauses (stays in memory) when the screen is closed.
      body: const EmbedUnity(onMessageFromUnity: onMessageFromUnity),
    );
  }
}
