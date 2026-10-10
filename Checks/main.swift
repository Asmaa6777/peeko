import Foundation
import PeekoCore

// Run with `make check`. Exits non-zero on any failure.

var failures = 0

@MainActor func group(_ name: String) {
    print("\n\(name)")
}

@MainActor func check(_ ok: Bool, _ message: String, file: StaticString = #file, line: UInt = #line) {
    print(ok ? "  ✓ \(message)" : "  ✗ \(message)  (\(file):\(line))")
    if !ok { failures += 1 }
}

@MainActor final class FakeFrontmost: FrontmostAppProvider {
    var bundleID: String?
    init(_ bundleID: String?) { self.bundleID = bundleID }
    func frontmostBundleID() -> String? { bundleID }
}

@MainActor final class FakeIdle: IdleTimeProvider {
    var seconds: TimeInterval
    init(_ seconds: TimeInterval) { self.seconds = seconds }
    func secondsSinceLastInput() -> TimeInterval { seconds }
}

actor FakeHTTP: HTTPClient {
    struct Reply { var status: Int; var body: String; var etag: String? }
    var replies: [Reply]
    private(set) var requests: [URLRequest] = []

    init(_ replies: [Reply]) { self.replies = replies }

    func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        requests.append(request)
        guard !replies.isEmpty else { throw URLError(.notConnectedToInternet) }
        let reply = replies.removeFirst()
        let headers = reply.etag.map { ["ETag": $0] } ?? [:]
        return (Data(reply.body.utf8), HTTPURLResponse(url: request.url!, statusCode: reply.status, httpVersion: nil, headerFields: headers)!)
    }
}

func offlineGitHub() -> GitHubEventsClient { GitHubEventsClient(http: FakeHTTP([])) }

