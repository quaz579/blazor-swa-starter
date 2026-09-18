#!/usr/bin/env bash
# Stops only the processes scripts/start-local.sh recorded as its own. Safe to
# run when nothing is running.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./ports.env
source "$SCRIPT_DIR/ports.env"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PIDS_FILE="$REPO_ROOT/.azurite/run/local.pids"

echo "Stopping local dev environment..."

if [ ! -f "$PIDS_FILE" ]; then
  echo "No local.pids file found, nothing to stop"
  exit 0
fi

listener_pids() {
  lsof -nP -iTCP:"$1" -sTCP:LISTEN -t 2>/dev/null || true
}

while IFS=: read -r name pid port; do
  [ -z "${pid:-}" ] && continue
  if kill -0 "$pid" 2>/dev/null; then
    kill "$pid" 2>/dev/null || true
    for _ in 1 2 3 4 5; do
      kill -0 "$pid" 2>/dev/null || break
      sleep 1
    done
    kill -0 "$pid" 2>/dev/null && kill -9 "$pid" 2>/dev/null || true
    echo "Stopped $name (pid $pid)"
  else
    echo "$name (pid $pid) was not running"
  fi

  # The SWA CLI and the Functions isolated-worker host each fork a child
  # (node / dotnet) that actually holds the listening socket, and that child
  # outlives the parent PID we just killed. Because $port is a port this
  # script itself started a service on, any listener still bound there now
  # is our own leaked child — killing it is targeted, not a blanket
  # process-name kill.
  if [ -n "${port:-}" ]; then
    while IFS= read -r leaked_pid; do
      [ -z "$leaked_pid" ] && continue
      kill -9 "$leaked_pid" 2>/dev/null || true
      echo "Killed leaked child of $name still listening on port $port (pid $leaked_pid)"
    done <<< "$(listener_pids "$port")"
  fi
done < "$PIDS_FILE"

rm -f "$PIDS_FILE"
echo "Done"
