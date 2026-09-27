#!/usr/bin/env bash
set -euo pipefail
bash package.sh
docker compose -f tests/e2e/compose.yaml up -d --wait --wait-timeout 180
