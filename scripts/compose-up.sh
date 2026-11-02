#!/usr/bin/env bash
#
# compose-up.sh — the whole estate as containers, with the demo seed (compose.yaml, ADR-0030, ADR-0031). Needs a Docker daemon.
#
#   scripts/compose-up.sh            generate the dev secrets once, build and start everything, wait for the seed, print the logins
#   scripts/compose-up.sh down [-v]  stop (-v also deletes the databases)
#   scripts/compose-up.sh seed       run the seed again (idempotent)
#   scripts/compose-up.sh reset      remove the demo collections and environments (names under "[demo] ")
#   scripts/compose-up.sh logs [svc] follow the logs
#   scripts/compose-up.sh ps
#
# Flags (environment variables):
#   AUTHSERVICE_IMAGE=<ref>   an authservice image other than ghcr.io/konradcinkusz/authservice:v0.3.4 (a runner that cannot pull from
#                             ghcr.io builds one from the same tag; the default is the pinned image)
#   SEED_ENABLED=false        start the estate without seeding (the seed container then refuses and exits 2)
#   COMPOSE_UP_ARGS=--no-build   use images that are already built (default: --build)
#   POSTGRES_PASSWORD, WEB_AUTH_PERMITS_PER_MINUTE    see compose.yaml
#
# The signing key and the encryption key are the ones scripts/gen-dev-secrets.sh keeps in the gitignored .local-stack/secrets.env,
# so the compose estate and the no-Docker estate share them. They are passed through this script's environment and never printed.

set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "${REPO_ROOT}"
STACK_DIR="${REPO_ROOT}/.local-stack"
SECRETS_FILE="${STACK_DIR}/secrets.env"
LOG_DIR="${STACK_DIR}/compose-logs"

command -v docker >/dev/null || { echo "Missing tool: docker" >&2; exit 1; }
docker info >/dev/null 2>&1 || { echo "No Docker daemon is reachable (docker info failed)." >&2; exit 1; }

load_secrets() {
  [ -f "${SECRETS_FILE}" ] || "${REPO_ROOT}/scripts/gen-dev-secrets.sh"
  # shellcheck disable=SC1090
  . "${SECRETS_FILE}"
  JWT_PRIVATE_KEY_PEM="$(printf '%s' "${JWT_PRIVATE_KEY_PEM_B64}" | base64 -d)"
  export JWT_PRIVATE_KEY_PEM SECRETS_ENCRYPTION_KEY
  # The folder authservice writes its log to, read by the seed and by the browser journeys (the invitation token).
  mkdir -p "${LOG_DIR}"
}

compose() { docker compose -f "${REPO_ROOT}/compose.yaml" "$@"; }

seed_exit_code() {
  local id
  id="$(compose ps -aq seed)"
  [ -n "${id}" ] || { echo "The seed container does not exist." >&2; return 1; }
  docker wait "${id}"
}

case "${1:-up}" in
  up)
    load_secrets
    # shellcheck disable=SC2086
    compose up -d ${COMPOSE_UP_ARGS:---build}
    echo "Waiting for the seed to finish..."
    code="$(seed_exit_code)"
    compose logs --no-log-prefix seed || true
    if [ "${code}" != "0" ]; then
      echo "The seed exited with ${code}." >&2
      exit 1
    fi
    cat <<MSG

The estate is up. Web: http://localhost:5310   authservice: http://localhost:5300
Demo logins (dev only, docs/AUTH.md):
  owner   demo.owner@postmanclone.test    password: Demo-Passw0rd!
  member  demo.member@postmanclone.test   password: Demo-Passw0rd!
MSG
    ;;
  down) load_secrets; shift; compose down "$@" ;;
  seed|reset)
    load_secrets
    compose run --rm --no-deps seed "$1"
    ;;
  logs) load_secrets; shift; compose logs -f "$@" ;;
  ps) load_secrets; compose ps -a ;;
  *) echo "usage: $0 [up|down|seed|reset|logs|ps]" >&2; exit 2 ;;
esac
