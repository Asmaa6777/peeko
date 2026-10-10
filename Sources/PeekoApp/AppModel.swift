import Foundation
import Observation
import PeekoCore

enum Mood {
    case dozing, looking, beaming

    init(_ status: TaskStatus) {
        switch status {
        case .pending, .failed: self = .dozing
        case .inProgress: self = .looking
        case .done: self = .beaming
        }
    }
}

@MainActor
@Observable
final class AppModel {
    let presets: [TaskDefinition]
    private let store: Store
    private(set) var taskList: TaskList
    private(set) var record: DayRecord
    private(set) var lastError: String?

    init(store: Store = Store(directory: Store.defaultDirectory), bundle: Bundle = .main) {
        self.store = store
        self.presets = Self.loadPresets(from: bundle)
        self.taskList = store.loadTaskList()
        self.record = store.loadDay(DayKey.string(for: .now))

        _ = NotificationCenter.default.addObserver(
            forName: .NSCalendarDayChanged, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.rolloverIfNeeded() }
        }
    }

    var tasks: [TaskDefinition] { taskList.tasks }
    var customTasks: [TaskDefinition] { tasks.filter(\.isCustom) }

    func status(for task: TaskDefinition) -> TaskStatus {
        record.statuses[task.id] ?? .pending
    }

    func isAutomatic(_ task: TaskDefinition) -> Bool { task.type != .manual }
    func isSupported(_ task: TaskDefinition) -> Bool { makeVerifier(for: task) != nil }

    var doneCount: Int { tasks.filter { status(for: $0).isDone }.count }

    var mood: Mood {
        if !tasks.isEmpty && doneCount == tasks.count { return .beaming }
        let anyProgress = tasks.contains {
            switch status(for: $0) {
            case .done, .inProgress: true
            case .pending, .failed: false
            }
        }
        return anyProgress ? .looking : .dozing
    }

    func isEnabled(_ preset: TaskDefinition) -> Bool {
        tasks.contains { $0.id == preset.id }
    }

    func setEnabled(_ preset: TaskDefinition, _ enabled: Bool) {
        if enabled {
            guard !isEnabled(preset) else { return }
            taskList.tasks.append(preset)
        } else {
            taskList.tasks.removeAll { $0.id == preset.id }
        }
        saveTaskList()
    }

    func addCustomManualTask(title: String) {
        let trimmed = title.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        taskList.tasks.append(.customManual(title: trimmed))
        saveTaskList()
    }

    func remove(_ task: TaskDefinition) {
        taskList.tasks.removeAll { $0.id == task.id }
        record.statuses[task.id] = nil
        saveTaskList()
        saveRecord()
    }

    func toggleManual(_ task: TaskDefinition) {
        guard task.type == .manual else { return }
        rolloverIfNeeded()
        record.statuses[task.id] = status(for: task).isDone ? nil : .done(at: .now)
        saveRecord()
    }

    func rolloverIfNeeded(now: Date = .now) {
        let key = DayKey.string(for: now)
        guard key != record.day else { return }
        record = store.loadDay(key)
    }

    private func saveTaskList() {
        do { try store.save(taskList); lastError = nil } catch { lastError = "Couldn't save tasks: \(error.localizedDescription)" }
    }

    private func saveRecord() {
        do { try store.save(record); lastError = nil } catch { lastError = "Couldn't save today: \(error.localizedDescription)" }
    }

    private static func loadPresets(from bundle: Bundle) -> [TaskDefinition] {
        guard let url = bundle.url(forResource: "catalog", withExtension: "json"),
              let data = try? Data(contentsOf: url),
              let catalog = try? Catalog.decode(data)
        else { return [] }
        return catalog.presets
    }
}
