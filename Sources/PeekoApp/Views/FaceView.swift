import SwiftUI

/// Peeko's head hanging from the top edge. Geometry is in the brand file's 72×60 units.
struct FaceView: View {
    let mood: Mood
    var width: CGFloat = 36

    private var s: CGFloat { width / 72 }

    var body: some View {
        ZStack(alignment: .topLeading) {
            UnevenRoundedRectangle(
                topLeadingRadius: 30 * s, bottomLeadingRadius: 24 * s,
                bottomTrailingRadius: 24 * s, topTrailingRadius: 30 * s
            )
            .fill(Theme.green)
            .place(x: 6, y: -12, w: 60, h: 62, s: s)

            switch mood {
            case .dozing:
                Capsule().fill(Theme.night).place(x: 18, y: 24, w: 12, h: 3, s: s)
                Capsule().fill(Theme.night).place(x: 42, y: 24, w: 12, h: 3, s: s)
            case .looking:
                Circle().fill(.white).place(x: 16, y: 16, w: 16, h: 16, s: s)
                Circle().fill(.white).place(x: 40, y: 16, w: 16, h: 16, s: s)
                Circle().fill(Theme.night).place(x: 23, y: 19, w: 7, h: 7, s: s)
                Circle().fill(Theme.night).place(x: 47, y: 19, w: 7, h: 7, s: s)
            case .beaming:
                Arch().stroke(Theme.night, style: StrokeStyle(lineWidth: 3 * s, lineCap: .round))
                    .place(x: 17.5, y: 21.5, w: 13, h: 7.5, s: s)
                Arch().stroke(Theme.night, style: StrokeStyle(lineWidth: 3 * s, lineCap: .round))
                    .place(x: 41.5, y: 21.5, w: 13, h: 7.5, s: s)
                UnevenRoundedRectangle(bottomLeadingRadius: 6 * s, bottomTrailingRadius: 6 * s)
                    .fill(Theme.night)
                    .place(x: 30, y: 34, w: 12, h: 6, s: s)
            }
        }
        .frame(width: 72 * s, height: 60 * s, alignment: .topLeading)
        .clipped()
        .accessibilityHidden(true)
    }
}

private struct Arch: Shape {
    func path(in rect: CGRect) -> Path {
        var p = Path()
        p.move(to: CGPoint(x: rect.minX, y: rect.maxY))
        p.addQuadCurve(
            to: CGPoint(x: rect.maxX, y: rect.maxY),
            control: CGPoint(x: rect.midX, y: rect.minY - rect.height)
        )
        return p
    }
}

private extension View {
    func place(x: CGFloat, y: CGFloat, w: CGFloat, h: CGFloat, s: CGFloat) -> some View {
        frame(width: w * s, height: h * s).offset(x: x * s, y: y * s)
    }
}
