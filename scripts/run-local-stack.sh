#!/usr/bin/env bash
#
# run-local-stack.sh — runs authservice on this machine, without Docker, against a local PostgreSQL.
#
#   scripts/run-local-stack.sh up       build and start authservice on AUTH_PORT (default 5300), then
#                                       CollectionsService on COLLECTIONS_PORT (default 5301) and
#                                       ExecutionService on EXEC_PORT (default 5303)
#   scripts/run-local-stack.sh down     stop them all (the databases and .local-stack/ are kept)
#   scripts/run-local-stack.sh status   show whether they run and whether their health answers
#
# The AppHost (src/PostmanClone.AppHost) runs the same authservice image through Docker. This script
# is the sessions' and developers' route when there is no Docker daemon. It runs the same pinned tag,
# built from source into .local-stack/ (gitignored), so no image is pulled.
#
# PostgreSQL comes from PG_CONNECTION (ADO.NET form). The default targets a local server with the
# development password. Each service database (authdb, collectionsdb, environmentsdb, executiondb) is
# created if it is missing. Secrets come from .local-stack/secrets.env (scripts/gen-dev-secrets.sh) and
# are never printed. Logs go to .local-stack/authservice.log.
#
# Requires: git, dotnet (10.0), openssl, psql, curl, base64.

set -euo pipefail

# The authservice version. Same tag as AuthServiceResource.ImageTag in src/PostmanClone.AppHost.
AUTHSERVICE_TAG="v0.3.4"
AUTHSERVICE_REPO="https://github.com/konradcinkusz/authservice"

AUTH_PORT="${AUTH_PORT:-5300}"
COLLECTIONS_PORT="${COLLECTIONS_PORT:-5301}"
EXEC_PORT="${EXEC_PORT:-5303}"
WEB_PORT="${WEB_PORT:-5310}"
# gitleaks:allow — the default is a local development server, not a credential.
PG_CONNECTION="${PG_CONNECTION:-Host=localhost;Username=postgres;Password=postgres}"
SERVICE_DATABASES=(authdb collectionsdb environmentsdb executiondb)

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "${REPO_ROOT}"
STACK_DIR="${REPO_ROOT}/.local-stack"
SRC_DIR="${STACK_DIR}/authservice-src"
BIN_DIR="${STACK_DIR}/authservice-bin"
PID_FILE="${STACK_DIR}/authservice.pid"
LOG_FILE="${STACK_DIR}/authservice.log"
COLLECTIONS_SRC_DIR="${REPO_ROOT}/src/PostmanClone.CollectionsService"
COLLECTIONS_BIN_DIR="${STACK_DIR}/collections-bin"
COLLECTIONS_PID_FILE="${STACK_DIR}/collections.pid"
COLLECTIONS_LOG_FILE="${STACK_DIR}/collections.log"
EXEC_SRC_PROJECT="${REPO_ROOT}/src/PostmanClone.ExecutionService/PostmanClone.ExecutionService.csproj"
EXEC_BIN_DIR="${STACK_DIR}/execution-bin"
EXEC_PID_FILE="${STACK_DIR}/execution.pid"
EXEC_LOG_FILE="${STACK_DIR}/execution.log"
SECRETS_FILE="${STACK_DIR}/secrets.env"

# Reads one key from PG_CONNECTION; empty when absent.
pg_value() {
  local wanted="$1" part key
  local IFS=';'
  for part in ${PG_CONNECTION}; do
    key="${part%%=*}"
    if [ "${key,,}" = "${wanted}" ]; then
      printf '%s' "${part#*=}"
      return 0
    fi
  done
}

PG_HOST="$(pg_value host)"
PG_PORT="$(pg_value port)"
PG_PORT="${PG_PORT:-5432}"
PG_USER="$(pg_value username)"
PG_PASSWORD="$(pg_value password)"

