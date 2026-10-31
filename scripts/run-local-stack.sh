#!/usr/bin/env bash
#
# run-local-stack.sh — runs the whole estate on this machine, without Docker, against a local PostgreSQL.
#
#   scripts/run-local-stack.sh up       start, in order: authservice (AUTH_PORT, default 5300), CollectionsService
#                                       (5301), EnvironmentsService (5302), ExecutionService (5303), the Web (5310)
#   scripts/run-local-stack.sh down     stop the processes this script started (databases and .local-stack/ are kept)
#   scripts/run-local-stack.sh status   whether each one runs and whether its health answers
#
# Flags (environment variables, read by `up`):
#   E2E_SENDER_LOOPBACK=1   the sender may reach loopback addresses (Execution:Ssrf:AllowLoopback=true). This is the
#                           test-only flag the E2E suite needs to send to its local target. The default refuses loopback
#                           (ADR-0011), and the refusal is what the E2E suite checks when this flag is absent.
#   AUTHSERVICE_ENVIRONMENT=Development   authservice logs invitation tokens locally (WP24, docs/AUTH.md).
#
# The AppHost (src/PostmanClone.AppHost) runs the same authservice image through Docker. This script is the route
# when there is no Docker daemon. It runs the same pinned tag, built from source into .local-stack/ (gitignored),
# so no image is pulled. Logs and build logs go to .local-stack/logs/. Each service writes its health answer to the
# log when it fails to start; the script waits for /health (or /health/ready for authservice) before it returns.
#
# PostgreSQL comes from PG_CONNECTION (ADO.NET form). The default targets a local server with the development
# password. Each service database (authdb, collectionsdb, environmentsdb, executiondb) is created if it is missing.
# Secrets come from .local-stack/secrets.env (scripts/gen-dev-secrets.sh) and are never printed.
#
# Requires: git, dotnet (10.0), openssl, psql, curl, base64.

set -euo pipefail

# The authservice version. Same tag as AuthServiceResource.ImageTag in src/PostmanClone.AppHost.
AUTHSERVICE_TAG="v0.3.4"
AUTHSERVICE_REPO="https://github.com/konradcinkusz/authservice"

AUTH_PORT="${AUTH_PORT:-5300}"
COLLECTIONS_PORT="${COLLECTIONS_PORT:-5301}"
ENVIRONMENTS_PORT="${ENVIRONMENTS_PORT:-5302}"
EXEC_PORT="${EXEC_PORT:-5303}"
WEB_PORT="${WEB_PORT:-5310}"
# gitleaks:allow — the default is a local development server, not a credential.
PG_CONNECTION="${PG_CONNECTION:-Host=localhost;Username=postgres;Password=postgres}"
SERVICE_DATABASES=(authdb collectionsdb environmentsdb executiondb)

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "${REPO_ROOT}"
STACK_DIR="${REPO_ROOT}/.local-stack"
LOG_DIR="${STACK_DIR}/logs"
SECRETS_FILE="${STACK_DIR}/secrets.env"

AUTH_SRC_DIR="${STACK_DIR}/authservice-src"
AUTH_BIN_DIR="${STACK_DIR}/authservice-bin"
COLLECTIONS_BIN_DIR="${STACK_DIR}/collections-bin"
ENVIRONMENTS_BIN_DIR="${STACK_DIR}/environments-bin"
EXEC_BIN_DIR="${STACK_DIR}/execution-bin"
WEB_BIN_DIR="${STACK_DIR}/web-bin"

SRC_COLLECTIONS="${REPO_ROOT}/src/PostmanClone.CollectionsService/PostmanClone.CollectionsService.csproj"
SRC_ENVIRONMENTS="${REPO_ROOT}/src/PostmanClone.EnvironmentsService/PostmanClone.EnvironmentsService.csproj"
SRC_EXEC="${REPO_ROOT}/src/PostmanClone.ExecutionService/PostmanClone.ExecutionService.csproj"
SRC_WEB="${REPO_ROOT}/src/PostmanClone.Web/PostmanClone.Web.csproj"

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

