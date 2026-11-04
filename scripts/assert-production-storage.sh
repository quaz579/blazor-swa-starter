#!/usr/bin/env bash
set -euo pipefail
for root in src/App.Api artifacts/api; do
  if [[ -d "$root" ]] && [[ -n "$(find "$root" -name storage-mode.json -print)" ]]; then
    echo "Production deployment must not contain storage-mode.json in API source or artifacts." >&2
    exit 1
  fi
done
