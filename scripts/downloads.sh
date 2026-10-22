#!/usr/bin/env bash
# Prints how many times each release file has been downloaded, from GitHub's own counts.
# Covers the site's buttons, the Releases page and install.sh alike. Set GITHUB_TOKEN to
# skip the 60-requests-an-hour limit for unauthenticated calls.
set -euo pipefail

REPO="${REPO:-STACK-42/peeko}"
AUTH=()
[ -n "${GITHUB_TOKEN:-}" ] && AUTH=(-H "Authorization: Bearer $GITHUB_TOKEN")

curl -fsSL "${AUTH[@]}" -H "Accept: application/vnd.github+json" -H "User-Agent: Peeko-App" \
  "https://api.github.com/repos/$REPO/releases?per_page=100" | python3 -c '
import json, sys

releases = json.load(sys.stdin)
mac = win = 0
for r in releases:
    tag = r["tag_name"]
    for a in r["assets"]:
        name, n = a["name"], a["download_count"]
        if name.endswith(".sha256"):
            continue
        print(f"{tag:<12} {name:<28} {n:>7}")
        if tag.startswith("windows-"):
            win += n
        else:
            mac += n
print()
for label, n in (("Mac", mac), ("Windows", win), ("Total", mac + win)):
    print(f"{label:<41} {n:>7}")
'
