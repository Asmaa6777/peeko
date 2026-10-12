import Foundation

public struct ActivitySample: Sendable {
    public var now: Date
    public var frontmostBundleID: String?
    public var secondsSinceInput: TimeInterval
    public var windowTitle: String?

    public init(now: Date, frontmostBundleID: String?, secondsSinceInput: TimeInterval, windowTitle: String? = nil) {
        self.now = now
        self.frontmostBundleID = frontmostBundleID
        self.secondsSinceInput = secondsSinceInput
        self.windowTitle = windowTitle
    }
}

/// Credits active time to `appTime` and `appLimit` tasks, one tick at a time.
public struct ActivityTracker: Sendable {
    /// A tick never credits more than this, so a sleeping Mac can't add hours.
    public static let maxDelta: TimeInterval = 10
    public static let defaultIdleCutoff: TimeInterval = 120

    /// Grace period before stepping away breaks a continuous run (a quick glance at chat is fine).
    public static let runGrace: TimeInterval = 60

    public private(set) var lastTick: Date?
    private var runs: [String: Run] = [:]
    let calendar: Calendar

    private struct Run {
        var seconds: TimeInterval = 0
        var awaySince: Date?
    }

    public init(calendar: Calendar = .current) {
        self.calendar = calendar
    }

    /// The next tick credits nothing and just restarts the clock. Sleep also ends any focus run.
    public mutating func pause() {
        lastTick = nil
        runs.removeAll()
    }

    /// Returns the seconds credited per task id.
    @discardableResult
    public mutating func record(
        _ sample: ActivitySample,
        tasks: [TaskDefinition],
        into record: inout DayRecord
    ) -> [String: TimeInterval] {
        let delta = lastTick.map { min(max(sample.now.timeIntervalSince($0), 0), Self.maxDelta) } ?? 0
        lastTick = sample.now
        guard delta > 0 else { return [:] }

        var credited: [String: TimeInterval] = [:]
        for task in tasks where task.type == .appTime || task.type == .appLimit {
            let counts = matches(task, sample)
            if task.params.continuous == true {
                var run = runs[task.id] ?? Run()
                if counts {
                    run.awaySince = nil
                    run.seconds += delta
                    if run.seconds > record.accumulatedSeconds[task.id] ?? 0 {
                        record.accumulatedSeconds[task.id] = run.seconds
                        credited[task.id] = delta
                    }
                } else if let away = run.awaySince {
                    if sample.now.timeIntervalSince(away) > Self.runGrace { run.seconds = 0 }
                } else {
                    run.awaySince = sample.now
                }
                runs[task.id] = run
            } else if counts {
                record.accumulatedSeconds[task.id, default: 0] += delta
                credited[task.id] = delta
            }
        }
        return credited
    }

    /// Front app in the task's list (an empty list means any app), title filter, time window, not idle.
    private func matches(_ task: TaskDefinition, _ sample: ActivitySample) -> Bool {
        guard let front = sample.frontmostBundleID else { return false }
        let apps = task.params.bundleIDs ?? []
        guard apps.isEmpty || apps.contains(front) else { return false }
        if task.needsWindowTitle {
            guard let title = sample.windowTitle?.lowercased(),
                  task.params.titleContains!.contains(where: { title.contains($0.lowercased()) })
            else { return false }
        }
        if let from = ActivityWindowVerifier.minutes(task.params.from),
           let until = ActivityWindowVerifier.minutes(task.params.until) {
            let c = calendar.dateComponents([.hour, .minute], from: sample.now)
            let minute = (c.hour ?? 0) * 60 + (c.minute ?? 0)
            guard minute >= from && minute < until else { return false }
        }
        let cutoff = task.params.idleCutoffSeconds.map(TimeInterval.init) ?? Self.defaultIdleCutoff
        return sample.secondsSinceInput <= cutoff
    }
}
