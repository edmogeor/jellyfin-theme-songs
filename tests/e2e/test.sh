#!/usr/bin/env bash
set -euo pipefail
docker compose -f tests/e2e/compose.yaml down -v
bash tests/e2e/up.sh
bash tests/e2e/fixtures.sh
python3 -u tests/e2e/smoke.py
