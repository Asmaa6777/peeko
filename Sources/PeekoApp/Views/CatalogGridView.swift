import PeekoCore
import SwiftUI

enum TaskCategory: String, CaseIterable, Identifiable {
    case time = "Time in apps"
    case limits = "Limits"
    case writing = "Writing"
    case routine = "Sleep & mornings"
    case mac = "On your Mac"
    case online = "Online"
    case manual = "Tap it yourself"

    var id: String { rawValue }

    init(_ task: TaskDefinition) {
        switch task.type {
        case .appTime: self = .time
        case .appLimit: self = .limits
        case .folderActivity: self = .writing
        case .activity: self = .routine
        case .remoteCheck: self = .online
        case .gitCommits, .backup, .reminders, .folderTidy: self = .mac
        case .manual: self = .manual
        }
    }
}

/// The window's catalog: search, category chips, and a card grid.
struct CatalogGridView: View {
    @Environment(AppModel.self) private var model
    @ViewState private var query = ""
    @ViewState private var category: TaskCategory?
    @FocusState private var searchFocused: Bool

    private let columns = [GridItem(.adaptive(minimum: 196), spacing: 12)]

    var body: some View {
        let presets = model.presets.filter(matches)
        let custom = model.customTasks.filter(matches)

        VStack(alignment: .leading, spacing: 18) {
            searchBar
            chips

            if presets.isEmpty && custom.isEmpty {
                VStack(spacing: 10) {
                    FaceView(mood: .looking, width: 52)
                    Text(query.isEmpty ? "Nothing in this category yet." : "Nothing matches \u{201C}\(query)\u{201D}.")
                        .font(.system(size: 14, weight: .semibold))
                    Text("Try another word, or add your own below.")
                        .font(Theme.mono())
                        .foregroundStyle(Theme.muted)
                }
                .frame(maxWidth: .infinity)
                .padding(.vertical, 32)
            }

            if !presets.isEmpty {
                PanelSection(title: "Presets") {
                    LazyVGrid(columns: columns, alignment: .leading, spacing: 12) {
                        ForEach(presets) { preset in
                            TaskCard(task: model.enabledCopy(of: preset) ?? preset, isOn: model.isEnabled(preset)) {
                                PresetToggle(preset: preset)
                            }
                        }
                    }
                }
            }

            if !custom.isEmpty {
                PanelSection(title: "Your tasks") {
                    LazyVGrid(columns: columns, alignment: .leading, spacing: 12) {
                        ForEach(custom) { task in
                            TaskCard(task: task, isOn: true) { DeleteTaskButton(task: task) }
                        }
                    }
                }
            }

            PanelSection(title: "Add your own") {
                VStack(spacing: 8) {
                    ManualTaskForm()
                    AppTimeForm()
                }
            }
        }
    }

    private var searchBar: some View {
        HStack(spacing: 10) {
            Image(systemName: "magnifyingglass").foregroundStyle(Theme.muted)
            TextField("Search tasks: reading, sleep, YouTube…", text: $query)
                .textFieldStyle(.plain)
                .font(.system(size: 14))
                .focused($searchFocused)
                .onExitCommand { query = "" }
            if !query.isEmpty {
                Button { query = "" } label: {
                    Image(systemName: "xmark.circle.fill").foregroundStyle(Theme.muted)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Clear search")
            }
            Text("⌘F")
                .font(Theme.mono(10))
                .foregroundStyle(Theme.muted.opacity(0.6))
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .background(RoundedRectangle(cornerRadius: 12).fill(Theme.card))
        .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(searchFocused ? Theme.green.opacity(0.6) : Color.white.opacity(0.08)))
        .background(
            Button("") { searchFocused = true }
                .keyboardShortcut("f", modifiers: .command)
                .opacity(0)
        )
    }

    private var chips: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                chip("All", selected: category == nil) { category = nil }
                ForEach(TaskCategory.allCases) { item in
                    chip(item.rawValue, selected: category == item) { category = category == item ? nil : item }
                }
            }
        }
    }

    private func chip(_ title: String, selected: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title)
                .font(Theme.mono(11).weight(.medium))
                .foregroundStyle(selected ? Theme.night : Theme.chalk.opacity(0.85))
                .padding(.horizontal, 12)
                .padding(.vertical, 6)
                .background(Capsule().fill(selected ? Theme.green : Theme.card))
        }
        .buttonStyle(.plain)
    }

    private func matches(_ task: TaskDefinition) -> Bool {
        let kind = TaskCategory(task)
        if let category, kind != category { return false }
        let q = query.trimmingCharacters(in: .whitespaces)
        guard !q.isEmpty else { return true }
        return [task.title, TaskDetail.describe(task), kind.rawValue].contains { $0.localizedStandardContains(q) }
    }
}

private struct TaskCard<Accessory: View>: View {
    let task: TaskDefinition
    let isOn: Bool
    @ViewBuilder let accessory: () -> Accessory

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .top) {
                TaskIcon(task.icon, size: 17)
                    .foregroundStyle(Theme.green)
                    .frame(width: 36, height: 36)
                    .background(RoundedRectangle(cornerRadius: 10).fill(Theme.green.opacity(0.12)))
                Spacer()
                accessory()
            }
            Text(task.title)
                .font(.system(size: 15, weight: .semibold))
                .lineLimit(2)
                .fixedSize(horizontal: false, vertical: true)
            Text(TaskDetail.describe(task))
                .font(Theme.mono(11))
                .foregroundStyle(Theme.muted)
                .lineLimit(2)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
            Text("· \(TaskCategory(task).rawValue)")
                .font(Theme.mono(10))
                .foregroundStyle(Theme.muted.opacity(0.8))
        }
        .padding(16)
        .frame(maxWidth: .infinity, minHeight: 168, alignment: .topLeading)
        .background(RoundedRectangle(cornerRadius: 14).fill(Theme.card))
        .overlay(RoundedRectangle(cornerRadius: 14).strokeBorder(isOn ? Theme.green.opacity(0.45) : Color.white.opacity(0.06)))
    }
}
