.PHONY: build app run install check dmg icon social site clean

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

social:
	swift scripts/make-social.swift docs/assets/social-preview.png

site:
	mkdir -p site/assets
	cp docs/assets/icon.png docs/assets/social-preview.png site/assets/
	cp install.sh site/install.sh
	cd site && python3 -m http.server 8642

check:
	swift run PeekoChecks

clean:
	rm -rf .build build
