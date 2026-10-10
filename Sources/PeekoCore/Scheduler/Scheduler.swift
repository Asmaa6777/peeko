import AppKit
import Foundation
import Observation

/// Owns today's record: ticks, credits app time, evaluates verifiers, rolls over at midnight, saves.
@MainActor
@Observable
public final class Scheduler {
    public static let tickInterval: TimeInterval = 10

    public private(set) var record: DayRecord
    public private(set) var lastSaveError: String?
    public var tasks: [TaskDefinition] = []
    public var githubUsername = ""

    /// Called once when an automatic task flips to done (never for manual taps).
    @ObservationIgnored public var onAutoComplete: (@MainActor (TaskDefinition) -> Void)?
    /// Called after every tick with that tick's activity sample.
    @ObservationIgnored public var onTick: (@MainActor (ActivitySample) -> Void)?

    @ObservationIgnored private let store: Store
    @ObservationIgnored private let frontmost: any FrontmostAppProvider
    @ObservationIgnored private let idle: any IdleTimeProvider
    @ObservationIgnored private let github: GitHubEventsClient
    @ObservationIgnored private let calendar: Calendar
    @ObservationIgnored private var activity = ActivityTracker()
    @ObservationIgnored private var timer: Timer?
    @ObservationIgnored private var observers: [(NotificationCenter, any NSObjectProtocol)] = []

    public init(
        store: Store,
        frontmost: any FrontmostAppProvider,
        idle: any IdleTimeProvider,
        github: GitHubEventsClient,
        calendar: Calendar = .current,
        now: Date = .now
    ) {
        self.store = store
        self.frontmost = frontmost
        self.idle = idle
        self.github = github
        self.calendar = calendar
        self.record = store.loadDay(DayKey.string(for: now, calendar: calendar))
    }

    public func start() {
        guard timer == nil else { return }
        let timer = Timer(timeInterval: Self.tickInterval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                Task { await self.tick() }
            }
        }
        timer.tolerance = 2
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer

        let workspace = NSWorkspace.shared.notificationCenter
        observe(workspace, NSWorkspace.willSleepNotification) { $0.pause() }
        observe(workspace, NSWorkspace.screensDidSleepNotification) { $0.pause() }
        observe(.default, .NSCalendarDayChanged) { $0.rolloverIfNeeded(now: .now) }

        Task { await tick() }
    }

    public func stop() {
        timer?.invalidate()
        timer = nil
        for (center, token) in observers { center.removeObserver(token) }
        observers.removeAll()
    }

    public func pause() {
        activity.pause()
    }

    public func tick(now: Date = .now) async {
        rolloverIfNeeded(now: now)
        let sample = ActivitySample(
            now: now,
            frontmostBundleID: frontmost.frontmostBundleID(),
            secondsSinceInput: idle.secondsSinceLastInput()
        )
        let credited = activity.record(sample, tasks: tasks, into: &record)
        let statusesChanged = await evaluateAutomaticTasks(now: now)
        if !credited.isEmpty || statusesChanged { save() }
        onTick?(sample)
    }

    /// Re-evaluates without crediting time, e.g. right after a task is enabled.
    public func refresh(now: Date = .now) async {
        rolloverIfNeeded(now: now)
        if await evaluateAutomaticTasks(now: now) { save() }
    }

    public func rolloverIfNeeded(now: Date) {
        let key = DayKey.string(for: now, calendar: calendar)
        guard key != record.day else { return }
        record = store.loadDay(key)
    }

    public func setManual(_ done: Bool, for task: TaskDefinition, now: Date = .now) {
        guard task.type == .manual else { return }
        rolloverIfNeeded(now: now)
        record.statuses[task.id] = done ? .done(at: now) : nil
        save()
    }

    public func forget(taskID: String) {
        record.statuses[taskID] = nil
        record.accumulatedSeconds[taskID] = nil
        save()
    }

    /// Manual tasks are skipped: the record is their source of truth, and writing an
    /// evaluated copy back after an `await` could undo a tap made in between.
    private func evaluateAutomaticTasks(now: Date) async -> Bool {
        let day = record.day
        let context = VerifierContext(now: now, record: record)
        let environment = VerifierEnvironment(githubUsername: githubUsername, github: github, calendar: calendar)
        let evaluated = tasks.filter { $0.type != .manual }
        var results: [String: TaskStatus] = [:]
        for task in evaluated {
            guard let verifier = makeVerifier(for: task, environment: environment) else { continue }
            results[task.id] = await verifier.evaluate(context: context)
        }
        guard record.day == day else { return false }

        var changed = false
        var completed: [TaskDefinition] = []
        for task in evaluated {
            guard let status = results[task.id], record.statuses[task.id] != status else { continue }
            if status.isDone && record.statuses[task.id]?.isDone != true { completed.append(task) }
            record.statuses[task.id] = status
            changed = true
        }
        for task in completed { onAutoComplete?(task) }
        return changed
    }

    private func save() {
        do {
            try store.save(record)
            lastSaveError = nil
        } catch {
            lastSaveError = "Couldn't save today: \(error.localizedDescription)"
        }
    }

    private func observe(
        _ center: NotificationCenter,
        _ name: Notification.Name,
        _ action: @escaping @MainActor (Scheduler) -> Void
    ) {
        let token = center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                action(self)
            }
        }
        observers.append((center, token))
    }
}
