import Foundation

/// Judges a time window against today's activity spans.
/// - `quietBetween`: no activity in the window (00:30–05:00 → slept on time; 23:00–24:00 → stopped by 11).
/// - `startBetween`: a stretch of activity starts inside the window (04:00–08:00 → up early).
/// - `gapBetween`: `count` breaks of at least `gapMinutes` inside the window (lunch away, took your breaks).
public struct ActivityWindowVerifier: Verifier {
    public static let rules: Set<String> = ["quietBetween", "startBetween", "gapBetween"]

    public let definition: TaskDefinition
    let calendar: Calendar

    public init(definition: TaskDefinition, calendar: Calendar) {
        self.definition = definition
        self.calendar = calendar
    }

    public func evaluate(context: VerifierContext) async -> TaskStatus {
        status(record: context.record, now: context.now)
    }

    public func status(record: DayRecord, now: Date) -> TaskStatus {
        if let existing = record.statuses[definition.id], existing.isDone { return existing }
        let p = definition.params
        guard let from = Self.minutes(p.from), let until = Self.minutes(p.until), from < until,
              let dayStart = DayKey.startOfDay(record.day, calendar: calendar)
        else { return .pending }

        let fromDate = calendar.date(byAdding: .minute, value: from, to: dayStart)!
        let untilDate = calendar.date(byAdding: .minute, value: until, to: dayStart)!
        let spans = (record.activity ?? []).sorted { $0.start < $1.start }

        switch p.rule {
        case "quietBetween":
            if spans.contains(where: { $0.end >= fromDate && $0.start < untilDate }) { return .failed }
            return now >= untilDate ? .done(at: untilDate) : .pending
        case "startBetween":
            // Only stretches that *start* in the window count, so an all-nighter isn't an early morning.
            if let first = spans.map(\.start).filter({ $0 >= fromDate }).min(), first < untilDate {
                return .done(at: first)
            }
            return now >= untilDate ? .failed : .pending
        case "gapBetween":
            return gaps(spans, from: fromDate, until: untilDate, now: now)
        default:
            return .pending
        }
    }

    /// Breaks are gaps between stretches of activity. Time before the day's first activity isn't a
    /// break (you hadn't started), but a gap that's still going on (you're away right now) counts.
    private func gaps(_ spans: [ActivitySpan], from: Date, until: Date, now: Date) -> TaskStatus {
        let need = TimeInterval(max(definition.params.gapMinutes ?? 30, 1) * 60)
        let wanted = max(definition.params.count ?? 1, 1)
        guard let firstActivity = spans.first?.start else { return now >= until ? .failed : .pending }

        let windowEnd = min(until, now)
        var cursor = max(from, firstActivity)
        var found = 0
        for span in spans where span.end > cursor {
            if span.start >= windowEnd { break }
            if span.start.timeIntervalSince(cursor) >= need {
                found += 1
                if found >= wanted { return .done(at: cursor.addingTimeInterval(need)) }
            }
            cursor = max(cursor, span.end)
        }
        if windowEnd.timeIntervalSince(cursor) >= need {
            found += 1
            if found >= wanted { return .done(at: cursor.addingTimeInterval(need)) }
        }
        return now >= until ? .failed : .pending
    }

    /// "HH:mm" → minutes after midnight. "24:00" is allowed as the end of the day.
    public static func minutes(_ hhmm: String?) -> Int? {
        guard let parts = hhmm?.split(separator: ":"), parts.count == 2,
              let h = Int(parts[0]), let m = Int(parts[1]), (0..<60).contains(m)
        else { return nil }
        if h == 24 && m == 0 { return 24 * 60 }
        guard (0..<24).contains(h) else { return nil }
        return h * 60 + m
    }
}
