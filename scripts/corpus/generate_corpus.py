#!/usr/bin/env python3
"""Generate the synthetic corpus and assemble PDFs under samples/corpus."""

from __future__ import annotations

import logging
import shutil
from pathlib import Path

from people import PEOPLE

from gen_behavioral import STAGING as BEHAVIORAL_STAGING
from gen_behavioral import TEMPLATE_ID as BEHAVIORAL_TEMPLATE
from gen_behavioral import main as run_behavioral
from gen_general import STAGING as GENERAL_STAGING
from gen_general import TEMPLATE_ID as GENERAL_TEMPLATE
from gen_general import main as run_general
from gen_housing import STAGING as HOUSING_STAGING
from gen_housing import TEMPLATE_ID as HOUSING_TEMPLATE
from gen_housing import main as run_housing
from gen_soap import STAGING as SOAP_STAGING
from gen_soap import TEMPLATE_ID as SOAP_TEMPLATE
from gen_soap import main as run_soap


PROJECT_ROOT = Path(__file__).resolve().parents[2]
CORPUS_ROOT = PROJECT_ROOT / "samples" / "corpus"

LOGGER = logging.getLogger(__name__)


def _person_tenant(person_id: str) -> str | None:
    for person in PEOPLE:
        if person["id"] == person_id:
            return person["tenant"]
    return None


def _tenant_path(tenant: str) -> Path:
    return CORPUS_ROOT / tenant


def _clear_pdfs(path: Path) -> None:
    if not path.exists():
        return
    for pdf in path.glob("*.pdf"):
        pdf.unlink()


def _clear_corpus() -> None:
    if not CORPUS_ROOT.exists():
        return

    for child in CORPUS_ROOT.iterdir():
        if child.is_file() and child.suffix.lower() == ".pdf":
            child.unlink()
            continue

        if child.is_dir():
            _clear_pdfs(child)
            if not any(child.iterdir()):
                child.rmdir()


def _clear_staging() -> None:
    for staging_dir in (GENERAL_STAGING, HOUSING_STAGING, BEHAVIORAL_STAGING, SOAP_STAGING):
        _clear_pdfs(Path(staging_dir))


def _copy_staging_pdfs(staging_dir: str) -> int:
    copied = 0
    source = Path(staging_dir)
    for pdf in source.glob("*.pdf"):
        person_id = pdf.stem.split("_")[0]
        tenant = _person_tenant(person_id)
        if tenant is None:
            LOGGER.warning(
                "Skipping PDF %s: no tenant mapping for person_id %s",
                pdf.name,
                person_id,
            )
            continue

        CORPUS_ROOT.mkdir(parents=True, exist_ok=True)
        tenant_dir = _tenant_path(tenant)
        tenant_dir.mkdir(parents=True, exist_ok=True)

        shutil.copy2(pdf, CORPUS_ROOT / pdf.name)
        shutil.copy2(pdf, tenant_dir / pdf.name)
        copied += 1

    return copied


def generate() -> None:
    _clear_corpus()
    _clear_staging()

    run_general()
    run_housing()
    run_behavioral()
    run_soap()

    counts = {
        GENERAL_TEMPLATE: _copy_staging_pdfs(GENERAL_STAGING),
        HOUSING_TEMPLATE: _copy_staging_pdfs(HOUSING_STAGING),
        BEHAVIORAL_TEMPLATE: _copy_staging_pdfs(BEHAVIORAL_STAGING),
        SOAP_TEMPLATE: _copy_staging_pdfs(SOAP_STAGING),
    }

    flat_total = len(list(CORPUS_ROOT.glob("*.pdf")))
    tenant_total = len(list(CORPUS_ROOT.glob("*/*.pdf")))

    print(f"Generated {flat_total} flat PDFs")
    print(f"Generated {tenant_total} tenant-organized PDFs")
    for template, count in counts.items():
        print(f"  {template}: {count}")


if __name__ == "__main__":
    generate()
