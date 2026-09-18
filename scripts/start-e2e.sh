#!/usr/bin/env bash
# Starts Azurite, the Functions API host, the Blazor dev server (E2E config),
# and the SWA CLI proxy for automated E2E testing (local or CI). Waits for
# each service to actually respond before returning. Idempotent: a service
# already listening on its port is left alone. Detaches on success; use
# stop-e2e.sh to tear down what this script started.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./ports.env
source "$SCRIPT_DIR/ports.env"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
RUN_DIR="$REPO_ROOT/.azurite/run"
LOG_DIR="$REPO_ROOT/.azurite/logs"
AZURITE_DATA_DIR="$REPO_ROOT/.azurite/e2e"
PIDS_FILE="$RUN_DIR/e2e.pids"

mkdir -p "$RUN_DIR" "$LOG_DIR" "$AZURITE_DATA_DIR"
: > "$PIDS_FILE"

listener_pid() {
  lsof -nP -iTCP:"$1" -sTCP:LISTEN -t 2>/dev/null || true
}

record_pid() {
  echo "$1:$2:$3" >> "$PIDS_FILE"
}

# curl -s without -f: any HTTP response (even a 4xx) proves the server is up.
port_responds() {
  curl -s -o /dev/null "$1"
}

http_ok() {
  curl -sf -o /dev/null "$1"
}

wait_for() {
  local name="$1" timeout="$2" log="$3"; shift 3
  local i=0
  until "$@" >/dev/null 2>&1; do
    i=$((i + 1))
    if [ "$i" -ge "$timeout" ]; then
      echo "ERROR: $name did not become ready within ${timeout}s" >&2
      [ -f "$log" ] && tail -n 20 "$log" >&2
      exit 1
    fi
    sleep 1
  done
}

echo "Starting E2E test environment..."

if [ -n "$(listener_pid "$AZURITE_BLOB_PORT")" ]; then
  echo "Azurite already running on port $AZURITE_BLOB_PORT, leaving it alone"
else
  npx --yes "azurite@^3.37.0" --silent \
    --location "$AZURITE_DATA_DIR" \
    --blobPort "$AZURITE_BLOB_PORT" \
    --queuePort "$AZURITE_QUEUE_PORT" \
    --tablePort "$AZURITE_TABLE_PORT" \
    > "$LOG_DIR/azurite-e2e.log" 2>&1 &
  azurite_pid=$!
  record_pid "azurite" "$azurite_pid" "$AZURITE_BLOB_PORT"
  wait_for "Azurite" 30 "$LOG_DIR/azurite-e2e.log" port_responds "http://127.0.0.1:$AZURITE_BLOB_PORT/"
  echo "Azurite started (pid $azurite_pid) - http://127.0.0.1:$AZURITE_BLOB_PORT"
fi

if [ -n "$(listener_pid "$API_PORT")" ]; then
  echo "Functions API already running on port $API_PORT, leaving it alone"
else
  (cd "$REPO_ROOT/src/App.Api" && exec func start --port "$API_PORT") > "$LOG_DIR/func-e2e.log" 2>&1 &
  func_pid=$!
  record_pid "func" "$func_pid" "$API_PORT"
  wait_for "Functions API" 60 "$LOG_DIR/func-e2e.log" http_ok "http://localhost:$API_PORT/api/health"
  echo "Functions API started (pid $func_pid) - http://localhost:$API_PORT"
fi

if [ -n "$(listener_pid "$WEB_PORT")" ]; then
  echo "Blazor dev server already running on port $WEB_PORT, leaving it alone"
else
  # ASPNETCORE_ENVIRONMENT=E2E makes the WASM host load appsettings.E2E.json
  # instead of appsettings.Development.json.
  (cd "$REPO_ROOT/src/App.Web" && ASPNETCORE_ENVIRONMENT=E2E exec dotnet run --urls "http://localhost:$WEB_PORT") > "$LOG_DIR/web-e2e.log" 2>&1 &
  web_pid=$!
  record_pid "web" "$web_pid" "$WEB_PORT"
  wait_for "Blazor dev server" 60 "$LOG_DIR/web-e2e.log" http_ok "http://localhost:$WEB_PORT/"
  echo "Blazor dev server started (pid $web_pid) - http://localhost:$WEB_PORT"
fi

if [ -n "$(listener_pid "$SWA_PORT")" ]; then
  echo "SWA CLI already running on port $SWA_PORT, leaving it alone"
else
  # swa-cli.config.json already points appDevserverUrl/apiDevserverUrl at the
  # servers started above, so swa start proxies them rather than relaunching.
  (cd "$REPO_ROOT" && exec swa start) > "$LOG_DIR/swa-e2e.log" 2>&1 &
  swa_pid=$!
  record_pid "swa" "$swa_pid" "$SWA_PORT"
  wait_for "SWA CLI" 30 "$LOG_DIR/swa-e2e.log" http_ok "http://localhost:$SWA_PORT/"
  echo "SWA CLI started (pid $swa_pid) - http://localhost:$SWA_PORT"
fi

echo ""
echo "E2E test environment ready:"
echo "  - Azurite:  http://127.0.0.1:$AZURITE_BLOB_PORT"
echo "  - Functions API: http://localhost:$API_PORT"
echo "  - Blazor:   http://localhost:$WEB_PORT"
echo "  - SWA CLI:  http://localhost:$SWA_PORT"
echo ""
echo "Run tests: cd tests && npm test"
echo "Stop with: $SCRIPT_DIR/stop-e2e.sh"
