import PeekoCore
import SwiftUI

/// The popover's compact list. The window uses `CatalogGridView`.
struct CatalogView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            PanelSection(title: "Presets") {
                if model.presets.isEmpty {
                    Text("No presets found (catalog.json missing from the app bundle).")
                        .font(Theme.mono())
                        .foregroundStyle(Theme.muted)
                }
                ForEach(model.presets) { preset in
                    row(model.enabledCopy(of: preset) ?? preset) { PresetToggle(preset: preset) }
                }
            }

            PanelSection(title: "Your tasks") {
                ForEach(model.customTasks) { task in
                    row(task) { DeleteTaskButton(task: task) }
                }
                ManualTaskForm()
                AppTimeForm()
            }
        }
    }

    private func row<Trailing: View>(_ task: TaskDefinition, @ViewBuilder trailing: () -> Trailing) -> some View {
        HStack(spacing: 12) {
            TaskIcon(task.icon, size: 14)
                .frame(width: 22)
                .foregroundStyle(Theme.green)
            VStack(alignment: .leading, spacing: 2) {
                Text(task.title)
                    .font(Theme.rowTitle)
                    .lineLimit(1)
                Text(TaskDetail.describe(task))
                    .font(Theme.mono())
                    .foregroundStyle(Theme.muted)
                    .lineLimit(1)
            }
            Spacer(minLength: 8)
            trailing()
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .background(RoundedRectangle(cornerRadius: 10).fill(Theme.card))
    }
}

/// On/off for a preset. Writing presets ask for a folder when switched on.
struct PresetToggle: View {
    @Environment(AppModel.self) private var model
    let preset: TaskDefinition

    var body: some View {
        // A closure, not `set: toggle`: Swift 6.3 crashes building the thunk for a method reference here.
        Toggle("", isOn: Binding(get: { model.isEnabled(preset) }, set: { toggle($0) }))
            .toggleStyle(.switch)
            .tint(Theme.green)
            .controlSize(.mini)
            .labelsHidden()
            .accessibilityLabel(preset.title)
    }

    func toggle(_ on: Bool) {
        guard on else { return model.setEnabled(preset, false) }
        switch model.setup(for: preset) {
        case .folder(let message):
            if let folder = PickedFolder.choose(message: message) { model.enable(preset, answer: folder.path) }
        case .text(let title, let message, let placeholder):
            if let answer = AskText.ask(title: title, message: message, placeholder: placeholder) { model.enable(preset, answer: answer) }
        case nil where preset.type == .reminders:
            Task {
                if await model.requestRemindersAccess() { model.setEnabled(preset, true) }
            }
        case nil:
            model.setEnabled(preset, true)
        }
    }
}

struct DeleteTaskButton: View {
    @Environment(AppModel.self) private var model
    let task: TaskDefinition

    var body: some View {
        Button { model.remove(task) } label: {
            Image(systemName: "trash").foregroundStyle(Theme.muted)
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Delete \(task.title)")
    }
}

struct ManualTaskForm: View {
    @Environment(AppModel.self) private var model
    @ViewState private var draft = ""

    var body: some View {
        HStack(spacing: 8) {
            TextField("Custom manual task", text: $draft)
                .textFieldStyle(.plain)
                .font(.system(size: 13))
                .onSubmit(add)
            Button("Add", action: add)
                .buttonStyle(.plain)
                .font(Theme.mono().weight(.medium))
                .foregroundStyle(isEmpty ? Theme.muted : Theme.green)
                .disabled(isEmpty)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .background(RoundedRectangle(cornerRadius: 10).strokeBorder(Color.white.opacity(0.12)))
    }

    private var isEmpty: Bool { draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

    private func add() {
        model.addCustomManualTask(title: draft)
        draft = ""
    }
}
