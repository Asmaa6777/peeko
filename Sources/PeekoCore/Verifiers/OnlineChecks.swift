import Foundation
import OSLog

private let log = Logger(subsystem: "com.a7med7x7.peeko", category: "online")

/// Fetches URLs at most once per interval, with ETag / Last-Modified, and remembers the last body.
public actor CachedFetcher {
    private struct Entry {
        var fetchedAt: Date
        var body: Data?
        var etag: String?
        var lastModified: String?
        var problem: String?
    }

    private let http: any HTTPClient
    private var entries: [String: Entry] = [:]

    public init(http: any HTTPClient) {
        self.http = http
    }

    public func body(of url: URL, every interval: TimeInterval, now: Date) async -> Data? {
        await body(for: URLRequest(url: url), every: interval, now: now)
    }

    /// Any request (GET or POST); cached per URL + body.
    public func body(for original: URLRequest, every interval: TimeInterval, now: Date) async -> Data? {
        guard let url = original.url else { return nil }
        let key = Self.key(for: original)
        var entry = entries[key] ?? Entry(fetchedAt: .distantPast)
        if now >= entry.fetchedAt, now.timeIntervalSince(entry.fetchedAt) < interval { return entry.body }

        var request = original
        request.setValue("Peeko-App (+https://peeko.stack42.tech)", forHTTPHeaderField: "User-Agent")
        if let etag = entry.etag { request.setValue(etag, forHTTPHeaderField: "If-None-Match") }
        if let modified = entry.lastModified { request.setValue(modified, forHTTPHeaderField: "If-Modified-Since") }
        entry.fetchedAt = now
        entries[key] = entry

        do {
            let (data, response) = try await http.send(request)
            switch response.statusCode {
            case 200:
                entries[key]?.body = data
                entries[key]?.etag = response.value(forHTTPHeaderField: "ETag")
                entries[key]?.lastModified = response.value(forHTTPHeaderField: "Last-Modified")
                entries[key]?.problem = nil
            case 304:
                entries[key]?.problem = nil
            default:
                entries[key]?.problem = "\(url.host ?? "the site") answered \(response.statusCode)"
            }
            log.info("\(url.host ?? "?", privacy: .public): \(response.statusCode)")
        } catch {
            entries[key]?.problem = "can't reach \(url.host ?? "the site") · offline or a firewall?"
            log.error("\(url.host ?? "?", privacy: .public): \(error.localizedDescription, privacy: .public)")
        }
        return entries[key]?.body
    }

    public func problem(for url: URL) -> String? {
        entries[url.absoluteString]?.problem
    }

    public func problem(for request: URLRequest) -> String? {
        entries[Self.key(for: request)]?.problem
    }

    static func key(for request: URLRequest) -> String {
        let body = request.httpBody.map { String(decoding: $0, as: UTF8.self) } ?? ""
        return (request.url?.absoluteString ?? "") + (body.isEmpty ? "" : " " + body)
    }
}

/// "Solved a Codeforces problem": an accepted (`OK`) submission today, from the official API.
public struct CodeforcesVerifier: Verifier {
    public static let interval: TimeInterval = 10 * 60

    public let definition: TaskDefinition
    let fetcher: CachedFetcher
    let calendar: Calendar

    public init(definition: TaskDefinition, fetcher: CachedFetcher, calendar: Calendar) {
        self.definition = definition
        self.fetcher = fetcher
        self.calendar = calendar
    }

    public func evaluate(context: VerifierContext) async -> TaskStatus {
        if let existing = context.record.statuses[definition.id], existing.isDone { return existing }
        guard let request = Self.request(for: definition.params.username ?? ""),
              let data = await fetcher.body(for: request, every: Self.interval, now: context.now)
        else { return .pending }
        let today = Self.acceptedTimes(data).filter { calendar.isDate($0, inSameDayAs: context.now) }
        return today.min().map { .done(at: $0) } ?? .pending
    }

