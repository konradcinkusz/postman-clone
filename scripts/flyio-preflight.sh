#!/usr/bin/env bash
#
# flyio-preflight.sh — checks that one Fly app exists and has the secrets it needs, before a deploy (ADR-0029).
#
# Usage (in .github/workflows/flyio.yml, with FLY_API_TOKEN in the environment):
#   scripts/flyio-preflight.sh <app> [secret-name ...]
#
# It reads the NAMES of the secrets (flyctl secrets list --json), never their values, and it stops the job with a message
# that names the app and the missing item. Nothing is changed on Fly.

set -euo pipefail

fail() {
  echo "::error title=Fly preflight::$*"
  echo "preflight failed: $*" >&2
  exit 1
}

if [ "$#" -lt 1 ]; then
  echo "usage: scripts/flyio-preflight.sh <app> [secret-name ...]" >&2
  exit 2
fi
app="$1"
shift

if [ -z "${FLY_API_TOKEN:-}" ]; then
  fail "FLY_API_TOKEN is missing. The owner adds it to the GitHub environment 'fly' (docs/FLY_DEPLOYMENT.md §2). Nothing was deployed."
fi

if ! flyctl status --app "$app" >/dev/null 2>&1; then
  fail "The Fly app ${app} does not exist or cannot be read. Create it first (docs/FLY_DEPLOYMENT.md §3). Nothing was deployed."
fi

if [ "$#" -gt 0 ]; then
  secrets_json="$(mktemp)"
  trap 'rm -f "$secrets_json"' EXIT
  flyctl secrets list --app "$app" --json > "$secrets_json" \
    || fail "Could not list the secrets of ${app}. Nothing was deployed."
  for name in "$@"; do
    if ! jq -e --arg n "$name" 'any(.[]; ((.Name // .name) == $n))' "$secrets_json" >/dev/null; then
      fail "${app} has no Fly secret named ${name}. Set it (flyio/SECRETS.md). Nothing was deployed."
    fi
  done
fi

echo "${app}: the app exists and the required secrets are set (names checked, values not read)."
