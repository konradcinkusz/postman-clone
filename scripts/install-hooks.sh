#!/usr/bin/env bash
#
# install-hooks.sh — point git at the committed hooks in scripts/hooks.
#
# One command, run once per clone. The pre-commit hook scans staged changes for secrets
# with gitleaks (or its container image) and refuses the commit if it finds one or if no
# scanner is available — architecture-standards P5 / REPO-BASELINE §2. The CI job in
# .github/workflows/secret-scan.yml runs the same scan over the full history.
#
# Works from Git Bash on Windows too: git runs the hook with its bundled bash.

set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "${REPO_ROOT}"

chmod +x scripts/hooks/* scripts/*.sh 2>/dev/null || true
git config core.hooksPath scripts/hooks
echo "Hooks installed: git now runs scripts/hooks/pre-commit before every commit."

if command -v gitleaks >/dev/null 2>&1; then
  echo "Secret scanner: gitleaks $(gitleaks version 2>/dev/null || echo '(version unknown)')."
elif command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  echo "Secret scanner: gitleaks not on PATH; the hook will use ghcr.io/gitleaks/gitleaks."
else
  echo "WARNING: no secret scanner available. The hook refuses to commit until one is:"
  echo "  • gitleaks  https://github.com/gitleaks/gitleaks#installing  (or: go install github.com/zricethezav/gitleaks/v8@v8.28.0 — the version verified with this hook, OI-05)"
  echo "  • Docker    https://docs.docker.com/get-docker/"
fi
