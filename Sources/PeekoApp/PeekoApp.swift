import SwiftUI

@main
struct PeekoApp: App {
    @ViewState private var model = AppModel()

    var body: some Scene {
        MenuBarExtra {
            RootView()
                .environment(model)
        } label: {
            Image(nsImage: MenuBarIcon.image(for: model.mood))
        }
        .menuBarExtraStyle(.window)
    }
}
