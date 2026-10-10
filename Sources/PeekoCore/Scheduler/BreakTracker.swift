import Foundation

/// Counts active screen time (any app) and says when it's time for a short break.
/// Stepping away for `breakLength` or longer counts as the break and restarts the count.
public struct BreakTracker: Sendable {
    public static let breakLength: TimeInterval = 5 * 60
    /// Input within this window counts as being at the computer.
    public static let activeWindow: TimeInterval = 120

    public private(set) var activeSeconds: TimeInterval = 0
    private var lastTick: Date?

    public init() {}

    /// Returns true when a reminder is due. The count restarts after each reminder.
    public mutating func record(_ sample: ActivitySample, interval: TimeInterval) -> Bool {
        defer { lastTick = sample.now }

        // Idle that long, or no ticks that long (asleep, app not running): that was a break.
        let gap = lastTick.map { sample.now.timeIntervalSince($0) } ?? 0
        if sample.secondsSinceInput >= Self.breakLength || gap >= Self.breakLength {
            activeSeconds = 0
            return false
        }
        guard lastTick != nil, sample.secondsSinceInput <= Self.activeWindow else { return false }

        activeSeconds += min(max(gap, 0), ActivityTracker.maxDelta)
        guard activeSeconds >= interval else { return false }
        activeSeconds = 0
        return true
    }
}
