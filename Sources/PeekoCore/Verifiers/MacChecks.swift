import EventKit
import Foundation

// Checks that read local state on this Mac: a tidy folder, Time Machine, git repos, Reminders.

/// "At most N things in a folder (the Desktop by default) after a given time."
public struct FolderTidyVerifier: Verifier {
    public let definition: TaskDefinition
    let calendar: Calendar

    public init(definition: TaskDefinition, calendar: Calendar) {
        self.definition = definition
        self.calendar = calendar
    }

    public func evaluate(context: VerifierContext) async -> TaskStatus {
        if let existing = context.record.statuses[definition.id], existing.isDone { return existing }
        guard let count = Self.itemCount(definition.params.path) else { return .pending }
        if let from = ActivityWindowVerifier.minutes(definition.params.from) {
            let start = calendar.date(byAdding: .minute, value: from, to: calendar.startOfDay(for: context.now))!
            guard context.now >= start else { return .pending }
        }
        return count <= (definition.params.maxItems ?? 5) ? .done(at: context.now) : .pending
    }

    /// Visible items in the folder; `~` is expanded. nil if it can't be read.
    public static func itemCount(_ path: String?) -> Int? {
        let folder = ((path?.isEmpty == false ? path! : "~/Desktop") as NSString).expandingTildeInPath
        return try? FileManager.default.contentsOfDirectory(
            at: URL(fileURLWithPath: folder),
            includingPropertiesForKeys: nil,
            options: [.skipsHiddenFiles]
        ).count
    }
}

/// "Backed up with Time Machine today", from the dates Time Machine records for each destination.
public struct BackupVerifier: Verifier {
    public static let plistPath = "/Library/Preferences/com.apple.TimeMachine.plist"

    public let definition: TaskDefinition
    let calendar: Calendar

    public init(definition: TaskDefinition, calendar: Calendar) {
        self.definition = definition
        self.calendar = calendar
    }

    public func evaluate(context: VerifierContext) async -> TaskStatus {
        if let existing = context.record.statuses[definition.id], existing.isDone { return existing }
        guard let latest = Self.latestBackup() else { return .pending }
        return calendar.isDate(latest, inSameDayAs: context.now) ? .done(at: latest) : .pending
    }

    public static func latestBackup(plist: [String: Any]? = nil) -> Date? {
        let settings = plist ?? (NSDictionary(contentsOfFile: plistPath) as? [String: Any]) ?? [:]
        let destinations = settings["Destinations"] as? [[String: Any]] ?? []
        return destinations.flatMap { $0["SnapshotDates"] as? [Date] ?? [] }.max()
    }
}

/// Finds git repos under a folder (up to two levels deep) and asks each for today's commits.
public actor GitScanner {
    public static let scanInterval: TimeInterval = 300
    static let gitCandidates = [
        "/Library/Developer/CommandLineTools/usr/bin/git",
        "/Applications/Xcode.app/Contents/Developer/usr/bin/git",
        "/opt/homebrew/bin/git",
        "/usr/local/bin/git",
    ]

    private var cache: [String: (scanned: Date, firstCommit: Date?)] = [:]

    public init() {}

    /// Never runs `/usr/bin/git` directly: without developer tools that shim pops an install dialog.
    public static var git: String? {
        gitCandidates.first { FileManager.default.isExecutableFile(atPath: $0) }
    }

    /// The earliest commit today by `author` (any author if empty), across repos under `root`.
    public func firstCommitToday(root: String, author: String, now: Date, calendar: Calendar) -> Date? {
        let key = "\(root)|\(author)|\(DayKey.string(for: now, calendar: calendar))"
        if let hit = cache[key], now >= hit.scanned, now.timeIntervalSince(hit.scanned) < Self.scanInterval {
            return hit.firstCommit
        }
        guard let git = Self.git else { return nil }
        let since = ISO8601DateFormatter().string(from: calendar.startOfDay(for: now))
        var first: Date?
        for repo in Self.repos(under: root) {
            var args = ["-C", repo, "log", "--all", "--since=\(since)", "--format=%ct"]
            if !author.isEmpty { args.append("--author=\(author)") }
            for line in Self.run(git, args).split(separator: "\n") {
                if let seconds = TimeInterval(line) {
                    let date = Date(timeIntervalSince1970: seconds)
                    if calendar.isDate(date, inSameDayAs: now) { first = min(first ?? date, date) }
                }
            }
        }
        cache[key] = (now, first)
        return first
    }

    static func repos(under root: String) -> [String] {
        let fm = FileManager.default
        func isRepo(_ path: String) -> Bool { fm.fileExists(atPath: (path as NSString).appendingPathComponent(".git")) }
        func children(_ path: String) -> [String] {
            ((try? fm.contentsOfDirectory(atPath: path)) ?? [])
                .filter { !$0.hasPrefix(".") }
                .map { (path as NSString).appendingPathComponent($0) }
                .filter { var dir: ObjCBool = false; return fm.fileExists(atPath: $0, isDirectory: &dir) && dir.boolValue }
        }
        if isRepo(root) { return [root] }
        var found: [String] = []
        for child in children(root) {
            if isRepo(child) { found.append(child) } else { found += children(child).filter(isRepo) }
        }
        return found
    }

    public static func run(_ launchPath: String, _ args: [String]) -> String {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: launchPath)
        process.arguments = args
        let out = Pipe()
        process.standardOutput = out
        process.standardError = Pipe()
        do { try process.run() } catch { return "" }
        let data = out.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        return String(data: data, encoding: .utf8) ?? ""
    }
}

