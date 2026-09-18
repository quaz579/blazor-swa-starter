#!/usr/bin/env bash
# End-to-end preflight: verifies required tools/versions, that the solution
# builds, that the dev ports are free, that swa-cli.config.json agrees with
# ports.env, and that Playwright browsers are installed. Starts no services
# and leaves the machine exactly as it found it.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./ports.env
source "$SCRIPT_DIR/ports.env"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

FAILURES=0

fail() {
  echo "FAIL: $1" >&2
  FAILURES=$((FAILURES + 1))
}

ok() {
  echo "OK: $1"
}

warn() {
  echo "WARN: $1"
}

echo "== Tool versions =="

if [ -f "$REPO_ROOT/global.json" ]; then
  dotnet_version="$(cd "$REPO_ROOT" && dotnet --version 2>/dev/null || true)"
  if [[ "$dotnet_version" == 9.* ]]; then
    ok "dotnet SDK resolves to $dotnet_version via global.json"
  else
    fail "dotnet --version resolved to '$dotnet_version', expected a 9.x SDK (check global.json / installed SDKs)"
  fi
else
  fail "global.json not found at repo root"
fi

if command -v node >/dev/null 2>&1; then
  node_version="$(node --version)"
  node_major="${node_version#v}"
  node_major="${node_major%%.*}"
  if [ "$node_major" -ge 22 ] 2>/dev/null; then
    ok "node $node_version"
  else
    fail "node $node_version found, need >= 22.x"
  fi
else
  fail "node not found on PATH"
fi

if command -v npm >/dev/null 2>&1; then
  ok "npm $(npm --version)"
else
  fail "npm not found on PATH"
fi

if command -v func >/dev/null 2>&1; then
  func_version="$(func --version 2>/dev/null || true)"
  if [[ "$func_version" == 4.* ]]; then
    ok "Azure Functions Core Tools $func_version"
  else
    fail "func --version returned '$func_version', expected 4.x"
  fi
else
  fail "func (Azure Functions Core Tools) not found on PATH"
fi

if command -v swa >/dev/null 2>&1; then
  ok "SWA CLI $(swa --version 2>/dev/null || echo present)"
else
  fail "swa (Static Web Apps CLI) not found on PATH"
fi

azurite_version=""
if command -v azurite >/dev/null 2>&1; then
  azurite_version="$(azurite --version 2>/dev/null | tail -n1 || true)"
else
  azurite_version="$(npx --yes "azurite@^3.37.0" --version 2>/dev/null | tail -n1 || true)"
fi
if [ -n "$azurite_version" ] && printf '%s\n' "$azurite_version" "3.37.0" | sort -V | head -n1 | grep -q '^3.37.0$'; then
  ok "azurite $azurite_version (>= 3.37.0)"
else
  fail "could not confirm azurite >= 3.37.0 (got '${azurite_version:-none}')"
fi

echo ""
echo "== dotnet build =="

sln="$(find "$REPO_ROOT" -maxdepth 1 -name '*.sln' | head -n1 || true)"
if [ -n "$sln" ]; then
  if (cd "$REPO_ROOT" && dotnet build "$sln" -c Release --nologo >/tmp/validate-build.log 2>&1); then
    ok "dotnet build succeeded ($sln)"
  else
    fail "dotnet build failed, see /tmp/validate-build.log"
    tail -n 30 /tmp/validate-build.log >&2
  fi
else
  warn "no .sln found at repo root yet, skipping dotnet build check"
fi

echo ""
echo "== Ports free =="

listener_pid() {
  lsof -nP -iTCP:"$1" -sTCP:LISTEN -t 2>/dev/null || true
}

occupied=""
for port in "$SWA_PORT" "$WEB_PORT" "$API_PORT" "$AZURITE_BLOB_PORT" "$AZURITE_QUEUE_PORT" "$AZURITE_TABLE_PORT"; do
  pid="$(listener_pid "$port")"
  if [ -n "$pid" ]; then
    occupied="$occupied $port(pid=$pid)"
  fi
done
if [ -n "$occupied" ]; then
  fail "ports already in use:$occupied — run stop-local.sh / stop-e2e.sh first"
else
  ok "all dev ports free ($SWA_PORT, $WEB_PORT, $API_PORT, $AZURITE_BLOB_PORT, $AZURITE_QUEUE_PORT, $AZURITE_TABLE_PORT)"
fi

echo ""
echo "== swa-cli.config.json / ports.env consistency =="

