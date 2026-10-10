import AppKit
import Foundation
import Observation
import PeekoCore
import ServiceManagement
import UserNotifications

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
    let scheduler: Scheduler
    private let store: Store
    private let notifier = Notifier()
    private(set) var taskList: TaskList
    private(set) var settings: AppSettings
    private(set) var notificationStatus: UNAuthorizationStatus = .notDetermined
    private(set) var launchAtLoginStatus: SMAppService.Status = SMAppService.mainApp.status
    private var localError: String?
    private var breakTracker = BreakTracker()
    private var breakMessageIndex = 0

    private static let lastNudgeDayKey = "lastNudgeDay"

    init(store: Store = Store(directory: Store.defaultDirectory), bundle: Bundle = .main) {
        self.store = store
        self.presets = Self.loadPresets(from: bundle)
        self.taskList = store.loadTaskList()
        self.settings = store.loadSettings()
        self.scheduler = Scheduler(
            store: store,
            frontmost: SystemFrontmostAppProvider(),
            idle: SystemIdleTimeProvider(),
            github: GitHubEventsClient(http: URLSessionHTTPClient())
        )
        scheduler.tasks = taskList.tasks
        scheduler.githubUsername = settings.githubUsername
        scheduler.onAutoComplete = { [weak self] task in self?.notifyCompleted(task) }
        scheduler.onTick = { [weak self] sample in
            self?.nudgeIfDue(now: sample.now)
            self?.remindToTakeBreakIfDue(sample)
        }
        scheduler.start()
        Task { await refreshSystemStatus() }
    }

    var tasks: [TaskDefinition] { taskList.tasks }
    var customTasks: [TaskDefinition] { tasks.filter(\.isCustom) }
    var lastError: String? { localError ?? scheduler.lastSaveError }

    func status(for task: TaskDefinition) -> TaskStatus {
        scheduler.record.statuses[task.id] ?? .pending
    }

    func accumulatedMinutes(for task: TaskDefinition) -> Int {
        Int((scheduler.record.accumulatedSeconds[task.id] ?? 0) / 60)
    }

    func isSupported(_ task: TaskDefinition) -> Bool { isVerifiable(task) }

    /// The username a GitHub task will check, or "" if none is set.
    func githubUsername(for task: TaskDefinition) -> String {
        let own = task.params.username ?? ""
        return own.isEmpty ? settings.githubUsername : own
    }

    var doneCount: Int { tasks.filter { status(for: $0).isDone }.count }

    /// Tasks that can still happen today (not done, not already missed).
    var openCount: Int {
        tasks.filter {
            switch status(for: $0) {
            case .done, .failed: false
            case .pending, .inProgress: true
            }
        }.count
    }

    var mood: Mood {
        if doneCount > 0 && openCount == 0 { return .beaming }
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

    func needsFolder(_ preset: TaskDefinition) -> Bool {
        preset.type == .folderActivity && (preset.params.path ?? "").isEmpty
    }

    /// The enabled copy carries user choices (like the folder), so show that one when it exists.
    func enabledCopy(of preset: TaskDefinition) -> TaskDefinition? {
        tasks.first { $0.id == preset.id }
    }

    func enable(_ preset: TaskDefinition, folder: URL) {
        guard !isEnabled(preset) else { return }
        var task = preset
        task.params.path = folder.path
        taskList.tasks.append(task)
        saveTaskList()
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

    func addCustomAppTimeTask(bundleID: String, appName: String, minutes: Int) {
        guard minutes > 0 else { return }
        taskList.tasks.append(.customAppTime(bundleID: bundleID, appName: appName, minutes: minutes))
        saveTaskList()
    }

    func remove(_ task: TaskDefinition) {
        taskList.tasks.removeAll { $0.id == task.id }
        saveTaskList()
        scheduler.forget(taskID: task.id)
    }

    func toggleManual(_ task: TaskDefinition) {
        scheduler.setManual(!status(for: task).isDone, for: task)
    }

    func rolloverIfNeeded() {
        scheduler.rolloverIfNeeded(now: .now)
    }

    // MARK: Settings

    func updateSettings(_ change: (inout AppSettings) -> Void) {
        var next = settings
        change(&next)
        guard next != settings else { return }
        let usernameChanged = next.githubUsername != settings.githubUsername
        settings = next
        do {
            try store.save(settings)
            localError = nil
        } catch {
            localError = "Couldn't save settings: \(error.localizedDescription)"
        }
        if usernameChanged {
            scheduler.githubUsername = settings.githubUsername
            Task { await scheduler.refresh() }
        }
    }

    func refreshSystemStatus() async {
        notificationStatus = await notifier.authorizationStatus()
        launchAtLoginStatus = SMAppService.mainApp.status
    }

    func requestNotifications() async {
        _ = await notifier.requestAuthorization()
        await refreshSystemStatus()
    }

    func setLaunchAtLogin(_ enabled: Bool) {
        do {
            if enabled {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
            localError = nil
        } catch {
            localError = "Couldn't change launch at login: \(error.localizedDescription)"
        }
        launchAtLoginStatus = SMAppService.mainApp.status
    }

    func revealDataFolder() {
        try? FileManager.default.createDirectory(at: store.directory, withIntermediateDirectories: true)
        NSWorkspace.shared.activateFileViewerSelecting([store.directory])
    }

    // MARK: Notifications

    private func notifyCompleted(_ task: TaskDefinition) {
        notifier.post(
            id: "done-\(scheduler.record.day)-\(task.id)",
            title: "Peeko saw that!",
            body: "\(task.title) ✓  ·  \(doneCount) of \(tasks.count) done today"
        )
    }

    private func nudgeIfDue(now: Date) {
        let defaults = UserDefaults.standard
        let remaining = openCount
        guard EveningNudge.isDue(
            now: now,
            settings: settings,
            lastNudgeDay: defaults.string(forKey: Self.lastNudgeDayKey),
            remaining: remaining
        ) else { return }
        defaults.set(DayKey.string(for: now), forKey: Self.lastNudgeDayKey)
        notifier.post(
            id: "nudge-\(DayKey.string(for: now))",
            title: "Peeko's still peeking",
            body: remaining == 1 ? "1 task left today." : "\(remaining) tasks left today."
        )
    }

    private func remindToTakeBreakIfDue(_ sample: ActivitySample) {
        let interval = TimeInterval(settings.breakIntervalMinutes * 60)
        guard breakTracker.record(sample, interval: interval), settings.breakRemindersEnabled else { return }
        let messages = BreakMessages.all(minutes: settings.breakIntervalMinutes)
        let message = messages[breakMessageIndex % messages.count]
        breakMessageIndex += 1
        notifier.post(id: "break-\(sample.now.timeIntervalSince1970)", title: message.title, body: message.body)
    }

    private func saveTaskList() {
        scheduler.tasks = taskList.tasks
        Task { await scheduler.refresh() }
        do {
            try store.save(taskList)
            localError = nil
        } catch {
            localError = "Couldn't save tasks: \(error.localizedDescription)"
        }
    }

    private static func loadPresets(from bundle: Bundle) -> [TaskDefinition] {
        guard let url = bundle.url(forResource: "catalog", withExtension: "json"),
              let data = try? Data(contentsOf: url),
              let catalog = try? Catalog.decode(data)
        else { return [] }
        return catalog.presets
    }
}
