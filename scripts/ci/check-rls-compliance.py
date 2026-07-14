#!/usr/bin/env python3
"""
ADR 004 compliance check: RLS tenant isolation.

SQL checks (infra/postgres/init.sql):
  - Every table with tenant_id column has ENABLE ROW LEVEL SECURITY
  - Every RLS-enabled table has a policy using current_setting('app.tenant_id', true)
  - app_user role does not have BYPASS RLS

Deployment checks (render.yaml):
  - Production enables the restricted app_user role
  - Production does not seed the known local demo administrators

C# checks (src/**/*.cs):
  - No file outside exempt paths uses `new NpgsqlConnection` directly
    Exempt: DbConnectionFactory.cs, PostgresHealthCheck.cs, Workers/Extraction.Worker/**
"""

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
INIT_SQL = REPO_ROOT / "infra" / "postgres" / "init.sql"
RENDER_YAML = REPO_ROOT / "render.yaml"
SRC_DIR = REPO_ROOT / "src"
API_PROGRAM = SRC_DIR / "Services" / "Northwoods.Api" / "Program.cs"

# Files allowed to use NpgsqlConnection directly (superuser / infrastructure code).
EXEMPT_CS_PATTERNS = [
    "BuildingBlocks/Northwoods.Tenancy/DbConnectionFactory.cs",
    "BuildingBlocks/Northwoods.Tenancy/DatabaseInitializer.cs",
    "BuildingBlocks/Northwoods.Tenancy/PostgresHealthCheck.cs",
    "Workers/Extraction.Worker/",
]

errors: list[str] = []


def relative(path: Path) -> str:
    try:
        return str(path.relative_to(REPO_ROOT))
    except ValueError:
        return str(path)


# ---------------------------------------------------------------------------
# SQL checks
# ---------------------------------------------------------------------------

def check_sql() -> None:
    if not INIT_SQL.exists():
        errors.append(f"MISSING: {INIT_SQL} not found")
        return

    sql = INIT_SQL.read_text()
    sql_upper = sql.upper()

    # Find all tables that declare tenant_id columns.
    # Match: CREATE TABLE [IF NOT EXISTS] <name> ( ... tenant_id ... )
    table_block_re = re.compile(
        r"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(\w+)\s*\(([^;]+?)\);",
        re.IGNORECASE | re.DOTALL,
    )

    tenant_tables: list[str] = []
    for m in table_block_re.finditer(sql):
        table_name = m.group(1).lower()
        body = m.group(2)
        if re.search(r"\btenant_id\b", body, re.IGNORECASE):
            tenant_tables.append(table_name)

    if not tenant_tables:
        errors.append("SQL: No tables with tenant_id found — check parser logic")
        return

    print(f"  Tables with tenant_id: {', '.join(tenant_tables)}")

    for table in tenant_tables:
        # Check ENABLE ROW LEVEL SECURITY
        rls_pattern = re.compile(
            rf"\bALTER\s+TABLE\s+{re.escape(table)}\s+ENABLE\s+ROW\s+LEVEL\s+SECURITY\b",
            re.IGNORECASE,
        )
        if not rls_pattern.search(sql):
            errors.append(f"SQL: table '{table}' has tenant_id but no ENABLE ROW LEVEL SECURITY")

        # Check policy using current_setting('app.tenant_id', true)
        policy_on_re = re.compile(
            rf"\bCREATE\s+POLICY\s+\w+\s+ON\s+{re.escape(table)}\b",
            re.IGNORECASE | re.DOTALL,
        )
        if not policy_on_re.search(sql):
            errors.append(f"SQL: table '{table}' has no CREATE POLICY")
        else:
            # Find the policy block and verify it uses current_setting
            # Policies end at the next semicolon.
            policy_blocks = re.findall(
                rf"CREATE\s+POLICY\s+\w+\s+ON\s+{re.escape(table)}\b.*?;",
                sql,
                re.IGNORECASE | re.DOTALL,
            )
            has_tenant_setting = any(
                "current_setting('app.tenant_id'" in block or
                'current_setting("app.tenant_id"' in block
                for block in policy_blocks
            )
            if not has_tenant_setting:
                errors.append(
                    f"SQL: policy on '{table}' does not reference "
                    "current_setting('app.tenant_id', true)"
                )

    # Check app_user does not have BYPASS RLS
    bypass_re = re.compile(
        r"\bapp_user\b.*?\bBYPASS\s+RLS\b|\bBYPASS\s+RLS\b.*?\bapp_user\b",
        re.IGNORECASE,
    )
    if bypass_re.search(sql):
        errors.append("SQL: app_user role has BYPASS RLS — violates ADR 004")

    alter_bypass_re = re.compile(
        r"\bALTER\s+ROLE\s+app_user\b.*?\bBYPASS\s+RLS\b",
        re.IGNORECASE | re.DOTALL,
    )
    if alter_bypass_re.search(sql):
        errors.append("SQL: ALTER ROLE app_user BYPASS RLS found — violates ADR 004")

    if re.search(r"CREATE\s+ROLE\s+app_user\b[^;]*\bLOGIN\b", sql, re.IGNORECASE):
        errors.append("SQL: app_user can log in directly — use a NOLOGIN role for SET LOCAL ROLE")