psql_admin() {
  PGHOST="${PG_HOST}" PGPORT="${PG_PORT}" PGUSER="${PG_USER}" PGPASSWORD="${PG_PASSWORD}" \
    psql -X -v ON_ERROR_STOP=1 -d postgres "$@"
}

require() {
  for tool in "$@"; do
    command -v "${tool}" >/dev/null || { echo "Missing tool: ${tool}" >&2; exit 1; }
  done
}

ensure_databases() {
  local db
  for db in "${SERVICE_DATABASES[@]}"; do
    if ! psql_admin -tAc "SELECT 1 FROM pg_database WHERE datname = '${db}'" | grep -q 1; then
      psql_admin -c "CREATE DATABASE \"${db}\"" >/dev/null
      echo "Created database ${db}."
    fi
  done
}

ensure_secrets() {
  if [ ! -f "${SECRETS_FILE}" ]; then
    "${REPO_ROOT}/scripts/gen-dev-secrets.sh"
  fi
}

ensure_source() {
  if [ ! -d "${SRC_DIR}/.git" ]; then
    git clone --depth 1 --branch "${AUTHSERVICE_TAG}" "${AUTHSERVICE_REPO}" "${SRC_DIR}"
  fi
  # The clone sits inside this repository, so MSBuild would import this repository's Directory.Build.props
  # (warnings as errors, audit settings) and Directory.Packages.props (central package versions) into
  # authservice. MSBuild imports only the nearest file, so empty files here shadow ours for the clone.
  printf '<Project />\n' > "${SRC_DIR}/Directory.Build.props"
  printf '<Project />\n' > "${SRC_DIR}/Directory.Packages.props"
}

build_authservice() {
  local log="${STACK_DIR}/authservice-build.log"
  if ! dotnet build "${SRC_DIR}/src/AuthService/AuthService.csproj" -c Release -o "${BIN_DIR}" > "${log}" 2>&1; then
    echo "authservice build failed. Last 80 lines of ${log}:" >&2
    tail -n 80 "${log}" >&2
    exit 1
  fi
}

is_running() {
  [ -f "${PID_FILE}" ] && kill -0 "$(cat "${PID_FILE}")" 2>/dev/null
}

health_status() {
  curl -s -o /dev/null -w '%{http_code}' --max-time 3 "http://localhost:${AUTH_PORT}/health/ready" || true
}

up() {
  require git dotnet openssl psql curl base64
  mkdir -p "${STACK_DIR}"
  chmod 700 "${STACK_DIR}"

  up_authservice
  up_collections
  up_execution
}

up_authservice() {
  if is_running; then
    echo "authservice already running (pid $(cat "${PID_FILE}")) on port ${AUTH_PORT}."
    return 0
  fi

  ensure_secrets
  ensure_source
  ensure_databases
  build_authservice

  # Secrets are sourced into this shell only and passed to authservice by environment, never echoed.
  # shellcheck disable=SC1090
  set -a; . "${SECRETS_FILE}"; set +a

  (
    export ASPNETCORE_URLS="http://localhost:${AUTH_PORT}"
    export DATABASE_PROVIDER=PostgreSQL
    export ConnectionStrings__DefaultConnection="Host=${PG_HOST};Port=${PG_PORT};Username=${PG_USER};Password=${PG_PASSWORD};Database=authdb"
    export Database__SchemaMode=Migrate
    export Database__MigrationsAssembly=AuthService.Migrations.PostgreSQL
    export Jwt__Issuer=PostmanClone
    export Jwt__Audience=PostmanClone
    export Jwt__PublicBaseUrl="http://localhost:${AUTH_PORT}"
    export Jwt__PrivateKeyPem="$(printf '%s' "${JWT_PRIVATE_KEY_PEM_B64}" | base64 -d)"
    export FrontendBaseUrl="http://localhost:${WEB_PORT}"
    # Production withholds an invitation token from the log when no email provider is configured. Set
    # AUTHSERVICE_ENVIRONMENT=Development to read the token there locally (docs/AUTH.md, Invitations).
    export ASPNETCORE_ENVIRONMENT="${AUTHSERVICE_ENVIRONMENT:-Production}"
    nohup dotnet "${BIN_DIR}/AuthService.dll" > "${LOG_FILE}" 2>&1 &
    echo $! > "${PID_FILE}"
  )

  # Schema migration runs after Kestrel starts (MigrateAsync), so /health/ready answers 503 until it is done.
  local attempt
  for attempt in $(seq 1 90); do
    if [ "$(health_status)" = "200" ]; then
      echo "authservice ${AUTHSERVICE_TAG} is ready on http://localhost:${AUTH_PORT} (pid $(cat "${PID_FILE}"))."
      return 0
    fi
    if ! is_running; then
      echo "authservice exited during start; see ${LOG_FILE}" >&2
      exit 1
    fi
    sleep 2
  done
  echo "authservice did not become ready within 180 s; see ${LOG_FILE}" >&2
  exit 1
}