    public static func request(for handle: String) -> URLRequest? {
        let name = handle.trimmingCharacters(in: .whitespaces)
        guard name.range(of: #"^[A-Za-z0-9_.-]{1,24}$"#, options: .regularExpression) != nil,
              let url = URL(string: "https://codeforces.com/api/user.status?handle=\(name)&from=1&count=50")
        else { return nil }
        return URLRequest(url: url)
    }

    /// Times of submissions with verdict OK.
    public static func acceptedTimes(_ data: Data) -> [Date] {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              json["status"] as? String == "OK",
              let result = json["result"] as? [[String: Any]]
        else { return [] }
        return result.compactMap { submission in
            guard submission["verdict"] as? String == "OK",
                  let seconds = (submission["creationTimeSeconds"] as? NSNumber)?.doubleValue
            else { return nil }
            return Date(timeIntervalSince1970: seconds)
        }
    }
}

/// "Solved a LeetCode problem": an accepted submission today, from LeetCode's public (unofficial)
/// GraphQL query `recentAcSubmissionList`. It can change without notice.
public struct LeetCodeVerifier: Verifier {
    public static let interval: TimeInterval = 10 * 60

    public let definition: TaskDefinition
    let fetcher: CachedFetcher
    let calendar: Calendar

    public init(definition: TaskDefinition, fetcher: CachedFetcher, calendar: Calendar) {
        self.definition = definition
        self.fetcher = fetcher
        self.calendar = calendar
    }

    public func evaluate(context: VerifierContext) async -> TaskStatus {
        if let existing = context.record.statuses[definition.id], existing.isDone { return existing }
        guard let request = Self.request(for: definition.params.username ?? ""),
              let data = await fetcher.body(for: request, every: Self.interval, now: context.now)
        else { return .pending }
        let today = Self.acceptedTimes(data).filter { calendar.isDate($0, inSameDayAs: context.now) }
        return today.min().map { .done(at: $0) } ?? .pending
    }

    public static func request(for username: String) -> URLRequest? {
        let name = username.trimmingCharacters(in: .whitespaces)
        guard name.range(of: #"^[A-Za-z0-9_-]{1,50}$"#, options: .regularExpression) != nil,
              let url = URL(string: "https://leetcode.com/graphql")
        else { return nil }
        let query = "query r($username: String!, $limit: Int!) { recentAcSubmissionList(username: $username, limit: $limit) { title timestamp } }"
        let payload: [String: Any] = ["query": query, "variables": ["username": name, "limit": 20]]
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.httpBody = try? JSONSerialization.data(withJSONObject: payload, options: [.sortedKeys])
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("https://leetcode.com", forHTTPHeaderField: "Referer")
        return request
    }

    /// `data.recentAcSubmissionList[].timestamp`, seconds as strings.
    public static func acceptedTimes(_ data: Data) -> [Date] {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let list = (json["data"] as? [String: Any])?["recentAcSubmissionList"] as? [[String: Any]]
        else { return [] }
        return list.compactMap { item in
            let raw = item["timestamp"]
            let seconds = (raw as? String).flatMap(Double.init) ?? (raw as? NSNumber)?.doubleValue
            return seconds.map { Date(timeIntervalSince1970: $0) }
        }
    }
}

/// The request a fetch-based online check makes, so the app can ask the fetcher what went wrong.
public func onlineRequest(for task: TaskDefinition) -> URLRequest? {
    let p = task.params
    switch p.source {
    case "feed": return FeedVerifier.url(p.url).map { URLRequest(url: $0) }
    case "duolingo": return DuolingoVerifier.url(for: p.username ?? "").map { URLRequest(url: $0) }
    case "codeforces": return CodeforcesVerifier.request(for: p.username ?? "")
    case "leetcode": return LeetCodeVerifier.request(for: p.username ?? "")
    default: return nil
    }
}

/// "Published something": any RSS or Atom feed with an item dated today.
public struct FeedVerifier: Verifier {
    public static let interval: TimeInterval = 30 * 60

    public let definition: TaskDefinition
    let fetcher: CachedFetcher
    let calendar: Calendar

    public init(definition: TaskDefinition, fetcher: CachedFetcher, calendar: Calendar) {
        self.definition = definition
        self.fetcher = fetcher
        self.calendar = calendar
    }

