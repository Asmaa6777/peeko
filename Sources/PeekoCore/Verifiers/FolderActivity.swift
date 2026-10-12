import Foundation

public struct FolderScanResult: Equatable, Sendable {
    public var firstEditToday: Date?
    public var wordsAdded: Int
    /// When a file named with today's date (e.g. 2026-10-12.md) was last modified, if one exists.
    public var dailyNote: Date?
}

/// Watches a notes folder by rescanning it at most once a minute.
///
/// Words added today = for each file changed today, its count now minus its count at the start of
/// the day. "Start of the day" is the first scan after launch or midnight; once a file changes, that
/// starting count is saved (`words/<day>.json`) so restarts don't lose it. Files created today start at 0,
/// even if they were created before Peeko's first scan.
public actor FolderScanner {
    public static let scanInterval: TimeInterval = 60
    public static let defaultExtensions = ["md", "markdown", "txt"]
    static let maxFiles = 20_000
    static let maxFileSize = 5_000_000

    private struct State {
        var day: String
        var lastScan: Date
        var result: FolderScanResult?
        var counts: [String: Int]
    }

    private struct File {
        var path: String
        var url: URL
        var modified: Date
        var created: Date?
    }

    private let store: Store
    private var states: [String: State] = [:]
    private var baselines: WordBaselines?

    public init(store: Store) {
        self.store = store
    }

    public func scan(taskID: String, folder: String, extensions: [String]?, now: Date, calendar: Calendar) -> FolderScanResult? {
        let day = DayKey.string(for: now, calendar: calendar)
        if let state = states[taskID], state.day == day,
           now >= state.lastScan, now.timeIntervalSince(state.lastScan) < Self.scanInterval {
            return state.result
        }

        let exts = Set((extensions ?? Self.defaultExtensions).map { $0.lowercased() })
        guard let files = Self.listFiles(in: URL(fileURLWithPath: folder, isDirectory: true), extensions: exts) else {
            states[taskID] = State(day: day, lastScan: now, result: nil, counts: states[taskID]?.counts ?? [:])
            return nil
        }

        let firstScan = states[taskID] == nil
        var counts = states[taskID]?.counts ?? [:]
        if firstScan {
            for file in files { counts[file.path] = Self.words(in: file.url) }
        }
        if baselines?.day != day { baselines = store.loadWordBaselines(day) }
        var saved = baselines?.tasks[taskID] ?? [:]
        var savedChanged = false

        let dayStart = calendar.startOfDay(for: now)
        let dailyNote = files
            .filter { ($0.path as NSString).lastPathComponent.contains(day) }
            .map(\.modified)
            .max()
        var added = 0
        var firstEdit: Date?
        for file in files where file.modified >= dayStart {
            firstEdit = min(firstEdit ?? file.modified, file.modified)
            let current = firstScan ? counts[file.path] ?? 0 : Self.words(in: file.url)
            let base: Int
            if let known = saved[file.path] {
                base = known
            } else if firstScan, let created = file.created, created >= dayStart {
                // Created today before our first look: all of it was written today.
                base = 0
                saved[file.path] = 0
                savedChanged = true
            } else if let previous = counts[file.path], previous == current {
                base = current
            } else {
                base = counts[file.path] ?? 0
                saved[file.path] = base
                savedChanged = true
            }
            added += max(0, current - base)
            counts[file.path] = current
        }

        if savedChanged, var all = baselines {
            all.tasks[taskID] = saved
            baselines = all
            try? store.save(all)
        }

        let result = FolderScanResult(firstEditToday: firstEdit, wordsAdded: added, dailyNote: dailyNote)
        states[taskID] = State(day: day, lastScan: now, result: result, counts: counts)
        return result
    }

    /// nil if the folder can't be read.
    private static func listFiles(in root: URL, extensions: Set<String>) -> [File]? {
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: root.path, isDirectory: &isDirectory), isDirectory.boolValue,
              let enumerator = FileManager.default.enumerator(
                  at: root,
                  includingPropertiesForKeys: [.isRegularFileKey, .contentModificationDateKey, .creationDateKey, .fileSizeKey],
                  options: [.skipsHiddenFiles, .skipsPackageDescendants]
              )
        else { return nil }

        let prefix = root.standardizedFileURL.path.count + 1
        var files: [File] = []
        for case let url as URL in enumerator {
            guard extensions.contains(url.pathExtension.lowercased()),
                  let values = try? url.resourceValues(forKeys: [.isRegularFileKey, .contentModificationDateKey, .creationDateKey, .fileSizeKey]),
                  values.isRegularFile == true,
                  (values.fileSize ?? 0) <= maxFileSize,
                  let modified = values.contentModificationDate
            else { continue }
            files.append(File(
                path: String(url.standardizedFileURL.path.dropFirst(prefix)),
                url: url,
                modified: modified,
                created: values.creationDate
            ))
            if files.count >= maxFiles { break }
        }
        return files
    }

    static func words(in url: URL) -> Int {
        guard let text = try? String(contentsOf: url, encoding: .utf8) else { return 0 }
        return text.split(whereSeparator: { $0.isWhitespace }).count
    }
}

public struct FolderActivityVerifier: Verifier {
    public let definition: TaskDefinition
    let scanner: FolderScanner
    let calendar: Calendar

    public init(definition: TaskDefinition, scanner: FolderScanner, calendar: Calendar) {
        self.definition = definition
        self.scanner = scanner
        self.calendar = calendar
    }

    public func evaluate(context: VerifierContext) async -> TaskStatus {
        if let existing = context.record.statuses[definition.id], existing.isDone { return existing }
        let p = definition.params
        guard let path = p.path, !path.isEmpty,
              let result = await scanner.scan(taskID: definition.id, folder: path, extensions: p.extensions, now: context.now, calendar: calendar)
        else { return .pending }

        if p.dailyNote == true {
            return result.dailyNote.map { .done(at: $0) } ?? .pending
        }
        let target = p.minWords ?? 0
        if target <= 0 { return result.firstEditToday.map { .done(at: $0) } ?? .pending }
        if result.wordsAdded >= target { return .done(at: context.now) }
        return result.wordsAdded > 0 ? .inProgress(progress: Double(result.wordsAdded) / Double(target)) : .pending
    }
}