# ---------------------------------------------------------------------------
# C# checks
# ---------------------------------------------------------------------------

def is_exempt(path: Path) -> bool:
    rel = relative(path)
    return any(exempt in rel for exempt in EXEMPT_CS_PATTERNS)


def check_csharp() -> None:
    cs_files = list(SRC_DIR.rglob("*.cs"))
    if not cs_files:
        errors.append(f"C#: No .cs files found under {SRC_DIR}")
        return

    for cs_file in cs_files:
        if is_exempt(cs_file):
            continue
        content = cs_file.read_text(errors="replace")
        # Look for `new NpgsqlConnection` — raw connection creation outside approved paths.
        matches = list(re.finditer(r"\bnew\s+NpgsqlConnection\b", content))
        for m in matches:
            lineno = content[: m.start()].count("\n") + 1
            errors.append(
                f"C#: {relative(cs_file)}:{lineno} — raw `new NpgsqlConnection` "
                "outside approved paths; use DbConnectionFactory.OpenSessionAsync"
            )


# ---------------------------------------------------------------------------
# Deployment checks
# ---------------------------------------------------------------------------

def check_render_config() -> None:
    if not RENDER_YAML.exists():
        errors.append(f"MISSING: {RENDER_YAML} not found")
        return

    render_config = RENDER_YAML.read_text()

    def configured_value(key: str):
        match = re.search(
            rf"-\s+key:\s*{re.escape(key)}\s*\n\s*value:\s*[\"']?([^\"'\s]+)",
            render_config,
        )
        return match.group(1).lower() if match else None

    use_app_user_role = configured_value("Database__UseAppUserRole")
    if use_app_user_role != "true":
        errors.append(
            "Render: Database__UseAppUserRole must be explicitly true; "
            "the database owner bypasses RLS"
        )

    seed_demo_admins = configured_value("Database__SeedDemoAdmins")
    if seed_demo_admins != "false":
        errors.append(
            "Render: Database__SeedDemoAdmins must be explicitly false; "
            "known local demo administrators cannot exist in the public deployment"
        )

    if not API_PROGRAM.exists():
        errors.append(f"MISSING: {API_PROGRAM} not found")
        return

    program = API_PROGRAM.read_text()
    if "app.Environment.IsProduction() && !useAppUserRole" not in program:
        errors.append("API: production startup does not reject disabled RLS")
    if "app.Environment.IsProduction() && seedDemoAdmins" not in program:
        errors.append("API: production startup does not reject seeded demo administrators")
    if "AssertDemoAdminsAbsentAsync" not in program:
        errors.append("API: startup does not verify that demo administrators were removed")


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main() -> int:
    print("=== ADR 004 RLS Compliance Check ===")
    print(f"  SQL:  {relative(INIT_SQL)}")
    print(f"  C#:   {relative(SRC_DIR)}")
    print(f"  Deploy: {relative(RENDER_YAML)}")

    check_sql()
    check_csharp()
    check_render_config()

    if errors:
        print("\nFAILURES:")
        for e in errors:
            print(f"  [FAIL] {e}")
        return 1

    print("\nAll RLS compliance checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
