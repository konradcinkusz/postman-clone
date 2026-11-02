#!/usr/bin/env bash
#
# aspire-probe.sh — starts the real AppHost (Aspire, DCP, containers) and reports whether the seeded estate comes up (ADR-0031).
#
# It is an experiment with a yes/no answer: does Aspire's orchestrator (DCP) run on this machine? It runs `dotnet run` on
# src/PostmanClone.AppHost, waits up to PROBE_TIMEOUT seconds for the Web to answer /health, then signs in as the demo owner through the
# Web's own sign-in form endpoint (which proves the seed ran and the token path works). It prints a verdict line:
#   PROBE-RESULT: works | AppHost exited | Web never answered | sign-in failed
# The AppHost's output goes to .local-stack/logs/apphost-probe.log. Needs docker, dotnet and curl. It leaves nothing running.

set -uo pipefail
REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "${REPO_ROOT}"
LOG_DIR="${REPO_ROOT}/.local-stack/logs"
mkdir -p "${LOG_DIR}"
LOG="${LOG_DIR}/apphost-probe.log"
TIMEOUT="${PROBE_TIMEOUT:-900}"

export DOTNET_ENVIRONMENT=Development
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://localhost:15888"
export ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL="http://localhost:16888"
export ASPIRE_ALLOW_UNSECURED_TRANSPORT=true

echo "docker: $(docker version --format '{{.Server.Version}}' 2>&1)"
nohup dotnet run --project src/PostmanClone.AppHost --no-launch-profile > "${LOG}" 2>&1 &
APPHOST_PID=$!

verdict="Web never answered"
deadline=$((SECONDS + TIMEOUT))
while [ "${SECONDS}" -lt "${deadline}" ]; do
  if ! kill -0 "${APPHOST_PID}" 2>/dev/null; then verdict="AppHost exited"; break; fi
  if [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 3 http://localhost:5310/health || true)" = "200" ]; then
    verdict="sign-in failed"
    # Give the one-shot seed time to finish, then sign in as the seeded owner. The Web answers a good sign-in with a 3xx and a cookie.
    for _ in $(seq 1 60); do
      code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 20 -c "${LOG_DIR}/probe.cookies" -X POST \
        -H 'Origin: http://localhost:5310' -H 'Referer: http://localhost:5310/sign-in' \
        --data-urlencode 'email=demo.owner@postmanclone.test' --data-urlencode 'password=Demo-Passw0rd!' http://localhost:5310/sign-in || true)"
      echo "sign-in attempt -> ${code}"
      if [ "${code}" = "302" ] || [ "${code}" = "303" ]; then verdict="works"; break; fi
      sleep 5
    done
    break
  fi
  sleep 5
done

echo "--- AppHost log (last 60 lines) ---"
tail -n 60 "${LOG}" || true
kill "${APPHOST_PID}" 2>/dev/null || true
sleep 3
docker ps -a --format '{{.Names}} {{.Status}}' | head -20 || true
echo "PROBE-RESULT: ${verdict}"
[ "${verdict}" = "works" ]