# CollectionsService runs from its own build output. It reads collectionsdb (created above) and checks tokens
# against authservice's discovery document on the same loopback origin. The secrets are not needed here.
collections_is_running() {
  [ -f "${COLLECTIONS_PID_FILE}" ] && kill -0 "$(cat "${COLLECTIONS_PID_FILE}")" 2>/dev/null
}

collections_health_status() {
  curl -s -o /dev/null -w '%{http_code}' --max-time 3 "http://localhost:${COLLECTIONS_PORT}/health" || true
}

up_collections() {
  if collections_is_running; then
    echo "CollectionsService already running (pid $(cat "${COLLECTIONS_PID_FILE}")) on port ${COLLECTIONS_PORT}."
    return 0
  fi

  local log="${STACK_DIR}/collections-build.log"
  if ! dotnet publish "${COLLECTIONS_SRC_DIR}/PostmanClone.CollectionsService.csproj" -c Release -o "${COLLECTIONS_BIN_DIR}" > "${log}" 2>&1; then
    echo "CollectionsService build failed. Last 80 lines of ${log}:" >&2
    tail -n 80 "${log}" >&2
    exit 1
  fi

  (
    export ASPNETCORE_URLS="http://localhost:${COLLECTIONS_PORT}"
    export DATABASE_PROVIDER=PostgreSQL
    export ConnectionStrings__collectionsdb="Host=${PG_HOST};Port=${PG_PORT};Username=${PG_USER};Password=${PG_PASSWORD};Database=collectionsdb"
    export Auth__MetadataAddress="http://localhost:${AUTH_PORT}/.well-known/openid-configuration"
    export Auth__Issuer=PostmanClone
    export Auth__Audience=PostmanClone
    export Auth__RequireHttpsMetadata=false
    nohup dotnet "${COLLECTIONS_BIN_DIR}/PostmanClone.CollectionsService.dll" > "${COLLECTIONS_LOG_FILE}" 2>&1 &
    echo $! > "${COLLECTIONS_PID_FILE}"
  )

  # /health is 503 until the migration has run (MigrateAsync after Kestrel starts), then 200.
  local attempt
  for attempt in $(seq 1 90); do
    if [ "$(collections_health_status)" = "200" ]; then
      echo "CollectionsService is ready on http://localhost:${COLLECTIONS_PORT} (pid $(cat "${COLLECTIONS_PID_FILE}"))."
      return 0
    fi
    if ! collections_is_running; then
      echo "CollectionsService exited during start; see ${COLLECTIONS_LOG_FILE}" >&2
      exit 1
    fi
    sleep 2
  done
  echo "CollectionsService did not become healthy within 180 s; see ${COLLECTIONS_LOG_FILE}" >&2
  exit 1
}

execution_is_running() {
  [ -f "${EXEC_PID_FILE}" ] && kill -0 "$(cat "${EXEC_PID_FILE}")" 2>/dev/null
}

