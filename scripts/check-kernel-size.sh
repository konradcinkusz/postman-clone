#!/usr/bin/env bash
#
# check-kernel-size.sh — the shared kernel stays within 800 lines (architecture-standards P2).
#
# Counts the non-blank, non-comment lines of every .cs file under the kernel folder and fails above the limit.
# Line comments (//, ///) and block comments (/* ... */) do not count; neither do blank lines. Comments are
# documentation, not kernel size, so they never push the kernel over the limit.
#
# Usage: scripts/check-kernel-size.sh [directory]    (default: src/PostmanClone.ServiceDefaults)
# Exit status 1 when the count exceeds 800. The tests call it with a fixture directory.

set -euo pipefail

LIMIT=800

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "${REPO_ROOT}"

directory="${1:-src/PostmanClone.ServiceDefaults}"
if [ ! -d "${directory}" ]; then
  echo "check-kernel-size: ${directory} is not a directory."
  exit 1
fi

count=0
while IFS= read -r file; do
  n=$(awk '
    /\/\*/ { in_block = 1 }
    {
      line = $0
      if (in_block) {
        if (index(line, "*/") > 0) { in_block = 0; line = substr(line, index(line, "*/") + 2) } else { next }
      }
      sub(/^[ \t]+/, "", line)
      if (line == "" || line ~ /^\/\//) next
      if (line ~ /^\/\*/) next
      print
    }
  ' "${file}" | wc -l)
  count=$((count + n))
done < <(find "${directory}" -name '*.cs' -type f -not -path '*/obj/*' -not -path '*/bin/*' | sort)

if [ "${count}" -gt "${LIMIT}" ]; then
  echo "Kernel ${directory} has ${count} non-blank, non-comment lines; the limit is ${LIMIT} (P2)."
  exit 1
fi
echo "Kernel ${directory}: ${count} non-blank, non-comment lines (limit ${LIMIT})."
