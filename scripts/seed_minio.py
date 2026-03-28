#!/usr/bin/env python3
"""
Upload corpus PDFs to MinIO using the seed SQL key mapping.

Usage:
  python3 scripts/seed_minio.py                         # local docker-compose defaults
  python3 scripts/seed_minio.py --endpoint s3.example.com --bucket intakes \\
      --access-key KEY --secret-key SECRET

Skips files already present in MinIO (idempotent).

Note: uses presigned POST (multipart) instead of direct PUT to work through
Cloudflare/CDN proxies that block S3 PUT requests.
"""
from __future__ import annotations
import argparse, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "corpus"))

import boto3
import requests
from botocore.exceptions import ClientError
from botocore.config import Config
from generate_seed_sql import v2_forms, v1_forms
from people import PEOPLE

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
CORPUS_DIR = os.path.join(REPO_ROOT, "samples", "corpus")


def build_manifest() -> list[tuple[str, str]]:
    """Returns [(minio_key, local_corpus_path), ...] for all seed documents."""
    entries = []
    for person in PEOPLE:
        for era, visit_num, template_id, _date, tenant in v2_forms(person):
            key = f"{tenant}/seed/{person['id']}_{visit_num:02d}_{era}_{template_id}.pdf"
            path = os.path.join(CORPUS_DIR, tenant, f"{person['id']}_{visit_num:02d}_{template_id}.pdf")
            entries.append((key, path))
        v1_idx: dict[str, int] = {}
        for era, visit_num, template_id, _date, tenant in v1_forms(person):
            v1_idx[template_id] = v1_idx.get(template_id, 0) + 1
            key = f"{tenant}/seed/{person['id']}_{visit_num:02d}_{era}_{template_id}.pdf"
            path = os.path.join(CORPUS_DIR, tenant, f"{person['id']}_{visit_num:02d}_{template_id}.pdf")
            entries.append((key, path))
    return entries


def seed(endpoint: str, bucket: str, access_key: str, secret_key: str, dry_run: bool = False):
    endpoint_url = f"http://{endpoint}" if "://" not in endpoint else endpoint
    s3 = boto3.client(
        "s3",
        endpoint_url=endpoint_url,
        aws_access_key_id=access_key,
        aws_secret_access_key=secret_key,
        region_name="us-east-1",
        config=Config(s3={"addressing_style": "path"}),
    )

    # Ensure bucket exists
    try:
        s3.head_bucket(Bucket=bucket)
    except ClientError:
        s3.create_bucket(Bucket=bucket)
        print(f"Created bucket: {bucket}")

    manifest = build_manifest()
    uploaded = skipped = missing = 0

    for key, path in manifest:
        if not os.path.exists(path):
            print(f"  MISSING source: {path}")
            missing += 1
            continue

        # Check if already in MinIO
        try:
            s3.head_object(Bucket=bucket, Key=key)
            skipped += 1
            continue
        except ClientError:
            pass

        if dry_run:
            print(f"  DRY-RUN would upload: {key}")
            uploaded += 1
            continue

        # Use presigned POST (multipart) instead of direct PUT to work through
        # CDN/proxy layers (e.g. Cloudflare) that block S3 PUT requests.
        presigned = s3.generate_presigned_post(
            Bucket=bucket,
            Key=key,
            Fields={"Content-Type": "application/pdf"},
            Conditions=[{"Content-Type": "application/pdf"}],
            ExpiresIn=3600,
        )
        with open(path, "rb") as f:
            resp = requests.post(
                presigned["url"],
                data=presigned["fields"],
                files={"file": (os.path.basename(path), f, "application/pdf")},
                timeout=60,
            )
        if resp.status_code not in (200, 204):
            print(f"  FAILED ({resp.status_code}): {key}")
            print(f"    {resp.text[:200]}")
            continue
        print(f"  uploaded: {key}")
        uploaded += 1

    print(f"\nDone. uploaded={uploaded}  skipped(already exist)={skipped}  missing={missing}")


def main():
    parser = argparse.ArgumentParser(description="Seed MinIO with corpus PDFs")
    parser.add_argument("--endpoint", default="localhost:9000")
    parser.add_argument("--bucket", default="intakes")
    parser.add_argument("--access-key", default="northwoods")
    parser.add_argument("--secret-key", default="northwoods")
    parser.add_argument("--dry-run", action="store_true", help="Print what would be uploaded without uploading")
    args = parser.parse_args()

    print(f"Seeding MinIO at {args.endpoint} bucket={args.bucket}")
    seed(args.endpoint, args.bucket, args.access_key, args.secret_key, dry_run=args.dry_run)


if __name__ == "__main__":
    main()
