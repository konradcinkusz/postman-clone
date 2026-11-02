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
SUMMARY="${LOG_DIR}/probe-summary.txt"
: > "${SUMMARY}"
say() { echo "$*" | tee -a "${SUMMARY}"; }
TIMEOUT="${PROBE_TIMEOUT:-900}"

export DOTNET_ENVIRONMENT=Development
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://localhost:15888"
export ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL="http://localhost:16888"
export ASPIRE_ALLOW_UNSECURED_TRANSPORT=true

say "docker: $(docker version --format '{{.Server.Version}}' 2>&1)"
nohup dotnet run --project src/PostmanClone.AppHost --no-launch-profile > "${LOG}" 2>&1 &
APPHOST_PID=$!

verdict="Web never answered"
deadline=$((SECONDS + TIMEOUT))
while [ "${SECONDS}" -lt "${deadline}" ]; do
  if ! kill -0 "${APPHOST_PID}" 2>/dev/null; then verdict="AppHost exited"; break; fi
  if [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 3 http://localhost:5310/health || true)" = "200" ]; then
    verdict="sign-in failed"
    # Give the one-shot seed time to finish, then sign in as the seeded owner through the Web's own form: GET the page for the antiforgery
    # token and cookie, POST the form with both. A good sign-in is answered with a redirect and a session cookie.
    jar="${LOG_DIR}/probe.cookies"
    for _ in $(seq 1 12); do
      rm -f "${jar}"
      token="$(curl -s --max-time 20 -c "${jar}" http://localhost:5310/sign-in | grep -o 'name="__RequestVerificationToken"[^>]*value="[^"]*"' | head -1 | sed 's/.*value="//; s/"$//')"
      if [ -n "${token}" ]; then
        code="$(curl -s -o "${LOG_DIR}/probe-post.headers" -D "${LOG_DIR}/probe-post.headers" -w '%{http_code}' --max-time 30 -b "${jar}" -c "${jar}" -X POST \
          -H 'Origin: http://localhost:5310' -H 'Referer: http://localhost:5310/sign-in' \
          --data-urlencode "__RequestVerificationToken=${token}" \
          --data-urlencode 'email=demo.owner@postmanclone.test' --data-urlencode 'password=Demo-Passw0rd!' http://localhost:5310/sign-in || true)"
      else
        code="no-antiforgery-token"
      fi
      say "sign-in attempt -> ${code} $(tr -d '\r' < "${LOG_DIR}/probe-post.headers" 2>/dev/null | grep -i '^location:' | head -1)"
      if [ "${code}" = "302" ] || [ "${code}" = "303" ]; then
        # The redirect target tells a sign-in from a refusal: a refusal goes back to /sign-in?error=...
        loc="$(curl -s -o /dev/null -D - --max-time 5 -b "${jar}" http://localhost:5310/ | tr -d '\r' | grep -i '^location:' || true)"
        say "after sign-in, GET / -> ${loc:-200 (no redirect: signed in)}"
        if ! echo "${loc}" | grep -qi 'sign-in'; then verdict="works"; break; fi
      fi
      sleep 5
    done
    break
  fi
  sleep 5
done

{
echo "--- authservice, asked directly (the seed's result) ---"
auth_container="$(docker ps --filter name=authservice --format '{{.Names}}' | head -1)"
if [ -n "${auth_container}" ]; then
  port="$(docker port "${auth_container}" 8080 | head -1 | sed 's/.*://')"
  echo "authservice container ${auth_container} on host port ${port}"
  curl -s -o /dev/null -w 'GET /health/ready -> %{http_code}\n' "http://localhost:${port}/health/ready" || true
  curl -s -o /dev/null -w 'POST /api/v1/auth/login (demo owner) -> %{http_code}\n' -X POST -H 'Content-Type: application/json' \
    --data '{"email":"demo.owner@postmanclone.test","password":"Demo-Passw0rd!"}' "http://localhost:${port}/api/v1/auth/login" || true
  echo "invitation lines in authservice's console: $(docker logs "${auth_container}" 2>&1 | grep -c 'Invitation email to' || true)"
fi
echo "--- containers started by DCP ---"
docker ps -a --format '{{.Names}}\t{{.Image}}\t{{.Status}}' | head -20 || true
echo "--- AppHost log: errors and seed lines ---"
grep -i -E "error|fail|exception|seed|authservice" "${LOG}" | cut -c1-220 | head -40 || true
echo "--- AppHost log (last 25 lines) ---"
tail -n 25 "${LOG}" || true
} >> "${SUMMARY}" 2>&1
kill "${APPHOST_PID}" 2>/dev/null || true
sleep 3
say "PROBE-RESULT: ${verdict}"
echo "=== PROBE SUMMARY ==="
cat "${SUMMARY}"
[ "${verdict}" = "works" ]
