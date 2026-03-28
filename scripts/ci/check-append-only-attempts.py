#!/usr/bin/env python3
"""
ADR 005 compliance check: extraction_attempts is append-only.

SQL checks (infra/postgres/init.sql):
  - No ON CONFLICT ... DO UPDATE targeting extraction_attempts

C# checks (src/**/*.cs and infra/**/*.sql):
  - No UPDATE extraction_attempts statement
  - No DELETE FROM extraction_attempts statement
"""

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
INIT_SQL = REPO_ROOT / "infra" / "postgres" / "init.sql"
SRC_DIR = REPO_ROOT / "src"

errors: list[str] = []


def relative(path: Path) -> str:
    try:
        return str(path.relative_to(REPO_ROOT))
    except ValueError:
        return str(path)


# ---------------------------------------------------------------------------
# SQL check
# ---------------------------------------------------------------------------

def check_sql() -> None:
    if not INIT_SQL.exists():
        errors.append(f"MISSING: {INIT_SQL} not found")
        return

    sql = INIT_SQL.read_text()

    # ON CONFLICT ... DO UPDATE on extraction_attempts
    # Strategy: find any ON CONFLICT block and check if it's preceded by an
    # INSERT INTO extraction_attempts context within a reasonable window.
    for m in re.finditer(
        r"ON\s+CONFLICT\s*\([^)]+\)\s*DO\s+UPDATE",
        sql,
        re.IGNORECASE | re.DOTALL,
    ):
        # Look back up to 500 chars for INSERT INTO extraction_attempts
        start = max(0, m.start() - 500)
        preceding = sql[start : m.start()]
        if re.search(r"\bINSERT\s+INTO\s+extraction_attempts\b", preceding, re.IGNORECASE):
            lineno = sql[: m.start()].count("\n") + 1
            errors.append(
                f"SQL: {relative(INIT_SQL)}:{lineno} — ON CONFLICT DO UPDATE targeting "
                "extraction_attempts violates append-only invariant (ADR 005)"
            )


# ---------------------------------------------------------------------------
# C# + SQL file checks
# ---------------------------------------------------------------------------

# Files legitimately allowed to DELETE/UPDATE extraction_attempts.
# DatabaseInitializer: removes mock/test data on startup (not live data mutations).
# AdminEndpoints: tenant-reset endpoint used only by admins to wipe a tenant's data.
EXEMPT_PATHS = [
    "BuildingBlocks/Northwoods.Tenancy/DatabaseInitializer.cs",
    "Services/Northwoods.Api/Endpoints/AdminEndpoints.cs",
]

MUTATION_PATTERNS = [
    # UPDATE extraction_attempts (with optional SET clause starting)
    (re.compile(r"\bUPDATE\s+extraction_attempts\b", re.IGNORECASE), "UPDATE"),
    # DELETE FROM extraction_attempts
    (re.compile(r"\bDELETE\s+FROM\s+extraction_attempts\b", re.IGNORECASE), "DELETE FROM"),
    # TRUNCATE extraction_attempts
    (re.compile(r"\bTRUNCATE\s+(?:TABLE\s+)?extraction_attempts\b", re.IGNORECASE), "TRUNCATE"),
]


def is_exempt(path: Path) -> bool:
    rel = relative(path)
    return any(exempt in rel for exempt in EXEMPT_PATHS)


def check_files(files: list[Path]) -> None:
    for path in files:
        if is_exempt(path):
            continue
        content = path.read_text(errors="replace")
        for pattern, label in MUTATION_PATTERNS:
            for m in pattern.finditer(content):
                lineno = content[: m.start()].count("\n") + 1
                errors.append(
                    f"{relative(path)}:{lineno} — `{label} extraction_attempts` "
                    "violates append-only invariant (ADR 005)"
                )


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main() -> int:
    print("=== ADR 005 Append-Only Extraction Attempts Check ===")

    check_sql()

    cs_files = list(SRC_DIR.rglob("*.cs"))
    sql_files = list((REPO_ROOT / "infra").rglob("*.sql"))
    check_files(cs_files + sql_files)

    if errors:
        print("\nFAILURES:")
        for e in errors:
            print(f"  [FAIL] {e}")
        return 1

    print("All append-only checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