execution_health_status() {
  curl -s -o /dev/null -w '%{http_code}' --max-time 3 "http://localhost:${EXEC_PORT}/health" || true
}

up_execution() {
  if execution_is_running; then
    echo "ExecutionService already running (pid $(cat "${EXEC_PID_FILE}")) on port ${EXEC_PORT}."
    return 0
  fi

  local log="${STACK_DIR}/execution-build.log"
  if ! dotnet build "${EXEC_SRC_PROJECT}" -c Release -o "${EXEC_BIN_DIR}" > "${log}" 2>&1; then
    echo "ExecutionService build failed. Last 80 lines of ${log}:" >&2
    tail -n 80 "${log}" >&2
    exit 1
  fi

  (
    export ASPNETCORE_URLS="http://localhost:${EXEC_PORT}"
    export ASPNETCORE_ENVIRONMENT=Production
    export DATABASE_PROVIDER=PostgreSQL
    export ConnectionStrings__executiondb="Host=${PG_HOST};Port=${PG_PORT};Username=${PG_USER};Password=${PG_PASSWORD};Database=executiondb"
    export Auth__MetadataAddress="http://localhost:${AUTH_PORT}/.well-known/openid-configuration"
    export Auth__Issuer=PostmanClone
    export Auth__Audience=PostmanClone
    # authservice listens on plain HTTP here, so its discovery document is fetched over HTTP (development stack only).
    export Auth__RequireHttpsMetadata=false
    nohup dotnet "${EXEC_BIN_DIR}/PostmanClone.ExecutionService.dll" > "${EXEC_LOG_FILE}" 2>&1 &
    echo $! > "${EXEC_PID_FILE}"
  )

  # MigrateAsync runs after Kestrel starts, so /health answers 503 until the schema is in place.
  local attempt
  for attempt in $(seq 1 90); do
    if [ "$(execution_health_status)" = "200" ]; then
      echo "ExecutionService is ready on http://localhost:${EXEC_PORT} (pid $(cat "${EXEC_PID_FILE}"))."
      return 0
    fi
    if ! execution_is_running; then
      echo "ExecutionService exited during start; see ${EXEC_LOG_FILE}" >&2
      exit 1
    fi
    sleep 2
  done
  echo "ExecutionService did not become ready within 180 s; see ${EXEC_LOG_FILE}" >&2
  exit 1
}

stop_execution() {
  if execution_is_running; then
    kill "$(cat "${EXEC_PID_FILE}")"
    echo "Stopped ExecutionService."
  else
    echo "ExecutionService is not running."
  fi
  rm -f "${EXEC_PID_FILE}"
}

down() {
  if is_running; then
    kill "$(cat "${PID_FILE}")"
    echo "Stopped authservice."
  else
    echo "authservice is not running."
  fi
  rm -f "${PID_FILE}"

  if collections_is_running; then
    kill "$(cat "${COLLECTIONS_PID_FILE}")"
    echo "Stopped CollectionsService."
  else
    echo "CollectionsService is not running."
  fi
  rm -f "${COLLECTIONS_PID_FILE}"

  stop_execution
}

status() {
  if is_running; then
    echo "authservice running (pid $(cat "${PID_FILE}")); /health/ready -> $(health_status)"
  else
    echo "authservice not running."
  fi

  if collections_is_running; then
    echo "CollectionsService running (pid $(cat "${COLLECTIONS_PID_FILE}")); /health -> $(collections_health_status)"
  else
    echo "CollectionsService not running."
  fi

  if execution_is_running; then
    echo "ExecutionService running (pid $(cat "${EXEC_PID_FILE}")); /health -> $(execution_health_status)"
  else
    echo "ExecutionService not running."
  fi
}

case "${1:-}" in
  up) up ;;
  down) down ;;
  status) status ;;
  *) echo "usage: $0 up|down|status" >&2; exit 2 ;;
esac
