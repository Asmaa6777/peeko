import Foundation

/// Judges a time window against today's activity spans.
/// - `quietBetween`: no activity in the window (e.g. 00:30–05:00 → slept on time).
/// - `startBetween`: a stretch of activity starts inside the window (e.g. 04:00–08:00 → up early).
public struct ActivityWindowVerifier: Verifier {
    public static let rules: Set<String> = ["quietBetween", "startBetween"]

    public let definition: TaskDefinition
    let calendar: Calendar

    public init(definition: TaskDefinition, calendar: Calendar) {
        self.definition = definition
        self.calendar = calendar
    }

    public func evaluate(context: VerifierContext) async -> TaskStatus {
        if let existing = context.record.statuses[definition.id], existing.isDone { return existing }
        let p = definition.params
        guard let from = Self.minutes(p.from), let until = Self.minutes(p.until), from < until else { return .pending }

        let dayStart = calendar.startOfDay(for: context.now)
        let fromDate = calendar.date(byAdding: .minute, value: from, to: dayStart)!
        let untilDate = calendar.date(byAdding: .minute, value: until, to: dayStart)!
        let spans = context.record.activity ?? []

        switch p.rule {
        case "quietBetween":
            if spans.contains(where: { $0.end >= fromDate && $0.start < untilDate }) { return .failed }
            return context.now >= untilDate ? .done(at: untilDate) : .pending
        case "startBetween":
            // Only stretches that *start* in the window count, so an all-nighter isn't an early morning.
            if let first = spans.map(\.start).filter({ $0 >= fromDate }).min(), first < untilDate {
                return .done(at: first)
            }
            return context.now >= untilDate ? .failed : .pending
        default:
            return .pending
        }
    }

    /// "HH:mm" → minutes after midnight.
    public static func minutes(_ hhmm: String?) -> Int? {
        guard let parts = hhmm?.split(separator: ":"), parts.count == 2,
              let h = Int(parts[0]), let m = Int(parts[1]), (0..<24).contains(h), (0..<60).contains(m)
        else { return nil }
        return h * 60 + m
    }
}
