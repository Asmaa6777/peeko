import AppKit
import PeekoCore
import SwiftUI

/// The full window: a sidebar plus the same screens as the popover, with room to breathe.
struct MainWindowView: View {
    static let id = "main"

    @Environment(AppModel.self) private var model
    @ViewState private var section: Tab = .today

    var body: some View {
        HStack(spacing: 0) {
            sidebar
                .frame(width: 230)
                .background(Color.white.opacity(0.025))
            Rectangle()
                .fill(Color.white.opacity(0.06))
                .frame(width: 1)
            detail
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .frame(minWidth: 780, minHeight: 540)
        .background(Theme.night)
        .foregroundStyle(Theme.chalk)
        .environment(\.colorScheme, .dark)
        .preferredColorScheme(.dark)
        .onAppear {
            // A Dock icon and ⌘Tab while the window is open; back to menu-bar-only when it closes.
            NSApp.setActivationPolicy(.regular)
            NSApp.activate()
            model.rolloverIfNeeded()
            Task { await model.refreshSystemStatus() }
        }
        .onDisappear {
            NSApp.setActivationPolicy(.accessory)
        }
    }

    // MARK: Sidebar

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .top, spacing: 12) {
                FaceView(mood: model.mood, width: 52)
                VStack(alignment: .leading, spacing: 2) {
                    Text("peeko")
                        .font(Theme.display(28))
                        .tracking(-1)
                    Text(Peeko.tagline)
                        .font(Theme.mono(10))
                        .foregroundStyle(Theme.muted)
                }
                .padding(.top, 6)
            }
            .padding(.horizontal, 20)
            // Clears the traffic lights of the hidden title bar.
            .padding(.top, 44)
            .padding(.bottom, 28)

            if model.settings.hasCompletedOnboarding {
                VStack(spacing: 4) {
                    ForEach(Array(Tab.allCases.enumerated()), id: \.element) { index, item in
                        navItem(item, shortcut: Character("\(index + 1)"))
                    }
                }
                .padding(.horizontal, 12)
            }

            Spacer()

            VStack(alignment: .leading, spacing: 6) {
                if !model.tasks.isEmpty {
                    Text("\(model.doneCount) / \(model.tasks.count) today")
                        .font(Theme.mono(13).weight(.medium))
                }
                if model.streak > 0 {
                    HStack(spacing: 4) {
                        Image(systemName: "flame.fill").foregroundStyle(Theme.green)
                        Text(model.streak == 1 ? "1 day in a row" : "\(model.streak) days in a row")
                    }
                    .font(Theme.mono(11))
                    .foregroundStyle(Theme.muted)
                }
                Text("v\(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev")")
                    .font(Theme.mono(10))
                    .foregroundStyle(Theme.muted.opacity(0.7))
            }
            .padding(20)
        }
    }

    private func navItem(_ item: Tab, shortcut: Character) -> some View {
        let selected = section == item
        return Button {
            section = item
        } label: {
            HStack(spacing: 10) {
                Image(systemName: icon(for: item))
                    .frame(width: 18)
                    .foregroundStyle(selected ? Theme.green : Theme.muted)
                Text(item.rawValue)
                    .font(.system(size: 14, weight: selected ? .semibold : .regular))
                Spacer()
                Text("⌘\(String(shortcut))")
                    .font(Theme.mono(10))
                    .foregroundStyle(Theme.muted.opacity(0.6))
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
            .background(RoundedRectangle(cornerRadius: 8).fill(selected ? Theme.card : .clear))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .keyboardShortcut(KeyEquivalent(shortcut), modifiers: .command)
    }

    private func icon(for tab: Tab) -> String {
        switch tab {
        case .today: "checklist"
        case .catalog: "square.grid.2x2"
        case .history: "calendar"
        case .settings: "gearshape"
        }
    }

    // MARK: Detail

    @ViewBuilder
    private var detail: some View {
        if model.settings.hasCompletedOnboarding {
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    VStack(alignment: .leading, spacing: 4) {
                        Text(section.rawValue)
                            .font(Theme.display(34))
                            .tracking(-1)
                        Text(subtitle)
                            .font(Theme.mono(12))
                            .foregroundStyle(Theme.muted)
                    }
                    if let error = model.lastError {
                        Text(error)
                            .font(Theme.mono())
                            .foregroundStyle(.red)
                    }
                    switch section {
                    case .today: TodayView(tab: $section)
                    case .catalog: CatalogGridView()
                    case .history: HistoryView(weeks: 20)
                    case .settings: SettingsView()
                    }
                }
                // The catalog grid and the history calendar use the extra width; lists stay readable.
                .frame(maxWidth: section == .catalog || section == .history ? 960 : 640, alignment: .leading)
                .padding(.horizontal, 36)
                .padding(.top, 48)
                .padding(.bottom, 36)
                .frame(maxWidth: .infinity)
            }
        } else {
            OnboardingView()
                .frame(maxWidth: 460, maxHeight: Theme.contentHeight)
        }
    }

    private var subtitle: String {
        switch section {
        case .today:
            model.tasks.isEmpty ? "nothing to watch yet" : "\(model.doneCount) of \(model.tasks.count) done"
        case .catalog: "pick what Peeko keeps an eye on"
        case .history: "the last 20 weeks"
        case .settings: "how Peeko behaves"
        }
    }
}
