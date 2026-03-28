#!/usr/bin/env python3
"""
Reset the Northwoods demo environment: wipe all documents, then seed via the real intake API.

IDEMPOTENCY
-----------
--clean   Idempotent. Wipes all documents for both tenants. Safe to run repeatedly.
--seed    NOT idempotent. Each run uploads 184 new documents. Always run --clean first
          (or use the default combined mode which does clean + seed in one go).

Usage
-----
  # Clean + seed (default — recommended)
  python3 scripts/reset_demo.py --api https://northwoods-api.onrender.com

  # Clean only
  python3 scripts/reset_demo.py --api https://northwoods-api.onrender.com --clean-only

  # Seed only (only if you know what you're doing — creates duplicates if already seeded)
  python3 scripts/reset_demo.py --api https://northwoods-api.onrender.com --seed-only

  # Local docker-compose
  python3 scripts/reset_demo.py --api http://localhost:5100

Requirements: pip install requests
"""
from __future__ import annotations
import argparse, os, sys, time
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "corpus"))

import requests
from generate_seed_sql import v2_forms, v1_forms, TENANT_IDS
from people import PEOPLE

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
CORPUS_DIR = os.path.join(REPO_ROOT, "samples", "corpus")

# Demo credentials (same for all users, set in DatabaseInitializer)
PASSWORD = "password"

TENANT_CREDS = {
    "tenant-a": {"email": "worker@sunrise.example",  "friendly": "sunrise"},
    "tenant-b": {"email": "worker@lakewood.example", "friendly": "lakewood"},
}
ADMIN_CREDS = {
    "tenant-a": {"email": "admin@sunrise.example"},
    "tenant-b": {"email": "admin@lakewood.example"},
}


def login(api: str, email: str) -> str:
    resp = requests.post(f"{api}/auth/login", json={"email": email, "password": PASSWORD}, timeout=15)
    resp.raise_for_status()
    return resp.json()["accessToken"]


def clean_tenant(api: str, tenant_id: str) -> int:
    token = login(api, ADMIN_CREDS[tenant_id]["email"])
    resp = requests.delete(
        f"{api}/admin/documents",
        headers={"Authorization": f"Bearer {token}"},
        timeout=30,
    )
    resp.raise_for_status()
    deleted = resp.json().get("deleted", 0)
    print(f"  {tenant_id}: deleted {deleted} documents")
    return deleted


def build_manifest() -> list[tuple[str, str, str, str]]:
    """Returns [(tenant_id, friendly_name, template_id, local_path), ...] for all 184 seed docs."""
    entries = []
    for person in PEOPLE:
        for _era, _visit_num, template_id, _date, friendly_tenant in v2_forms(person):
            tenant_id = TENANT_IDS.get(friendly_tenant, friendly_tenant)
            path = os.path.join(CORPUS_DIR, friendly_tenant,
                                f"{person['id']}_{_visit_num:02d}_{template_id}.pdf")
            entries.append((tenant_id, friendly_tenant, template_id, path))
        for _era, _visit_num, template_id, _date, friendly_tenant in v1_forms(person):
            tenant_id = TENANT_IDS.get(friendly_tenant, friendly_tenant)
            path = os.path.join(CORPUS_DIR, friendly_tenant,
                                f"{person['id']}_{_visit_num:02d}_{template_id}.pdf")
            entries.append((tenant_id, friendly_tenant, template_id, path))
    return entries


def seed(api: str):
    # Get one token per tenant (worker role can upload)
    tokens: dict[str, str] = {}
    for tenant_id, creds in TENANT_CREDS.items():
        tokens[tenant_id] = login(api, creds["email"])
        print(f"  logged in as {creds['email']}")

    manifest = build_manifest()
    uploaded = skipped = failed = 0

    for i, (tenant_id, friendly_tenant, template_id, path) in enumerate(manifest, 1):
        if not os.path.exists(path):
            print(f"  [{i}/{len(manifest)}] MISSING: {path}")
            skipped += 1
            continue

        filename = os.path.basename(path)
        try:
            with open(path, "rb") as f:
                resp = requests.post(
                    f"{api}/intakes",
                    headers={"Authorization": f"Bearer {tokens[tenant_id]}"},
                    data={"templateId": template_id},
                    files={"file": (filename, f, "application/pdf")},
                    timeout=30,
                )
            if resp.status_code == 202:
                doc_id = resp.json().get("docId", "?")
                print(f"  [{i}/{len(manifest)}] queued {filename} → {doc_id}")
                uploaded += 1
            else:
                print(f"  [{i}/{len(manifest)}] FAILED ({resp.status_code}): {filename} — {resp.text[:100]}")
                failed += 1
        except Exception as e:
            print(f"  [{i}/{len(manifest)}] ERROR: {filename} — {e}")
            failed += 1

        # Small pause to avoid overwhelming the API
        if i % 10 == 0:
            time.sleep(0.5)

    print(f"\nDone. queued={uploaded}  skipped(missing)={skipped}  failed={failed}")
    print("Worker will process documents in batches of 10 every ~5s.")
    print("Watch progress at the submissions page — status moves: Queued → Processing → Ready for Review")


def main():
    parser = argparse.ArgumentParser(description="Reset Northwoods demo data")
    parser.add_argument("--api", default="http://localhost:5100",
                        help="API base URL (default: http://localhost:5100)")
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--clean-only", action="store_true", help="Wipe documents only, do not seed")
    group.add_argument("--seed-only", action="store_true",
                       help="Seed only — NOT idempotent, creates duplicates if run twice")
    args = parser.parse_args()

    do_clean = not args.seed_only
    do_seed = not args.clean_only

    if do_clean:
        print(f"=== Cleaning all documents ({args.api}) ===")
        for tenant_id in TENANT_CREDS:
            clean_tenant(args.api, tenant_id)
        print()

    if do_seed:
        if args.seed_only:
            print("WARNING: --seed-only is not idempotent. Running this twice creates duplicate documents.")
            print()
        print(f"=== Seeding 184 documents via intake API ({args.api}) ===")
        seed(args.api)


if __name__ == "__main__":
    main()
