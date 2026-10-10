import Foundation

public struct Store: Sendable {
    public let directory: URL

    public init(directory: URL) {
        self.directory = directory
    }

    public static var defaultDirectory: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Peeko", isDirectory: true)
    }

    var tasksURL: URL { directory.appendingPathComponent("tasks.json") }
    var daysDirectory: URL { directory.appendingPathComponent("days", isDirectory: true) }

    func dayURL(_ key: String) -> URL {
        daysDirectory.appendingPathComponent("\(key).json")
    }

    public func loadTaskList() -> TaskList {
        load(TaskList.self, from: tasksURL) ?? TaskList()
    }

    public func save(_ list: TaskList) throws {
        try write(list, to: tasksURL)
    }

    public func loadDay(_ key: String) -> DayRecord {
        load(DayRecord.self, from: dayURL(key)) ?? DayRecord(day: key)
    }

    public func save(_ record: DayRecord) throws {
        try write(record, to: dayURL(record.day))
    }

    private func load<T: Decodable>(_ type: T.Type, from url: URL) -> T? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        do {
            return try decoder.decode(type, from: data)
        } catch {
            // Keep the unreadable file instead of silently overwriting the user's data.
            let stamp = Int(Date().timeIntervalSince1970)
            let aside = url.appendingPathExtension("corrupt-\(stamp)")
            try? FileManager.default.moveItem(at: url, to: aside)
            return nil
        }
    }

    private func write<T: Encodable>(_ value: T, to url: URL) throws {
        try FileManager.default.createDirectory(
            at: url.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .iso8601
        try encoder.encode(value).write(to: url, options: .atomic)
    }
}
