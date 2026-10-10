import AppKit
import PeekoCore

enum TaskDetail {
    static func describe(_ task: TaskDefinition) -> String {
        switch task.type {
        case .manual:
            return "tick it yourself"
        case .appTime, .appLimit:
            let minutes = task.params.minutes.map { "\($0) min" } ?? "time"
            let apps = appNames(task)
            return apps.isEmpty ? minutes : "\(minutes) in \(apps)"
        case .remoteCheck:
            return task.params.source == "githubPush" ? "a public push today" : "remote check"
        }
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
