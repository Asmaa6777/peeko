.PHONY: build app run check dmg icon clean

build:
	./scripts/build.sh

app: build
	./scripts/bundle.sh
	./scripts/sign.sh

run: app
	-pkill -x Peeko
	open build/Peeko.app

dmg: app
	./scripts/dmg.sh

icon:
	./scripts/icon.sh

check:
	swift run PeekoChecks

clean:
	rm -rf .build build
