import AppKit
import PeekoCore

enum TaskDetail {
    static func describe(_ task: TaskDefinition) -> String {
        switch task.type {
        case .manual:
            return "tick it yourself"
        case .appTime:
            let minutes = task.params.minutes.map { "\($0) min" } ?? "time"
            let apps = appNames(task)
            return apps.isEmpty ? minutes : "\(minutes) in \(apps)"
        case .appLimit:
            return "under \(task.params.minutes ?? 0) min \(limitTarget(task))"
        case .remoteCheck:
            return task.params.source == "githubPush" ? "a public push today" : "remote check"
        case .activity:
            let from = task.params.from ?? "", until = task.params.until ?? ""
            return task.params.rule == "startBetween"
                ? "first on the Mac before \(until)"
                : "no screen time \(from)–\(until)"
        case .folderActivity:
            guard let folder = folderName(task) else { return "pick a folder in Catalog" }
            let words = task.params.minWords ?? 0
            return words > 0 ? "\(words) words in \(folder)" : "any note in \(folder)"
        }
    }

    static func done(_ task: TaskDefinition, at date: Date) -> String {
        let time = date.formatted(date: .omitted, time: .shortened)
        switch task.type {
        case .manual: return "done · \(time)"
        case .activity where task.params.rule == "quietBetween": return "looks like you slept on time"
        case .activity: return "up and about at \(time)"
        case .appLimit: return "stayed under · nice"
        default: return "Peeko saw that! · \(time)"
        }
    }

    /// Gentle on purpose: these are hints, not verdicts.
    static func missed(_ task: TaskDefinition) -> String {
        switch task.type {
        case .activity where task.params.rule == "quietBetween": return "up late last night · no worries"
        case .activity: return "a slow morning · no worries"
        case .appLimit: return "over today's \(task.params.minutes ?? 0) min · fresh start tomorrow"
        default: return "not today · no worries"
        }
    }

    /// "of YouTube" for title filters, "in Discord" for whole apps.
    static func limitTarget(_ task: TaskDefinition) -> String {
        if let titles = task.params.titleContains, !titles.isEmpty {
            return "of " + titles.joined(separator: " / ")
        }
        return "in " + appNames(task)
    }

    static func folderName(_ task: TaskDefinition) -> String? {
        guard let path = task.params.path, !path.isEmpty else { return nil }
        return URL(fileURLWithPath: path).lastPathComponent
    }

    static func appNames(_ task: TaskDefinition) -> String {
        (task.params.bundleIDs ?? []).map(appName).joined(separator: " / ")
    }

    static func appName(_ bundleID: String) -> String {
        if let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) {
            return FileManager.default.displayName(atPath: url.path)
                .replacingOccurrences(of: ".app", with: "")
        }
        return bundleID.split(separator: ".").last.map(String.init) ?? bundleID
    }
}