config_path="$REPO_ROOT/swa-cli.config.json"
if [ ! -f "$config_path" ]; then
  fail "swa-cli.config.json not found at repo root"
elif command -v python3 >/dev/null 2>&1; then
  if python3 - "$config_path" "$WEB_PORT" "$API_PORT" "$SWA_PORT" <<'PY'
import json, sys
from urllib.parse import urlparse

path, web_port, api_port, swa_port = sys.argv[1:5]
with open(path) as f:
    cfg = json.load(f)
app = cfg["configurations"]["app"]
web = urlparse(app["appDevserverUrl"]).port
api = urlparse(app["apiDevserverUrl"]).port
swa = app.get("port")

errors = []
if str(web) != web_port:
    errors.append(f"appDevserverUrl port {web} != WEB_PORT {web_port}")
if str(api) != api_port:
    errors.append(f"apiDevserverUrl port {api} != API_PORT {api_port}")
if str(swa) != swa_port:
    errors.append(f"config port {swa} != SWA_PORT {swa_port}")

if errors:
    print("\n".join(errors), file=sys.stderr)
    sys.exit(1)
PY
  then
    ok "swa-cli.config.json ports agree with ports.env"
  else
    fail "swa-cli.config.json ports drifted from ports.env (see errors above)"
  fi
elif command -v node >/dev/null 2>&1; then
  if node -e "
    const fs = require('fs');
    const cfg = JSON.parse(fs.readFileSync(process.argv[1], 'utf8'));
    const app = cfg.configurations.app;
    const webPort = new URL(app.appDevserverUrl).port;
    const apiPort = new URL(app.apiDevserverUrl).port;
    const swaPort = String(app.port);
    const errors = [];
    if (webPort !== process.argv[2]) errors.push('web port mismatch: ' + webPort + ' != ' + process.argv[2]);
    if (apiPort !== process.argv[3]) errors.push('api port mismatch: ' + apiPort + ' != ' + process.argv[3]);
    if (swaPort !== process.argv[4]) errors.push('swa port mismatch: ' + swaPort + ' != ' + process.argv[4]);
    if (errors.length) { console.error(errors.join('\n')); process.exit(1); }
  " "$config_path" "$WEB_PORT" "$API_PORT" "$SWA_PORT"
  then
    ok "swa-cli.config.json ports agree with ports.env"
  else
    fail "swa-cli.config.json ports drifted from ports.env (see errors above)"
  fi
elif command -v jq >/dev/null 2>&1; then
  web_url="$(jq -r '.configurations.app.appDevserverUrl' "$config_path")"
  api_url="$(jq -r '.configurations.app.apiDevserverUrl' "$config_path")"
  cfg_swa_port="$(jq -r '.configurations.app.port' "$config_path")"
  cfg_web_port="${web_url##*:}"
  cfg_api_port="${api_url##*:}"
  if [ "$cfg_web_port" = "$WEB_PORT" ] && [ "$cfg_api_port" = "$API_PORT" ] && [ "$cfg_swa_port" = "$SWA_PORT" ]; then
    ok "swa-cli.config.json ports agree with ports.env"
  else
    fail "swa-cli.config.json ports drifted from ports.env (web=$cfg_web_port api=$cfg_api_port swa=$cfg_swa_port)"
  fi
else
  fail "no JSON parser available (python3, node, or jq) to check swa-cli.config.json drift"
fi

echo ""
echo "== Playwright browsers =="

tests_dir="$REPO_ROOT/tests"
if [ ! -f "$tests_dir/package.json" ]; then
  warn "tests/package.json not found yet, skipping Playwright check"
else
  if [ -d "$tests_dir/node_modules/@playwright/test" ]; then
    ok "tests/node_modules/@playwright/test installed"
  else
    fail "tests/node_modules/@playwright/test missing — run npm ci in tests/"
  fi
  # Only one of the two cache locations exists on any given OS, so find always
  # exits non-zero; under `set -o pipefail` that would sink the whole pipeline
  # regardless of what it matched.
  if { find "$HOME/Library/Caches/ms-playwright" "$HOME/.cache/ms-playwright" \
        -maxdepth 1 -iname 'chromium-*' 2>/dev/null || true; } | grep -q .; then
    ok "Playwright chromium browser cached"
  else
    fail "no Playwright chromium browser cache found — run: (cd tests && npx playwright install --with-deps chromium)"
  fi
fi

echo ""
if [ "$FAILURES" -eq 0 ]; then
  echo "All checks passed."
  exit 0
else
  echo "$FAILURES check(s) failed."
  exit 1
fi
