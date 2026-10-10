.PHONY: build app run clean

build:
	./scripts/build.sh

app: build
	./scripts/bundle.sh
	./scripts/sign.sh

run: app
	-pkill -x Peeko
	open build/Peeko.app

clean:
	rm -rf .build build
