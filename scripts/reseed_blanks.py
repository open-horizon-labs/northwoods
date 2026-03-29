#!/usr/bin/env python3
"""Re-upload blank template PDFs to a running Northwoods instance.

Usage:
    python3 scripts/reseed_blanks.py --api https://northwoods-api.onrender.com

Logs in as admin for each tenant, then uploads the blank v2 PDFs via the
template blank-PDF endpoint. This overwrites whatever is currently stored
without touching any other data.
"""

import argparse
import os
import sys
import requests

TENANTS = [
    {"tenantId": "tenant-a", "email": "admin@sunrise.example", "password": "password"},
    {"tenantId": "tenant-b", "email": "admin@lakewood.example", "password": "password"},
]

TEMPLATES = [
    ("general-assistance", "samples/intakes/blank-general-assistance.pdf"),
    ("housing-stability", "samples/intakes/blank-housing-stability.pdf"),
    ("behavioral-health", "samples/intakes/blank-behavioral-health.pdf"),
    ("soap-note", "samples/intakes/blank-soap-note.pdf"),
]


def login(api: str, tenant: dict) -> str:
    r = requests.post(f"{api}/auth/login", json=tenant)
    r.raise_for_status()
    return r.json()["accessToken"]


def upload_blank(api: str, token: str, template_id: str, pdf_path: str) -> bool:
    with open(pdf_path, "rb") as f:
        r = requests.post(
            f"{api}/templates/{template_id}/blank-pdf",
            headers={"Authorization": f"Bearer {token}"},
            files={"file": (os.path.basename(pdf_path), f, "application/pdf")},
        )
    if r.ok:
        print(f"  ✓ {template_id}")
        return True
    else:
        print(f"  ✗ {template_id}: {r.status_code} {r.text[:200]}")
        return False


def main():
    parser = argparse.ArgumentParser(description="Re-upload blank template PDFs")
    parser.add_argument("--api", required=True, help="API base URL")
    args = parser.parse_args()

    api = args.api.rstrip("/")
    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    ok = True

    for tenant in TENANTS:
        print(f"\n{tenant['tenantId']}:")
        token = login(api, tenant)
        for template_id, rel_path in TEMPLATES:
            pdf_path = os.path.join(repo_root, rel_path)
            if not os.path.exists(pdf_path):
                print(f"  ✗ {template_id}: file not found at {pdf_path}")
                ok = False
                continue
            if not upload_blank(api, token, template_id, pdf_path):
                ok = False

    print("\nDone." if ok else "\nDone with errors.")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
