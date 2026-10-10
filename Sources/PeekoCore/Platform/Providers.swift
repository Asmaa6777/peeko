import AppKit
import CoreGraphics

@MainActor
public protocol FrontmostAppProvider {
    func frontmostBundleID() -> String?
}

@MainActor
public protocol IdleTimeProvider {
    func secondsSinceLastInput() -> TimeInterval
}

/// Needs no permission.
public struct SystemFrontmostAppProvider: FrontmostAppProvider {
    public init() {}

    public func frontmostBundleID() -> String? {
        NSWorkspace.shared.frontmostApplication?.bundleIdentifier
    }
}

/// Seconds since the last keyboard or mouse event in this login session. Needs no permission.
public struct SystemIdleTimeProvider: IdleTimeProvider {
    public init() {}

    public func secondsSinceLastInput() -> TimeInterval {
        CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: CGEventType(rawValue: ~0)!)
    }
}