# Secrets are sourced into the calling subshell only and passed to a service by environment, never echoed.
load_secrets() {
  # shellcheck disable=SC1090
  set -a; . "${SECRETS_FILE}"; set +a
}

ensure_source() {
  if [ ! -d "${AUTH_SRC_DIR}/.git" ]; then
    git clone --depth 1 --branch "${AUTHSERVICE_TAG}" "${AUTHSERVICE_REPO}" "${AUTH_SRC_DIR}"
  fi
  # The clone sits inside this repository, so MSBuild would import this repository's Directory.Build.props
  # (warnings as errors, audit settings) and Directory.Packages.props (central package versions) into
  # authservice. MSBuild imports only the nearest file, so empty files here shadow ours for the clone.
  printf '<Project />\n' > "${AUTH_SRC_DIR}/Directory.Build.props"
  printf '<Project />\n' > "${AUTH_SRC_DIR}/Directory.Packages.props"
}

# Builds or publishes one project into its bin directory. A failure prints the end of its build log and stops the script.
build_into() {
  local name="$1" log="${LOG_DIR}/$1-build.log"
  shift
  if ! "$@" > "${log}" 2>&1; then
    echo "${name} build failed. Last 80 lines of ${log}:" >&2
    tail -n 80 "${log}" >&2
    exit 1
  fi
}

# Pid files and health. A service is "running" when its pid file names a live process.
pid_file() { echo "${STACK_DIR}/$1.pid"; }

is_running() {
  local pf
  pf="$(pid_file "$1")"
  [ -f "${pf}" ] && kill -0 "$(cat "${pf}")" 2>/dev/null
}

health_code() {
  curl -s -o /dev/null -w '%{http_code}' --max-time 3 "$1" || true
}

# Starts one service detached. The caller exports its environment in a subshell first.
#   start_detached <name> <log file> <command...>
start_detached() {
  local name="$1" log="$2"
  shift 2
  nohup "$@" > "${log}" 2>&1 &
  echo $! > "$(pid_file "${name}")"
}

# Waits until <url> answers 200. Exits when the process dies first or after 180 s. <name> is the pid-file name.
#   wait_healthy <name> <label> <url> <log file>
wait_healthy() {
  local name="$1" label="$2" url="$3" log="$4" attempt
  for attempt in $(seq 1 90); do
    if [ "$(health_code "${url}")" = "200" ]; then
      echo "${label} is ready at ${url} (pid $(cat "$(pid_file "${name}")"))."
      return 0
    fi
    if ! is_running "${name}"; then
      echo "${label} exited during start; see ${log}" >&2
      tail -n 40 "${log}" >&2 || true
      exit 1
    fi
    sleep 2
  done
  echo "${label} did not become ready within 180 s; see ${log}" >&2
  exit 1
}

# Common Auth__ settings for the .NET services. Their discovery document is served over plain HTTP here.
auth_env() {
  export Auth__MetadataAddress="http://localhost:${AUTH_PORT}/.well-known/openid-configuration"
  export Auth__Issuer=PostmanClone
  export Auth__Audience=PostmanClone
  export Auth__RequireHttpsMetadata=false
}

up() {
  require git dotnet openssl psql curl base64
  mkdir -p "${LOG_DIR}"
  chmod 700 "${STACK_DIR}"

  up_authservice
  up_collections
  up_environments
  up_execution
  up_web
}

up_authservice() {
  if is_running authservice; then
    echo "authservice already running (pid $(cat "$(pid_file authservice)")) on port ${AUTH_PORT}."
    return 0
  fi

  ensure_secrets
  ensure_source
  ensure_databases
  build_into authservice dotnet build "${AUTH_SRC_DIR}/src/AuthService/AuthService.csproj" -c Release -o "${AUTH_BIN_DIR}"

  (
    load_secrets
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
    start_detached authservice "${LOG_DIR}/authservice.log" dotnet "${AUTH_BIN_DIR}/AuthService.dll"
  )
  # Schema migration runs after Kestrel starts (MigrateAsync), so /health/ready answers 503 until it is done.
  wait_healthy authservice "authservice ${AUTHSERVICE_TAG}" "http://localhost:${AUTH_PORT}/health/ready" "${LOG_DIR}/authservice.log"
}

