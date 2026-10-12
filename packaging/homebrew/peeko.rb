cask "peeko" do
  version "0.2.0"
  sha256 "38bb38deaf81327d49cf674428ac4d2b3c28cc86ed29510b32cc658636d34865"

  url "https://github.com/STACK-42/peeko/releases/download/v#{version}/Peeko-#{version}.dmg"
  name "Peeko"
  desc "Menu bar app that keeps your daily list and ticks things off by itself"
  homepage "https://peeko.stack42.tech/"

  livecheck do
    url :url
    strategy :github_latest
  end

  depends_on arch: :arm64
  depends_on macos: ">= :sonoma"

  app "Peeko.app"

  # Peeko is ad-hoc signed (no Apple Developer ID yet), so Gatekeeper would block the
  # quarantined copy. The download is pinned by sha256 above; clear the flag so it opens.
  postflight do
    system_command "/usr/bin/xattr",
                   args: ["-dr", "com.apple.quarantine", "#{appdir}/Peeko.app"]
  end

  uninstall quit: "com.a7med7x7.peeko"

  zap trash: [
    "~/Library/Application Support/Peeko",
    "~/Library/Preferences/com.a7med7x7.peeko.plist",
  ]
end
