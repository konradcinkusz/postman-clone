#!/usr/bin/env bash
#
# check-doc-links.sh — every relative link in the tracked Markdown files resolves to a file or directory.
#
# Checks the inline links [text](target) of every tracked *.md file (git ls-files), so a link
# into an untracked or ignored path is reported too. External links (http, https, mailto) are
# not fetched: a repository check cannot promise that a remote page is still there. Same-page
# anchors (#x) are skipped. A fragment or query on a relative link is removed before the path
# check; the fragment itself is not checked.
#
# Run from anywhere inside the repository. Exit status 1 when a link is broken.

set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "${REPO_ROOT}"

broken=0
checked=0

while IFS= read -r file; do
  dir="$(dirname "${file}")"

  # Every inline link target, one "](target)" per line. grep exits 1 for a file without links.
  while IFS= read -r link; do
    target="${link#](}"
    target="${target%)}"
    target="${target%% *}"   # drop an optional "title" after the target

    case "${target}" in
      ""|\#*|http://*|https://*|mailto:*) continue ;;
    esac

    path="${target%%#*}"
    path="${path%%\?*}"

    if [[ "${path}" == /* ]]; then
      resolved=".${path}"
    else
      resolved="${dir}/${path}"
    fi

    checked=$((checked + 1))
    if [ ! -e "${resolved}" ]; then
      echo "broken link in ${file}: ${target}"
      broken=$((broken + 1))
    fi
  done < <(grep -oE '\]\([^)]*\)' "${file}" || true)
done < <(git ls-files '*.md')

if [ "${broken}" -gt 0 ]; then
  echo "${broken} broken relative link(s) out of ${checked} checked."
  exit 1
fi
echo "All ${checked} relative Markdown links resolve."