public struct GitCommitsVerifier: Verifier {
    public let definition: TaskDefinition
    let scanner: GitScanner
    let calendar: Calendar

    public init(definition: TaskDefinition, scanner: GitScanner, calendar: Calendar) {
        self.definition = definition
        self.scanner = scanner
        self.calendar = calendar
    }

    public func evaluate(context: VerifierContext) async -> TaskStatus {
        if let existing = context.record.statuses[definition.id], existing.isDone { return existing }
        guard let root = definition.params.path, !root.isEmpty,
              let first = await scanner.firstCommitToday(root: root, author: definition.params.author ?? "", now: context.now, calendar: calendar)
        else { return .pending }
        return .done(at: first)
    }
}

/// What Reminders says about today, reduced to the two facts the check needs.
public struct RemindersDay: Equatable, Sendable {
    public var completedToday: [Date]
    public var openDueToday: Int

    public init(completedToday: [Date], openDueToday: Int) {
        self.completedToday = completedToday
        self.openDueToday = openDueToday
    }

    /// Cleared = something was finished today and nothing due today is left open.
    public var status: TaskStatus {
        guard openDueToday == 0, let last = completedToday.max() else { return .pending }
        return .done(at: last)
    }
}

public actor RemindersReader {
    public static let readInterval: TimeInterval = 120
    private var store: EKEventStore?
    private var cache: (read: Date, day: RemindersDay)?

    public init() {}

    public static var isAuthorized: Bool {
        EKEventStore.authorizationStatus(for: .reminder) == .fullAccess
    }

    public func today(now: Date, calendar: Calendar) async -> RemindersDay? {
        guard Self.isAuthorized else { return nil }
        if let cache, now >= cache.read, now.timeIntervalSince(cache.read) < Self.readInterval { return cache.day }
        let store = self.store ?? EKEventStore()
        self.store = store
        let start = calendar.startOfDay(for: now)
        let end = calendar.date(byAdding: .day, value: 1, to: start)!
        let completed = await fetch(store.predicateForCompletedReminders(withCompletionDateStarting: start, ending: end, calendars: nil))
        let open = await fetch(store.predicateForIncompleteReminders(withDueDateStarting: start, ending: end, calendars: nil))
        let day = RemindersDay(completedToday: completed.compactMap(\.self), openDueToday: open.count)
        cache = (now, day)
        return day
    }

    /// Completion dates only, so nothing non-Sendable leaves the callback.
    private func fetch(_ predicate: NSPredicate) async -> [Date?] {
        guard let store else { return [] }
        return await withCheckedContinuation { continuation in
            store.fetchReminders(matching: predicate) { reminders in
                continuation.resume(returning: (reminders ?? []).map(\.completionDate))
            }
        }
    }
}

public struct RemindersVerifier: Verifier {
    public let definition: TaskDefinition
    let reader: RemindersReader
    let calendar: Calendar

    public init(definition: TaskDefinition, reader: RemindersReader, calendar: Calendar) {
        self.definition = definition
        self.reader = reader
        self.calendar = calendar
    }

    public func evaluate(context: VerifierContext) async -> TaskStatus {
        if let existing = context.record.statuses[definition.id], existing.isDone { return existing }
        return await reader.today(now: context.now, calendar: calendar)?.status ?? .pending
    }
}
