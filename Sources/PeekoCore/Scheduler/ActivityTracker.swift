import Foundation

public struct ActivitySample: Sendable {
    public var now: Date
    public var frontmostBundleID: String?
    public var secondsSinceInput: TimeInterval

    public init(now: Date, frontmostBundleID: String?, secondsSinceInput: TimeInterval) {
        self.now = now
        self.frontmostBundleID = frontmostBundleID
        self.secondsSinceInput = secondsSinceInput
    }
}

/// Credits active time to `appTime` tasks, one tick at a time.
public struct ActivityTracker: Sendable {
    /// A tick never credits more than this, so a sleeping Mac can't add hours.
    public static let maxDelta: TimeInterval = 10
    public static let defaultIdleCutoff: TimeInterval = 120

    public private(set) var lastTick: Date?

    public init() {}

    /// The next tick credits nothing and just restarts the clock.
    public mutating func pause() {
        lastTick = nil
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
        guard delta > 0, let front = sample.frontmostBundleID else { return [:] }

        var credited: [String: TimeInterval] = [:]
        for task in tasks where task.type == .appTime {
            guard task.params.bundleIDs?.contains(front) == true else { continue }
            let cutoff = task.params.idleCutoffSeconds.map(TimeInterval.init) ?? Self.defaultIdleCutoff
            guard sample.secondsSinceInput <= cutoff else { continue }
            record.accumulatedSeconds[task.id, default: 0] += delta
            credited[task.id] = delta
        }
        return credited
    }
}
