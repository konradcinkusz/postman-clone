#!/usr/bin/env bash
#
# smoke-auth.sh — end-to-end check of the authservice contract that PostmanClone relies on (ADR-0004).
#
# Run it against a running authservice (scripts/run-local-stack.sh up, or the AppHost). It registers a
# fresh user, logs in, creates an organization, refreshes, and checks the claims the services read:
#   sub, organization, organization:{id}:role   (role Owner for the creator)
# and that a token issued before the organization existed carries no organization claim.
# It prints status codes, claim NAMES, and the organization id. It never prints a token, a password
# or a refresh token. The user and the organization it creates are left in the database.
#
# Usage: scripts/smoke-auth.sh [base-url]     default: http://localhost:5300 (AUTH_PORT)

set -euo pipefail

BASE="${1:-http://localhost:${AUTH_PORT:-5300}}"
BASE="${BASE%/}"
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "${WORK_DIR}"' EXIT

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

# call METHOD PATH EXPECTED_STATUS [JSON_BODY] [BEARER]
# Writes the response body to WORK_DIR/body; returns it on stdout.
call() {
  local method="$1" path="$2" expected="$3" body="${4:-}" bearer="${5:-}"
  local -a args=(-sS -o "${WORK_DIR}/body" -w '%{http_code}' -X "${method}" "${BASE}${path}")
  if [ -n "${body}" ]; then
    args+=(-H 'Content-Type: application/json' --data-binary "${body}")
  fi
  if [ -n "${bearer}" ]; then
    args+=(-H "Authorization: Bearer ${bearer}")
  fi
  local status
  status="$(curl "${args[@]}")"
  echo "${method} ${path} -> ${status}"
  if [ "${status}" != "${expected}" ]; then
    fail "${method} ${path} answered ${status}, expected ${expected}"
  fi
}

# json EXPRESSION: prints a value from the last response body. The expression is over `d`, e.g. "d['accessToken']".
json() { python3 -I -c 'import json,sys; d=json.load(open(sys.argv[1])); print(eval(sys.argv[2]))' "${WORK_DIR}/body" "$1"; }

# Prints the claim names of a JWT's payload, sorted. Values are not printed.
claims_of() {
  python3 -I -c '
import base64, json, sys
payload = sys.argv[1].split(".")[1]
payload += "=" * (-len(payload) % 4)
claims = json.loads(base64.urlsafe_b64decode(payload))
for name in sorted(claims):
    print(name)
' "$1"
}

echo "authservice smoke test against ${BASE}"

call GET /health/ready 200
call GET /.well-known/jwks.json 200
keys="$(json "len(d['keys'])")"
[ "${keys}" -ge 1 ] || fail "JWKS has no keys"
echo "JWKS keys: ${keys}"

call GET /api/v1/auth/consents/versions 200
terms="$(json "d['terms']")"
privacy="$(json "d['privacy']")"

email="smoke-$(openssl rand -hex 6)@example.test"
password="$(openssl rand -hex 16)"
register_body="$(python3 -I -c 'import json,sys; print(json.dumps({"email":sys.argv[1],"password":sys.argv[2],"acceptedTermsVersion":sys.argv[3],"acceptedPrivacyVersion":sys.argv[4]}))' "${email}" "${password}" "${terms}" "${privacy}")"
call POST /api/v1/auth/register 200 "${register_body}"
register_access="$(json "d['accessToken']")"
echo "Claims on the token from register (before any organization):"
claims_of "${register_access}" | sed 's/^/  /'
if claims_of "${register_access}" | grep -qx 'organization'; then
  fail "a token issued before any organization carries an organization claim"
fi

login_body="$(python3 -I -c 'import json,sys; print(json.dumps({"email":sys.argv[1],"password":sys.argv[2]}))' "${email}" "${password}")"
call POST /api/v1/auth/login 200 "${login_body}"
login_refresh="$(json "d['refreshToken']")"
login_access="$(json "d['accessToken']")"
echo "Token type and lifetime: $(json "d['tokenType']"), expiresIn $(json "d['expiresIn']") s"

call GET /api/v1/auth/me 200 "" "${login_access}"
user_id="$(json "d['id']")"

org_body='{"name":"smoke-org"}'
call POST /api/v1/organizations 201 "${org_body}" "${login_access}"
org_id="$(json "d['id']")"
org_role="$(json "d['role']")"
[ "${org_role}" = "Owner" ] || fail "creator's role in the new organization is ${org_role}, expected Owner"
echo "Organization created: ${org_id} (creator role: ${org_role})"

refresh_body="$(python3 -I -c 'import json,sys; print(json.dumps({"refreshToken":sys.argv[1]}))' "${login_refresh}")"
call POST /api/v1/auth/refresh 200 "${refresh_body}"
refreshed_access="$(json "d['accessToken']")"
refreshed_refresh="$(json "d['refreshToken']")"

echo "Claims on the refreshed token:"
claims_of "${refreshed_access}" | sed 's/^/  /'

claims="$(claims_of "${refreshed_access}")"
printf '%s\n' "${claims}" | grep -qx 'sub' || fail "no sub claim"
printf '%s\n' "${claims}" | grep -qx 'organization' || fail "refreshed token has no organization claim"
printf '%s\n' "${claims}" | grep -qx "organization:${org_id}:role" || fail "no organization:{id}:role claim for ${org_id}"

call GET /api/v1/auth/me 200 "" "${refreshed_access}"
me_role="$(python3 -I -c 'import json,sys; orgs=json.load(open(sys.argv[1]))["organizations"]; print(next(o["role"] for o in orgs if o["id"]==sys.argv[2]))' "${WORK_DIR}/body" "${org_id}")"
[ "${me_role}" = "Owner" ] || fail "/auth/me lists role ${me_role} for the organization, expected Owner"
echo "/api/v1/auth/me lists the organization with role ${me_role}."

call POST /api/v1/auth/logout 200 "" "${refreshed_access}" >/dev/null
call POST /api/v1/auth/refresh 401 "$(python3 -I -c 'import json,sys; print(json.dumps({"refreshToken":sys.argv[1]}))' "${refreshed_refresh}")"
echo "Refresh after logout is refused (401)."

call GET /api/v1/auth/me 401 "" "not-a-token"

echo "PASS: authservice contract holds for user ${user_id} and organization ${org_id}."