    public func evaluate(context: VerifierContext) async -> TaskStatus {
        if let existing = context.record.statuses[definition.id], existing.isDone { return existing }
        guard let url = Self.url(definition.params.url),
              let data = await fetcher.body(of: url, every: Self.interval, now: context.now)
        else { return .pending }
        let today = FeedDates.parse(data).filter { calendar.isDate($0, inSameDayAs: context.now) }
        return today.min().map { .done(at: $0) } ?? .pending
    }

    public static func url(_ string: String?) -> URL? {
        guard let string, let url = URL(string: string.trimmingCharacters(in: .whitespaces)),
              url.scheme == "https" || url.scheme == "http", url.host != nil
        else { return nil }
        return url
    }
}

/// Item dates from RSS (`pubDate`, `dc:date`) and Atom (`published`, `updated`) feeds.
public enum FeedDates {
    public static func parse(_ data: Data) -> [Date] {
        let delegate = Collector()
        let parser = XMLParser(data: data)
        parser.delegate = delegate
        parser.parse()
        return delegate.dates
    }

    private final class Collector: NSObject, XMLParserDelegate {
        var dates: [Date] = []
        private var depthInItem = 0
        private var text = ""
        private var capturing = false
        private static let dateTags: Set<String> = ["pubDate", "dc:date", "published", "updated"]

        func parser(_ parser: XMLParser, didStartElement name: String, namespaceURI: String?, qualifiedName: String?, attributes: [String: String] = [:]) {
            if name == "item" || name == "entry" { depthInItem += 1 }
            capturing = depthInItem > 0 && Self.dateTags.contains(name)
            text = ""
        }

        func parser(_ parser: XMLParser, foundCharacters string: String) {
            if capturing { text += string }
        }

        func parser(_ parser: XMLParser, didEndElement name: String, namespaceURI: String?, qualifiedName: String?) {
            if capturing, Self.dateTags.contains(name), let date = FeedDates.date(text) { dates.append(date) }
            capturing = false
            if name == "item" || name == "entry" { depthInItem -= 1 }
        }
    }

    static func date(_ raw: String) -> Date? {
        let s = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        let iso = ISO8601DateFormatter()
        if let d = iso.date(from: s) { return d }
        iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = iso.date(from: s) { return d }
        let rfc = DateFormatter()
        rfc.locale = Locale(identifier: "en_US_POSIX")
        for format in ["EEE, dd MMM yyyy HH:mm:ss Z", "EEE, dd MMM yyyy HH:mm:ss zzz", "dd MMM yyyy HH:mm:ss Z", "EEE, dd MMM yyyy HH:mm Z"] {
            rfc.dateFormat = format
            if let d = rfc.date(from: s) { return d }
        }
        return nil
    }
}

/// "Did a Duolingo lesson": the public profile's current streak ends today.
/// Uses Duolingo's unofficial users endpoint, so it can break without notice.
public struct DuolingoVerifier: Verifier {
    public static let interval: TimeInterval = 15 * 60

    public let definition: TaskDefinition
    let fetcher: CachedFetcher
    let calendar: Calendar

    public init(definition: TaskDefinition, fetcher: CachedFetcher, calendar: Calendar) {
        self.definition = definition
        self.fetcher = fetcher
        self.calendar = calendar
    }

    public func evaluate(context: VerifierContext) async -> TaskStatus {
        if let existing = context.record.statuses[definition.id], existing.isDone { return existing }
        guard let url = Self.url(for: definition.params.username ?? ""),
              let data = await fetcher.body(of: url, every: Self.interval, now: context.now),
              let end = Self.streakEndDate(data)
        else { return .pending }
        return end == DayKey.string(for: context.now, calendar: calendar) ? .done(at: context.now) : .pending
    }

    public static func url(for username: String) -> URL? {
        let name = username.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty, name.range(of: #"^[A-Za-z0-9_.-]{1,64}$"#, options: .regularExpression) != nil else { return nil }
        return URL(string: "https://www.duolingo.com/2017-06-30/users?username=\(name)")
    }

    /// `users[0].streakData.currentStreak.endDate`, a "yyyy-MM-dd" string.
    public static func streakEndDate(_ data: Data) -> String? {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let user = (json["users"] as? [[String: Any]])?.first,
              let streak = (user["streakData"] as? [String: Any])?["currentStreak"] as? [String: Any]
        else { return nil }
        return streak["endDate"] as? String
    }
}
