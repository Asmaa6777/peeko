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
    ]
)