# The .NET services of this repository run from their own publish output, with the same database and token settings.
# CollectionsService checks tokens against authservice's discovery document on the same loopback origin.
up_collections() {
  if is_running collections; then
    echo "CollectionsService already running (pid $(cat "$(pid_file collections)")) on port ${COLLECTIONS_PORT}."
    return 0
  fi
  build_into collections dotnet publish "${SRC_COLLECTIONS}" -c Release -o "${COLLECTIONS_BIN_DIR}"

  (
    auth_env
    export ASPNETCORE_URLS="http://localhost:${COLLECTIONS_PORT}"
    export DATABASE_PROVIDER=PostgreSQL
    export ConnectionStrings__collectionsdb="Host=${PG_HOST};Port=${PG_PORT};Username=${PG_USER};Password=${PG_PASSWORD};Database=collectionsdb"
    start_detached collections "${LOG_DIR}/collections.log" dotnet "${COLLECTIONS_BIN_DIR}/PostmanClone.CollectionsService.dll"
  )
  # /health is 503 until the migration has run (MigrateAsync after Kestrel starts), then 200.
  wait_healthy collections "CollectionsService" "http://localhost:${COLLECTIONS_PORT}/health" "${LOG_DIR}/collections.log"
}

# EnvironmentsService seals secret values with Secrets:EncryptionKey (ADR-0006). Without the key it still starts, and
# secret writes answer 503 (the health endpoint reports `secrets`).
up_environments() {
  if is_running environments; then
    echo "EnvironmentsService already running (pid $(cat "$(pid_file environments)")) on port ${ENVIRONMENTS_PORT}."
    return 0
  fi
  build_into environments dotnet publish "${SRC_ENVIRONMENTS}" -c Release -o "${ENVIRONMENTS_BIN_DIR}"

  (
    load_secrets
    auth_env
    export ASPNETCORE_URLS="http://localhost:${ENVIRONMENTS_PORT}"
    export DATABASE_PROVIDER=PostgreSQL
    export ConnectionStrings__environmentsdb="Host=${PG_HOST};Port=${PG_PORT};Username=${PG_USER};Password=${PG_PASSWORD};Database=environmentsdb"
    export Secrets__EncryptionKey="${SECRETS_ENCRYPTION_KEY}"
    start_detached environments "${LOG_DIR}/environments.log" dotnet "${ENVIRONMENTS_BIN_DIR}/PostmanClone.EnvironmentsService.dll"
  )
  wait_healthy environments "EnvironmentsService" "http://localhost:${ENVIRONMENTS_PORT}/health" "${LOG_DIR}/environments.log"
}

# The sender. Loopback is refused unless E2E_SENDER_LOOPBACK=1 (the test-only flag). A relaxed SSRF setting is refused by the
# service in Production (ExecutionOptionsValidator), so a loopback-enabled stack runs as Development. This is local only.
up_execution() {
  if is_running execution; then
    echo "ExecutionService already running (pid $(cat "$(pid_file execution)")) on port ${EXEC_PORT}."
    return 0
  fi
  build_into execution dotnet build "${SRC_EXEC}" -c Release -o "${EXEC_BIN_DIR}"

  (
    auth_env
    export ASPNETCORE_URLS="http://localhost:${EXEC_PORT}"
    export DATABASE_PROVIDER=PostgreSQL
    export ConnectionStrings__executiondb="Host=${PG_HOST};Port=${PG_PORT};Username=${PG_USER};Password=${PG_PASSWORD};Database=executiondb"
    export Environments__BaseAddress="http://localhost:${ENVIRONMENTS_PORT}"
    if [ "${E2E_SENDER_LOOPBACK:-0}" = "1" ]; then
      export ASPNETCORE_ENVIRONMENT=Development
      export Execution__Ssrf__AllowLoopback=true
      echo "ExecutionService: loopback is ALLOWED for the sender (E2E_SENDER_LOOPBACK=1, local test use only)."
    else
      export ASPNETCORE_ENVIRONMENT=Production
    fi
    start_detached execution "${LOG_DIR}/execution.log" dotnet "${EXEC_BIN_DIR}/PostmanClone.ExecutionService.dll"
  )
  wait_healthy execution "ExecutionService" "http://localhost:${EXEC_PORT}/health" "${LOG_DIR}/execution.log"
}

