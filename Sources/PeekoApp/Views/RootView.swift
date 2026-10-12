import PeekoCore
import SwiftUI

enum Tab: String, CaseIterable {
    case today = "Today"
    case catalog = "Catalog"
    case history = "History"
    case settings = "Settings"
}

struct RootView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.openWindow) private var openWindow
    @ViewState private var tab: Tab = .today

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            // Kept in the layout during onboarding so the panel height never changes.
            tabBar
                .opacity(model.settings.hasCompletedOnboarding ? 1 : 0)
                .allowsHitTesting(model.settings.hasCompletedOnboarding)
            if let error = model.lastError {
                Text(error)
                    .font(Theme.mono())
                    .foregroundStyle(.red)
                    .padding(.horizontal, 16)
                    .padding(.bottom, 8)
            }
            // Fixed height: a MenuBarExtra window that resizes on tab switch drifts away from the menu bar.
            Group {
                if model.settings.hasCompletedOnboarding {
                    ScrollView {
                        Group {
                            switch tab {
                            case .today: TodayView(tab: $tab)
                            case .catalog: CatalogView()
                            case .history: HistoryView()
                            case .settings: SettingsView()
                            }
                        }
                        .padding(.horizontal, 12)
                        .padding(.vertical, 10)
                    }
                } else {
                    OnboardingView()
                }
            }
            .frame(height: Theme.contentHeight)
            footer
        }
        .frame(width: 320)
        .background(Theme.night)
        .foregroundStyle(Theme.chalk)
        .environment(\.colorScheme, .dark)
        .onAppear {
            model.rolloverIfNeeded()
            Task { await model.refreshSystemStatus() }
        }
    }

    private var header: some View {
        HStack(alignment: .top, spacing: 12) {
            FaceView(mood: model.mood, width: 48)
            VStack(alignment: .leading, spacing: 2) {
                Text("peeko")
                    .font(Theme.display(26))
                    .tracking(-1)
                Text(Peeko.tagline)
                    .font(Theme.mono())
                    .foregroundStyle(Theme.muted)
            }
            .padding(.top, 8)
            Spacer()
            if !model.tasks.isEmpty {
                VStack(alignment: .trailing, spacing: 2) {
                    Text("\(model.doneCount) / \(model.tasks.count)")
                        .font(Theme.mono(15).weight(.medium))
                    HStack(spacing: 3) {
                        if model.streak > 0 {
                            Image(systemName: "flame.fill")
                                .foregroundStyle(Theme.green)
                            Text("\(model.streak) ·")
                        }
                        Text("today")
                    }
                    .font(Theme.mono())
                    .foregroundStyle(Theme.muted)
                    .accessibilityElement(children: .combine)
                }
                .padding(.top, 10)
            }
        }
        .padding(.horizontal, 16)
        .padding(.bottom, 10)
    }

    private var tabBar: some View {
        HStack(spacing: 14) {
            ForEach(Tab.allCases, id: \.self) { item in
                Button {
                    tab = item
                } label: {
                    VStack(spacing: 4) {
                        Text(item.rawValue.uppercased())
                            .font(Theme.mono().weight(.medium))
                            .tracking(0.8)
                            .foregroundStyle(tab == item ? Theme.chalk : Theme.muted)
                        Capsule()
                            .fill(tab == item ? Theme.green : .clear)
                            .frame(height: 2)
                    }
                    .fixedSize()
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
            Spacer()
        }
        .padding(.horizontal, 16)
        .padding(.bottom, 4)
    }

    private var footer: some View {
        HStack {
            Text("v\(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev")")
                .font(Theme.mono())
                .foregroundStyle(Theme.muted)
            Spacer()
            Button {
                openWindow(id: MainWindowView.id)
                NSApp.activate()
            } label: {
                Label("Open window", systemImage: "arrow.up.left.and.arrow.down.right")
            }
            .buttonStyle(.plain)
            .font(Theme.mono())
            .foregroundStyle(Theme.muted)
            .keyboardShortcut("o")
            .padding(.trailing, 12)
            Button("Quit") { NSApplication.shared.terminate(nil) }
                .buttonStyle(.plain)
                .font(Theme.mono())
                .foregroundStyle(Theme.muted)
                .keyboardShortcut("q")
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .background(Color.white.opacity(0.03))
    }
}
