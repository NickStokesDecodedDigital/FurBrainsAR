import 'package:flutter_test/flutter_test.dart';

import 'package:furbrains_host/main.dart';

void main() {
  testWidgets('Home screen shows the play button', (WidgetTester tester) async {
    await tester.pumpWidget(const FurBrainsHostApp());
    expect(find.text('Play with your FurBrain'), findsOneWidget);
  });
}
