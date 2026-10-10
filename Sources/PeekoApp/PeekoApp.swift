import PeekoCore
import SwiftUI

@main
struct PeekoApp: App {
    var body: some Scene {
        MenuBarExtra("Peeko", systemImage: "eyes") {
            VStack(alignment: .leading, spacing: 4) {
                Text("Hello, Peeko")
                    .font(.headline)
                Text(Peeko.tagline)
                    .font(.caption.monospaced())
                    .foregroundStyle(.secondary)
                Divider()
                Button("Quit Peeko") { NSApplication.shared.terminate(nil) }
                    .keyboardShortcut("q")
            }
            .padding(12)
            .frame(width: 220, alignment: .leading)
        }
        .menuBarExtraStyle(.window)
    }
}