# The Web reaches its backends by service-discovery names (docs/OPERATIONS.md §4.3). It is one instance (ADR-0008).
up_web() {
  if is_running web; then
    echo "Web already running (pid $(cat "$(pid_file web)")) on port ${WEB_PORT}."
    return 0
  fi
  build_into web dotnet publish "${SRC_WEB}" -c Release -o "${WEB_BIN_DIR}"

  (
    auth_env
    export ASPNETCORE_URLS="http://localhost:${WEB_PORT}"
    export ASPNETCORE_ENVIRONMENT=Development
    export Auth__MetadataAddress="http://localhost:${AUTH_PORT}/.well-known/openid-configuration"
    # The sign-in forms' window per client address (OI-02). 10 is the product default. The browser journeys sign in from one
    # address many times, so their run sets WEB_AUTH_PERMITS_PER_MINUTE=18 (just under the suite's own authservice budget).
    export RateLimiting__AuthPermitsPerMinute="${WEB_AUTH_PERMITS_PER_MINUTE:-10}"
    # Service-discovery names contain hyphens, which bash cannot export, so they go through env(1).
    # The content root is the working directory. Started from anywhere else, the Web finds no wwwroot and every static
    # asset (CSS, the Blazor bundle) answers 200 with an empty body, so the page never starts its circuit.
    cd "${WEB_BIN_DIR}"
    start_detached web "${LOG_DIR}/web.log" env \
      "services__authservice__http__0=http://localhost:${AUTH_PORT}" \
      "services__collections-service__http__0=http://localhost:${COLLECTIONS_PORT}" \
      "services__environments-service__http__0=http://localhost:${ENVIRONMENTS_PORT}" \
      "services__execution-service__http__0=http://localhost:${EXEC_PORT}" \
      dotnet "${WEB_BIN_DIR}/PostmanClone.Web.dll"
  )
  wait_healthy web "Web" "http://localhost:${WEB_PORT}/health" "${LOG_DIR}/web.log"
}

stop_one() {
  local name="$1" label="$2"
  if is_running "${name}"; then
    kill "$(cat "$(pid_file "${name}")")"
    echo "Stopped ${label}."
  else
    echo "${label} is not running."
  fi
  rm -f "$(pid_file "${name}")"
}

down() {
  stop_one web "Web"
  stop_one execution "ExecutionService"
  stop_one environments "EnvironmentsService"
  stop_one collections "CollectionsService"
  stop_one authservice "authservice"
}

status() {
  if is_running authservice; then
    echo "authservice running (pid $(cat "$(pid_file authservice)")); /health/ready -> $(health_code "http://localhost:${AUTH_PORT}/health/ready")"
  else
    echo "authservice not running."
  fi
  if is_running collections; then
    echo "CollectionsService running (pid $(cat "$(pid_file collections)")); /health -> $(health_code "http://localhost:${COLLECTIONS_PORT}/health")"
  else
    echo "CollectionsService not running."
  fi
  if is_running environments; then
    echo "EnvironmentsService running (pid $(cat "$(pid_file environments)")); /health -> $(health_code "http://localhost:${ENVIRONMENTS_PORT}/health")"
  else
    echo "EnvironmentsService not running."
  fi
  if is_running execution; then
    echo "ExecutionService running (pid $(cat "$(pid_file execution)")); /health -> $(health_code "http://localhost:${EXEC_PORT}/health")"
  else
    echo "ExecutionService not running."
  fi
  if is_running web; then
    echo "Web running (pid $(cat "$(pid_file web)")); /health -> $(health_code "http://localhost:${WEB_PORT}/health")"
  else
    echo "Web not running."
  fi
}

case "${1:-}" in
  up) up ;;
  down) down ;;
  status) status ;;
  *) echo "usage: $0 up|down|status" >&2; exit 2 ;;
esac
