// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "Peeko",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "PeekoApp", targets: ["PeekoApp"]),
        .library(name: "PeekoCore", targets: ["PeekoCore"]),
    ],
    targets: [
        .target(name: "PeekoCore"),
        .executableTarget(name: "PeekoApp", dependencies: ["PeekoCore"]),
        // Stand-in for a test target: XCTest and Swift Testing need Xcode, not Command Line Tools.
        .executableTarget(name: "PeekoChecks", dependencies: ["PeekoCore"], path: "Checks"),
    ]
)
