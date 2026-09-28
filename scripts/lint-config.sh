#!/usr/bin/env bash
set -euo pipefail

script=$(mktemp --suffix=.js)
trap 'rm -f "$script"' EXIT
python3 - "$script" <<'PY'
import pathlib
import re
import sys

page = pathlib.Path('Jellyfin.Plugin.JellyScore/config.html').read_text()
scripts = re.findall(r'<script>(.*?)</script>', page, re.S)
assert len(scripts) == 1, 'expected exactly one inline admin script'
pathlib.Path(sys.argv[1]).write_text(scripts[0])
PY
node_modules/.bin/oxlint --deny-warnings "$script"
