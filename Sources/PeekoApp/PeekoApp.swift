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

        Window("Peeko", id: MainWindowView.id) {
            MainWindowView()
                .environment(model)
        }
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 960, height: 680)
        .windowResizability(.contentMinSize)
    }
}
