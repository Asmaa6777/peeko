import AppKit
import ApplicationServices
import CoreGraphics

@MainActor
public protocol FrontmostAppProvider {
    func frontmostBundleID() -> String?
    /// The front window's title, or nil without Accessibility permission.
    func frontmostWindowTitle() -> String?
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

    public func frontmostWindowTitle() -> String? {
        guard AXIsProcessTrusted(), let app = NSWorkspace.shared.frontmostApplication else { return nil }
        let element = AXUIElementCreateApplication(app.processIdentifier)
        // Don't let a hung app stall the tick.
        AXUIElementSetMessagingTimeout(element, 0.25)
        var window: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXFocusedWindowAttribute as CFString, &window) == .success,
              let window, CFGetTypeID(window) == AXUIElementGetTypeID()
        else { return nil }
        var title: CFTypeRef?
        guard AXUIElementCopyAttributeValue(window as! AXUIElement, kAXTitleAttribute as CFString, &title) == .success
        else { return nil }
        return title as? String
    }
}

public enum Accessibility {
    @MainActor public static var isTrusted: Bool { AXIsProcessTrusted() }

    /// Shows macOS's "allow in Accessibility settings" prompt if not yet allowed.
    @MainActor public static func requestTrust() {
        // The value of kAXTrustedCheckOptionPrompt; the global itself isn't concurrency-safe in Swift 6.
        _ = AXIsProcessTrustedWithOptions(["AXTrustedCheckOptionPrompt": true] as CFDictionary)
    }
}

/// Seconds since the last keyboard or mouse event in this login session. Needs no permission.
public struct SystemIdleTimeProvider: IdleTimeProvider {
    public init() {}

    public func secondsSinceLastInput() -> TimeInterval {
        CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: CGEventType(rawValue: ~0)!)
    }
}
