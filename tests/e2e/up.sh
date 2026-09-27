#!/usr/bin/env bash
set -euo pipefail
bash package.sh linux-x64
docker compose -f tests/e2e/compose.yaml up -d --wait --wait-timeout 180
