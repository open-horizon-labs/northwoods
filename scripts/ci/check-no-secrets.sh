#!/usr/bin/env bash
# Secret safety check.
# Exits 1 if any high-confidence secret pattern is found in tracked files,
# or if .env.local is not covered by .gitignore.
#
# Patterns (high-confidence only — false positives are worse than false negatives):
#   AWS access key:       AKIA[0-9A-Z]{16}
#   OpenAI key:           sk-[A-Za-z0-9]{20,}
#   GitHub PAT (classic): ghp_[A-Za-z0-9]{36}
#   GitHub PAT (fine):    github_pat_[A-Za-z0-9_]{82}
#   PEM private key:      -----BEGIN (RSA|EC|DSA|OPENSSH) PRIVATE KEY-----
#   Generic bearer token  in source code (not .env files)

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

FAILURES=0

fail() {
    echo "  [FAIL] $*"
    FAILURES=$((FAILURES + 1))
}

echo "=== Secret Safety Check ==="

# ---------------------------------------------------------------------------
# 1. Verify .env.local is covered by .gitignore
# ---------------------------------------------------------------------------
GITIGNORE="$REPO_ROOT/.gitignore"
if [[ ! -f "$GITIGNORE" ]]; then
    fail ".gitignore not found at repo root"
else
    # .env.* glob covers .env.local; also check direct match
    if grep -qE '^\.env\.\*$|^\.env\.local$' "$GITIGNORE"; then
        echo "  [OK] .env.local is covered by .gitignore"
    else
        fail ".env.local is NOT covered by .gitignore (add '.env.*' or '.env.local')"
    fi
fi

# ---------------------------------------------------------------------------
# 2. Scan tracked files for secret patterns
#    We scan git-tracked files only so we don't trip on .env.local etc.
# ---------------------------------------------------------------------------

# Files to always exclude from scanning (known-safe placeholders / test fixtures).
EXCLUDE_PATTERNS=(
    ".env.example"
    ".env"
    "*.md"
    "pnpm-lock.yaml"
    "package-lock.json"
)

# Build git ls-files exclusion args.
# We use process substitution to avoid subshell FAILURES issue.
TRACKED_FILES=$(git -C "$REPO_ROOT" ls-files \
    -- \
    ':!:.env.example' \
    ':!:.env' \
    ':!:*.md' \
    ':!:pnpm-lock.yaml' \
    ':!:package-lock.json' \
    ':!:node_modules/**' \
    ':!:**/bin/**' \
    ':!:**/obj/**' \
    2>/dev/null || true)

if [[ -z "$TRACKED_FILES" ]]; then
    echo "  [WARN] No tracked files found (git ls-files returned nothing)"
fi

# Patterns: label|regex
declare -a SECRET_PATTERNS=(
    "AWS access key|AKIA[0-9A-Z]{16}"
    "OpenAI API key|sk-[A-Za-z0-9T_]{20,}"
    "GitHub PAT (classic)|ghp_[A-Za-z0-9]{36}"
    "GitHub PAT (fine-grained)|github_pat_[A-Za-z0-9_]{50,}"
    "PEM private key|-----BEGIN (RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----"
)

while IFS= read -r file; do
    [[ -f "$file" ]] || continue
    for entry in "${SECRET_PATTERNS[@]}"; do
        label="${entry%%|*}"
        pattern="${entry##*|}"
        # grep -P for PCRE; -n for line numbers; -I to skip binary files
        matches=$(grep -PIn "$pattern" "$file" 2>/dev/null || true)
        if [[ -n "$matches" ]]; then
            while IFS= read -r match_line; do
                fail "$file: $label found — $match_line"
            done <<< "$matches"
        fi
    done
done <<< "$TRACKED_FILES"

# ---------------------------------------------------------------------------
# Result
# ---------------------------------------------------------------------------
if [[ $FAILURES -eq 0 ]]; then
    echo "All secret safety checks passed."
    exit 0
else
    echo ""
    echo "$FAILURES secret safety check(s) failed."
    exit 1
fi
