import SwiftUI

/// A task's icon: an SF Symbol name, or one of the few brand marks SF Symbols doesn't ship.
struct TaskIcon: View {
    let name: String
    let size: CGFloat

    init(_ name: String, size: CGFloat) {
        self.name = name
        self.size = size
    }

    var body: some View {
        switch name {
        case "github":
            GitHubMark().frame(width: size, height: size)
        default:
            Image(systemName: name).font(.system(size: size))
        }
    }
}

/// The GitHub mark (Octicons `mark-github`, 16×16), scaled to fit.
struct GitHubMark: Shape {
    func path(in rect: CGRect) -> Path {
        let side = min(rect.width, rect.height)
        let scale = side / 16
        let origin = CGPoint(x: rect.midX - side / 2, y: rect.midY - side / 2)
        func p(_ x: CGFloat, _ y: CGFloat) -> CGPoint {
            CGPoint(x: origin.x + x * scale, y: origin.y + y * scale)
        }
        var path = Path()
        path.move(to: p(8, 0))
        path.addCurve(to: p(16, 8), control1: p(12.42, 0), control2: p(16, 3.58))
        path.addCurve(to: p(10.55, 15.59), control1: p(16, 11.436), control2: p(13.81, 14.495))
        path.addCurve(to: p(10, 15.21), control1: p(10.15, 15.67), control2: p(10, 15.42))
        path.addCurve(to: p(10.01, 13.01), control1: p(10, 14.94), control2: p(10.01, 14.08))
        path.addCurve(to: p(9.47, 11.53), control1: p(10.01, 12.26), control2: p(9.76, 11.78))
        path.addCurve(to: p(13.12, 7.58), control1: p(11.25, 11.33), control2: p(13.12, 10.65))
        path.addCurve(to: p(12.3, 5.43), control1: p(13.12, 6.7), control2: p(12.81, 5.99))
        path.addCurve(to: p(12.22, 3.31), control1: p(12.38, 5.23), control2: p(12.66, 4.41))
        path.addCurve(to: p(10.02, 4.13), control1: p(12.22, 3.31), control2: p(11.55, 3.09))
        path.addCurve(to: p(8.02, 3.86), control1: p(9.38, 3.95), control2: p(8.7, 3.86))
        path.addCurve(to: p(6.02, 4.13), control1: p(7.34, 3.86), control2: p(6.66, 3.95))
        path.addCurve(to: p(3.82, 3.31), control1: p(4.49, 3.1), control2: p(3.82, 3.31))
        path.addCurve(to: p(3.74, 5.43), control1: p(3.38, 4.41), control2: p(3.66, 5.23))
        path.addCurve(to: p(2.92, 7.58), control1: p(3.23, 5.99), control2: p(2.92, 6.71))
        path.addCurve(to: p(6.56, 11.53), control1: p(2.92, 10.64), control2: p(4.78, 11.33))
        path.addCurve(to: p(6.05, 12.6), control1: p(6.33, 11.73), control2: p(6.12, 12.08))
        path.addCurve(to: p(3.72, 11.94), control1: p(5.59, 12.81), control2: p(4.44, 13.15))
        path.addCurve(to: p(2.49, 11.12), control1: p(3.57, 11.7), control2: p(3.12, 11.11))
        path.addCurve(to: p(2.5, 11.65), control1: p(1.82, 11.13), control2: p(2.22, 11.5))
        path.addCurve(to: p(3.32, 12.78), control1: p(2.84, 11.84), control2: p(3.23, 12.55))
        path.addCurve(to: p(6.01, 13.72), control1: p(3.48, 13.23), control2: p(4, 14.09))
        path.addCurve(to: p(6.02, 15.21), control1: p(6.01, 14.39), control2: p(6.02, 15.02))
        path.addCurve(to: p(5.47, 15.59), control1: p(6.02, 15.42), control2: p(5.87, 15.66))
        path.addCurve(to: p(0, 8), control1: p(2.203, 14.501), control2: p(0, 11.443))
        path.addCurve(to: p(8, 0), control1: p(0, 3.58), control2: p(3.58, 0))
        path.closeSubpath()
        return path
    }
}