func eventsJSON(_ events: [(String, String)]) -> String {
    "[" + events.map { #"{"type":"\#($0.0)","created_at":"\#($0.1)","id":"1"}"# }.joined(separator: ",") + "]"
}

var calendar = Calendar(identifier: .gregorian)
calendar.timeZone = TimeZone(identifier: "Asia/Riyadh")!

@MainActor func at(_ day: Int, _ hour: Int, _ minute: Int, _ second: Int = 0) -> Date {
    calendar.date(from: DateComponents(year: 2026, month: 10, day: day, hour: hour, minute: minute, second: second))!
}

let preview = "com.apple.Preview"
let read = TaskDefinition(
    id: "read-pdfs", title: "Read", type: .appTime,
    params: TaskParams(bundleIDs: [preview], minutes: 30, idleCutoffSeconds: 120), icon: "book"
)
let deepWork = TaskDefinition(
    id: "deep-work", title: "Deep work", type: .appTime,
    params: TaskParams(bundleIDs: ["com.microsoft.VSCode", "com.apple.Terminal"], minutes: 90), icon: "laptopcomputer"
)
let gym = TaskDefinition(id: "gym", title: "Gym", type: .manual, icon: "dumbbell")

@MainActor func sample(_ date: Date, _ front: String?, idle: TimeInterval = 5) -> ActivitySample {
    ActivitySample(now: date, frontmostBundleID: front, secondsSinceInput: idle)
}

@MainActor func tempStore() -> Store {
    Store(directory: FileManager.default.temporaryDirectory.appendingPathComponent("peeko-checks-\(UUID())"))
}

// MARK: - ActivityTracker

group("ActivityTracker")
do {
    var tracker = ActivityTracker()
    var record = DayRecord(day: "2026-10-10")
    let t0 = at(10, 9, 0)

    tracker.record(sample(t0, preview), tasks: [read], into: &record)
    check(record.accumulatedSeconds["read-pdfs"] == nil, "first tick only starts the clock")

    tracker.record(sample(t0.addingTimeInterval(10), preview), tasks: [read], into: &record)
    check(record.accumulatedSeconds["read-pdfs"] == 10, "counts 10s when the app is in front and you're active")

    tracker.record(sample(t0.addingTimeInterval(20), preview, idle: 121), tasks: [read], into: &record)
    check(record.accumulatedSeconds["read-pdfs"] == 10, "doesn't count past the idle cutoff")

    tracker.record(sample(t0.addingTimeInterval(30), preview, idle: 119), tasks: [read], into: &record)
    check(record.accumulatedSeconds["read-pdfs"] == 20, "still counts while idle is under the cutoff")

    tracker.record(sample(t0.addingTimeInterval(40), "com.apple.Safari"), tasks: [read], into: &record)
    check(record.accumulatedSeconds["read-pdfs"] == 20, "doesn't count other apps")

    tracker.record(sample(t0.addingTimeInterval(40 + 2 * 3600), preview), tasks: [read], into: &record)
    check(record.accumulatedSeconds["read-pdfs"] == 30, "a 2-hour sleep gap is capped at 10s")

    tracker.pause()
    tracker.record(sample(t0.addingTimeInterval(9000), preview), tasks: [read], into: &record)
    check(record.accumulatedSeconds["read-pdfs"] == 30, "after pause() the next tick credits nothing")

    tracker.record(sample(t0.addingTimeInterval(8990), preview), tasks: [read], into: &record)
    check(record.accumulatedSeconds["read-pdfs"] == 30, "a clock going backwards credits nothing")

    tracker.record(sample(t0.addingTimeInterval(9000), preview), tasks: [read], into: &record)
    tracker.record(sample(t0.addingTimeInterval(9004), preview), tasks: [read], into: &record)
    check(record.accumulatedSeconds["read-pdfs"] == 44, "short ticks credit their real length")

    var record2 = DayRecord(day: "2026-10-10")
    var tracker2 = ActivityTracker()
    tracker2.record(sample(t0, "com.apple.Terminal"), tasks: [read, deepWork], into: &record2)
    tracker2.record(sample(t0.addingTimeInterval(10), "com.apple.Terminal", idle: 100), tasks: [read, deepWork], into: &record2)
    check(record2.accumulatedSeconds["deep-work"] == 10, "any of several bundle IDs counts; default cutoff is 120s")
    check(record2.accumulatedSeconds["read-pdfs"] == nil, "only matching tasks are credited")
}

// MARK: - AppTimeVerifier

group("AppTimeVerifier")
do {
    let verifier = AppTimeVerifier(definition: read)
    let now = at(10, 12, 0)
    var record = DayRecord(day: "2026-10-10")

    check(await verifier.evaluate(context: .init(now: now, record: record)) == .pending, "no time yet → pending")

    record.accumulatedSeconds["read-pdfs"] = 900
    check(await verifier.evaluate(context: .init(now: now, record: record)) == .inProgress(progress: 0.5), "15 of 30 min → 50%")

    record.accumulatedSeconds["read-pdfs"] = 1800
    check(await verifier.evaluate(context: .init(now: now, record: record)) == .done(at: now), "30 of 30 min → done")

    let earlier = at(10, 11, 0)
    record.statuses["read-pdfs"] = .done(at: earlier)
    check(await verifier.evaluate(context: .init(now: now, record: record)) == .done(at: earlier), "keeps the original done time")
}

// MARK: - Scheduler

group("Scheduler")
do {
    let store = tempStore()
    let front = FakeFrontmost(preview)
    let idle = FakeIdle(5)
    let start = at(10, 9, 0)
    let scheduler = Scheduler(store: store, frontmost: front, idle: idle, github: offlineGitHub(), calendar: calendar, now: start)
    scheduler.tasks = [read, gym]

    for i in 0...90 { await scheduler.tick(now: start.addingTimeInterval(Double(i) * 10)) }
    check(scheduler.record.accumulatedSeconds["read-pdfs"] == 900, "15 minutes of ticks → 900s")
    check(scheduler.record.statuses["read-pdfs"] == .inProgress(progress: 0.5), "status is in progress at 50%")

    scheduler.setManual(true, for: gym, now: start.addingTimeInterval(905))
    for i in 91...180 { await scheduler.tick(now: start.addingTimeInterval(Double(i) * 10)) }
    let doneAt = start.addingTimeInterval(1800)
    check(scheduler.record.statuses["read-pdfs"] == .done(at: doneAt), "30 minutes in Preview flips the task to done")
    check(scheduler.record.statuses["gym"]?.isDone == true, "ticks don't undo a manual tap")

    await scheduler.tick(now: start.addingTimeInterval(1810))
    check(scheduler.record.statuses["read-pdfs"] == .done(at: doneAt), "done time doesn't move on later ticks")

    let reloaded = store.loadDay("2026-10-10")
    check(reloaded.accumulatedSeconds["read-pdfs"] == 1810, "progress is saved to disk")

    scheduler.pause()
    await scheduler.tick(now: start.addingTimeInterval(1810 + 3600))
    check(scheduler.record.accumulatedSeconds["read-pdfs"] == 1810, "no credit for the tick after sleep")
}

group("Scheduler: midnight rollover")
do {
    let store = tempStore()
    let scheduler = Scheduler(store: store, frontmost: FakeFrontmost(preview), idle: FakeIdle(5), github: offlineGitHub(), calendar: calendar, now: at(10, 23, 59, 40))
    scheduler.tasks = [read, gym]
    scheduler.setManual(true, for: gym, now: at(10, 23, 59, 40))

    await scheduler.tick(now: at(10, 23, 59, 40))
    await scheduler.tick(now: at(10, 23, 59, 50))
    check(scheduler.record.day == "2026-10-10", "still Oct 10 before midnight")

    await scheduler.tick(now: at(11, 0, 0, 0))
    check(scheduler.record.day == "2026-10-11", "rolls over to Oct 11 at local midnight")
    check(scheduler.record.statuses["gym"] == nil, "manual ticks reset on the new day")

    let yesterday = store.loadDay("2026-10-10")
    check(yesterday.accumulatedSeconds["read-pdfs"] == 10, "yesterday's time stays in yesterday's file")
    check(yesterday.statuses["gym"]?.isDone == true, "yesterday's ticks are kept")
    check(scheduler.record.accumulatedSeconds["read-pdfs"] == 10, "the tick that crossed midnight counts toward the new day")

    scheduler.rolloverIfNeeded(now: at(11, 0, 0, 5))
    check(scheduler.record.day == "2026-10-11", "rollover is idempotent within the day")
}

// MARK: - GitHub

group("GitHubEventsClient")
do {
    let http = FakeHTTP([
        .init(status: 200, body: eventsJSON([("PushEvent", "2026-10-10T08:00:00Z")]), etag: "\"abc\""),
        .init(status: 304, body: "", etag: "\"abc\""),
        .init(status: 500, body: "oops", etag: nil),
    ])
    let client = GitHubEventsClient(http: http)
    let t0 = at(10, 12, 0)

    let first = await client.events(for: "octocat", now: t0)
    check(first?.count == 1, "first call fetches and decodes events")
    let firstRequest = await http.requests.first
    check(firstRequest?.url?.absoluteString == "https://api.github.com/users/octocat/events/public?per_page=100", "hits the public events endpoint")
    check(firstRequest?.value(forHTTPHeaderField: "User-Agent") == "Peeko-App", "sends a User-Agent")
    check(firstRequest?.value(forHTTPHeaderField: "If-None-Match") == nil, "no ETag on the first request")

    _ = await client.events(for: "octocat", now: t0.addingTimeInterval(599))
    check(await client.requestCount == 1, "doesn't refetch within 10 minutes")

    let afterNotModified = await client.events(for: "octocat", now: t0.addingTimeInterval(600))
    check(await client.requestCount == 2, "refetches after 10 minutes")
    check(await http.requests.last?.value(forHTTPHeaderField: "If-None-Match") == "\"abc\"", "sends the saved ETag")
    check(afterNotModified == first, "304 keeps the cached events")

    let afterError = await client.events(for: "octocat", now: t0.addingTimeInterval(1200))
    check(afterError == first, "a server error keeps the cached events")

    check(await client.events(for: "../evil", now: t0) == nil, "rejects invalid usernames without a request")
    check(await client.requestCount == 3, "no request was made for the invalid username")
    check(GitHubEventsClient.isValidUsername("a7med-7x7") && !GitHubEventsClient.isValidUsername("-bad"), "username validation")
}

group("GitHubPushVerifier")
do {
    let push = TaskDefinition(id: "github-push", title: "Push", type: .remoteCheck,
                              params: TaskParams(source: "githubPush", username: ""), icon: "arrow.up.circle")
    func verdict(_ events: [(String, String)], now: Date, username: String = "octocat") async -> TaskStatus {
        let client = GitHubEventsClient(http: FakeHTTP([.init(status: 200, body: eventsJSON(events), etag: nil)]))
        let env = VerifierEnvironment(githubUsername: username, github: client, calendar: calendar)
        return await makeVerifier(for: push, environment: env)!.evaluate(context: .init(now: now, record: DayRecord(day: "x")))
    }

    // 01:00 on Oct 11 in Riyadh (UTC+3) is 22:00 UTC on Oct 10.
    let lateNight = at(11, 2, 0)
    check(await verdict([("PushEvent", "2026-10-10T22:00:00Z")], now: lateNight) == .done(at: at(11, 1, 0)),
          "a push at 01:00 local counts for that local day, though UTC says yesterday")
    check(await verdict([("PushEvent", "2026-10-10T20:00:00Z")], now: lateNight) == .pending,
          "a push from yesterday (local) doesn't count")
    check(await verdict([("WatchEvent", "2026-10-11T05:00:00Z")], now: at(11, 12, 0)) == .pending,
          "only PushEvent counts")
    check(await verdict([("PushEvent", "2026-10-11T09:00:00Z"), ("PushEvent", "2026-10-11T06:00:00Z")], now: at(11, 13, 0)) == .done(at: at(11, 9, 0)),
          "done time is the first push of the day")
    check(await verdict([("PushEvent", "2026-10-11T06:00:00Z")], now: at(11, 13, 0), username: "") == .pending,
          "no username → stays pending")
    check(isVerifiable(push), "githubPush is verifiable")
}

group("Scheduler: completion callback")
do {
    let push = TaskDefinition(id: "github-push", title: "Push", type: .remoteCheck,
                              params: TaskParams(source: "githubPush", username: ""), icon: "arrow.up.circle")
    let http = FakeHTTP([.init(status: 200, body: eventsJSON([("PushEvent", "2026-10-10T06:00:00Z")]), etag: nil)])
    let scheduler = Scheduler(store: tempStore(), frontmost: FakeFrontmost(nil), idle: FakeIdle(5),
                              github: GitHubEventsClient(http: http), calendar: calendar, now: at(10, 12, 0))
    scheduler.tasks = [push, gym]
    scheduler.githubUsername = "octocat"
    var completed: [String] = []
    scheduler.onAutoComplete = { completed.append($0.id) }

    scheduler.setManual(true, for: gym, now: at(10, 12, 0))
    await scheduler.tick(now: at(10, 12, 0))
    await scheduler.tick(now: at(10, 12, 0, 10))
    check(scheduler.record.statuses["github-push"]?.isDone == true, "a push today marks the task done")
    check(completed == ["github-push"], "completion fires once, and never for manual taps")
}

// MARK: - Settings and the evening nudge

group("AppSettings and EveningNudge")
do {
    let legacy = try! JSONDecoder().decode(AppSettings.self, from: Data(#"{"schemaVersion":1,"githubUsername":"octocat"}"#.utf8))
    check(legacy.githubUsername == "octocat" && legacy.eveningNudgeEnabled && legacy.eveningNudgeMinutes == 21 * 60,
          "missing fields fall back to defaults")

    let store = tempStore()
    var settings = AppSettings()
    settings.hasCompletedOnboarding = true
    try! store.save(settings)
    check(store.loadSettings() == settings, "settings round-trip")

    let s = AppSettings()
    check(!EveningNudge.isDue(now: at(10, 20, 59), settings: s, lastNudgeDay: nil, remaining: 2, calendar: calendar), "not before 21:00")
    check(EveningNudge.isDue(now: at(10, 21, 0), settings: s, lastNudgeDay: nil, remaining: 2, calendar: calendar), "due at 21:00 with tasks left")
    check(!EveningNudge.isDue(now: at(10, 22, 0), settings: s, lastNudgeDay: "2026-10-10", remaining: 2, calendar: calendar), "only once per day")
    check(!EveningNudge.isDue(now: at(10, 21, 30), settings: s, lastNudgeDay: nil, remaining: 0, calendar: calendar), "not when everything's done")
    var off = AppSettings(); off.eveningNudgeEnabled = false
    check(!EveningNudge.isDue(now: at(10, 21, 30), settings: off, lastNudgeDay: nil, remaining: 2, calendar: calendar), "not when turned off")
}

print(failures == 0 ? "\nAll checks passed." : "\n\(failures) check(s) failed.")
exit(failures == 0 ? 0 : 1)
