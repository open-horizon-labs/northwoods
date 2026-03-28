#!/usr/bin/env python3
"""
Collect generated PDFs from staging dirs into samples/corpus/.

Run after all gen scripts have been run:
  python3 scripts/corpus/gen_general_v2.py
  python3 scripts/corpus/gen_general.py
  python3 scripts/corpus/gen_housing_v2.py
  python3 scripts/corpus/gen_housing.py
  python3 scripts/corpus/gen_behavioral_v2.py
  python3 scripts/corpus/gen_behavioral.py
  python3 scripts/corpus/gen_soap_v2.py
  python3 scripts/corpus/gen_soap.py

Then run:
  python3 scripts/corpus/collect_corpus.py
"""
from __future__ import annotations
import sys, os, shutil

sys.path.insert(0, os.path.dirname(__file__))
from people import PEOPLE
from generate_seed_sql import v2_forms, v1_forms

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../.."))
CORPUS_DIR = os.path.join(REPO_ROOT, "samples", "corpus")

V2_STAGING = "/tmp/corpus-v2"
V1_STAGING = "/tmp/corpus-staging"

# Maps template_id -> staging subdirectory name
TEMPLATE_DIR = {
    "general-assistance": "general",
    "housing-stability": "housing",
    "behavioral-health": "behavioral",
    "soap-note": "soap",
}

def collect():
    copied = 0
    missing = []

    for person in PEOPLE:
        # ── V2 era ──────────────────────────────────────────────────────────
        for era, visit_num, template_id, _date, tenant in v2_forms(person):
            subdir = TEMPLATE_DIR.get(template_id)
            if not subdir:
                continue
            # v2 gen scripts output with _v2_ in filename
            src_name = f"{person['id']}_{visit_num:02d}_v2_{template_id}.pdf"
            src = os.path.join(V2_STAGING, subdir, src_name)
            dest_dir = os.path.join(CORPUS_DIR, tenant)
            dest = os.path.join(dest_dir, f"{person['id']}_{visit_num:02d}_{template_id}.pdf")

            if not os.path.exists(src):
                missing.append(f"v2 {src}")
                continue
            os.makedirs(dest_dir, exist_ok=True)
            shutil.copy2(src, dest)
            copied += 1

        # ── V1 era ──────────────────────────────────────────────────────────
        v1_idx = {}  # template_id -> per-template index (1-based within v1 gen output)
        for era, visit_num, template_id, _date, tenant in v1_forms(person):
            subdir = TEMPLATE_DIR.get(template_id)
            if not subdir:
                continue
            # v1 gen scripts start visit nums from 1 per template, independently
            # reconstruct which v1 file this corresponds to
            v1_idx[template_id] = v1_idx.get(template_id, 0) + 1
            local_idx = v1_idx[template_id]

            src_name = f"{person['id']}_{local_idx:02d}_{template_id}.pdf"
            src = os.path.join(V1_STAGING, subdir, src_name)
            dest_dir = os.path.join(CORPUS_DIR, tenant)
            dest = os.path.join(dest_dir, f"{person['id']}_{visit_num:02d}_{template_id}.pdf")

            if not os.path.exists(src):
                missing.append(f"v1 {src}")
                continue
            os.makedirs(dest_dir, exist_ok=True)
            shutil.copy2(src, dest)
            copied += 1

    print(f"\n=== Corpus collection ===")
    print(f"Copied: {copied}")
    if missing:
        print(f"Missing ({len(missing)}):")
        for m in missing:
            print(f"  {m}")
    else:
        print("No missing sources.")

    # Final count
    total = sum(len(os.listdir(os.path.join(CORPUS_DIR, d)))
                for d in os.listdir(CORPUS_DIR)
                if os.path.isdir(os.path.join(CORPUS_DIR, d)))
    print(f"Total PDFs in samples/corpus/: {total}")


if __name__ == "__main__":
    collect()
