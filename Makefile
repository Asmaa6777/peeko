.PHONY: build app run install check dmg icon clean

build:
	./scripts/build.sh

app: build
	./scripts/bundle.sh
	./scripts/sign.sh

run: app
	-pkill -x Peeko
	open build/Peeko.app

install: app
	-osascript -e 'quit app "Peeko"' 2>/dev/null
	rm -rf /Applications/Peeko.app
	cp -R build/Peeko.app /Applications/
	open /Applications/Peeko.app

dmg: app
	./scripts/dmg.sh

icon:
	./scripts/icon.sh

check:
	swift run PeekoChecks

clean:
	rm -rf .build build
