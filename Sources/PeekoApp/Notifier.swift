import Foundation
import OSLog
import UserNotifications

private let log = Logger(subsystem: "com.a7med7x7.peeko", category: "notifications")

/// Wraps UNUserNotificationCenter. Only works from the bundled .app (it needs a bundle ID).
@MainActor
final class Notifier: NSObject, UNUserNotificationCenterDelegate {
    private let center = UNUserNotificationCenter.current()

    override init() {
        super.init()
        center.delegate = self
    }

    func authorizationStatus() async -> UNAuthorizationStatus {
        let status = await center.notificationSettings().authorizationStatus
        log.info("authorization status: \(status.rawValue, privacy: .public)")
        return status
    }

    func requestAuthorization() async -> Bool {
        do {
            let granted = try await center.requestAuthorization(options: [.alert, .sound])
            log.info("authorization granted: \(granted, privacy: .public)")
            return granted
        } catch {
            log.error("authorization request failed: \(error.localizedDescription, privacy: .public)")
            return false
        }
    }

    func post(id: String, title: String, body: String) {
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        content.sound = .default
        center.add(UNNotificationRequest(identifier: id, content: content, trigger: nil)) { error in
            if let error {
                log.error("post \(id, privacy: .public) failed: \(error.localizedDescription, privacy: .public)")
            } else {
                log.info("posted \(id, privacy: .public)")
            }
        }
    }

    // Show banners even while the popover is open (the app counts as active then).
    nonisolated func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification
    ) async -> UNNotificationPresentationOptions {
        [.banner, .sound]
    }
}
