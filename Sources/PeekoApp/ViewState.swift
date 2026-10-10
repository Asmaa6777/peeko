import SwiftUI

// The macOS 27 SDK routes `@State` through a SwiftUI macro whose plugin ships only with Xcode,
// not Command Line Tools. Aliasing the property wrapper sidesteps the macro.
typealias ViewState<Value> = SwiftUI.State<Value>
