#!/usr/bin/env bash
#
# check-flyio-trigger.sh — the Fly workflow runs by hand and by nothing else (ADR-0026 Decision 10, ADR-0029, P12 DEVIATION).
#
# Fails when .github/workflows/flyio.yml has any trigger other than workflow_dispatch, or any branch, tag, path, schedule
# or cron filter inside the `on:` block, or when workflow_call appears anywhere in it. Also proves the check itself: a
# fixture with a push trigger, one with a tag filter and one with a schedule must each fail.
#
# Run by the pr-validation job `flyio-config`, and locally:  scripts/check-flyio-trigger.sh [workflow-file]

set -euo pipefail

cd "$(dirname "$0")/.."
workflow="${1:-.github/workflows/flyio.yml}"

# check_file <file> → 0 when the only trigger is workflow_dispatch, 1 (with reasons on stderr) otherwise.
check_file() {
  local file="$1" problems
  problems="$(awk '
    BEGIN { in_on = 0; saw_dispatch = 0 }
    # The `on:` key must be a block (its triggers below), never an inline list such as `on: [push]`.
    /^on:/ {
      in_on = 1
      if ($0 !~ /^on:[[:space:]]*$/) { print "the on: key is not a block: " $0 }
      next
    }
    # Any other top-level key ends the on: block.
    /^[^[:space:]#]/ { in_on = 0 }
    in_on && /^[[:space:]]*#/ { next }
    in_on && /^  [A-Za-z_]+:/ {
      key = $0; sub(/^  /, "", key); sub(/:.*/, "", key)
      if (key == "workflow_dispatch") { saw_dispatch = 1 } else { print "trigger " key " is not allowed" }
    }
    in_on && /(branches|branches-ignore|tags|tags-ignore|paths|paths-ignore|schedule|cron)[[:space:]]*:/ {
      print "a filter or schedule is not allowed: " $0
    }
    /^[[:space:]]*workflow_call[[:space:]]*:/ { print "workflow_call is not allowed" }
    END { if (!saw_dispatch) print "workflow_dispatch is missing from the on: block" }
  ' "$file")"
  if [ -n "$problems" ]; then
    echo "$problems" | sed "s|^|${file}: |" >&2
    return 1
  fi
  return 0
}

if ! check_file "$workflow"; then
  echo "::error title=Fly workflow has another trigger::${workflow} may only be started by workflow_dispatch (ADR-0029)." >&2
  exit 1
fi
echo "${workflow}: the only trigger is workflow_dispatch."

# The check must catch what it claims to catch. Each fixture below is a broken copy; every one must fail.
fixtures="$(mktemp -d)"
trap 'rm -rf "$fixtures"' EXIT

write_fixture() {
  local name="$1" trigger_block="$2"
  {
    echo "name: fixture"
    echo
    printf '%s\n' "$trigger_block"
    echo
    echo "jobs:"
    echo "  noop:"
    echo "    runs-on: ubuntu-latest"
    echo "    steps:"
    echo "      - run: echo fixture"
  } > "${fixtures}/${name}.yml"
}

write_fixture push-trigger 'on:
  workflow_dispatch:
  push:
    branches: [main]'
write_fixture tag-filter 'on:
  workflow_dispatch:
  push:
    tags:
      - "v*"'
write_fixture schedule 'on:
  workflow_dispatch:
  schedule:
    - cron: "0 3 * * *"'
write_fixture no-dispatch 'on:
  repository_dispatch:'
write_fixture inline-on 'on: [push]'

proved=0
for fixture in "${fixtures}"/*.yml; do
  if check_file "$fixture" 2>/dev/null; then
    echo "::error::the trigger check accepted the broken fixture $(basename "$fixture")" >&2
    exit 1
  fi
  proved=$((proved + 1))
done
echo "The trigger check rejects ${proved} broken fixtures (push, tag filter, schedule, no workflow_dispatch, inline on:)."
