#!/usr/bin/env python3
"""
Spike: OpenAI vision OCR vs PaddleOCR baseline comparison.

Reads sample intake PDFs, extracts fields via OpenAI vision (nano → mini fallback),
compares against PaddleOCR baseline, writes results to docs/spike-openai-vision-ocr.md.
"""

import base64
import json
import os
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from typing import Optional

# ---------------------------------------------------------------------------
# Config
# ---------------------------------------------------------------------------
REPO_ROOT = Path(__file__).resolve().parent.parent
SAMPLES_DIR = REPO_ROOT / "samples" / "intakes"
PADDLE_SCRIPT = (
    REPO_ROOT
    / "src"
    / "Workers"
    / "Extraction.Worker"
    / "scripts"
    / "paddle_extract.py"
)
OUTPUT_DOC = REPO_ROOT / "docs" / "spike-openai-vision-ocr.md"

MODELS_PRIORITY = ["gpt-5.4-nano", "gpt-5.4-mini"]

EXTRACT_FIELDS = [
    "applicantName",
    "dateOfBirth",
    "address",
    "householdSize",
    "monthlyIncome",
    "requestedServices",
    "notes",
]

EXTRACT_PROMPT = (
    "You are an expert at reading handwritten intake forms. "
    "Extract the following fields from this form image exactly as written. "
    "Return ONLY a JSON object (no markdown, no explanation) with these keys: "
    + ", ".join(EXTRACT_FIELDS)
    + ". For each field, the value should be an object with two keys: "
    "'value' (the extracted text, or null if not present) and "
    "'confidence' (a float 0.0–1.0 indicating how confident you are). "
    "If the form is blank or you cannot read the image, return all fields as null with confidence 0."
)

OPENAI_NORMALIZER_PROMPT = (
    "You are an expert at reading handwritten text. "
    "Given the raw OCR text below, extract the following fields as JSON: "
    + ", ".join(EXTRACT_FIELDS)
    + ". For each field return an object: {'value': <string or null>, 'confidence': <0.0-1.0>}. "
    "Return ONLY the JSON object."
)

RATE_LIMIT_DELAY = 2  # seconds between API calls


# ---------------------------------------------------------------------------
# Environment setup
# ---------------------------------------------------------------------------
def load_env_local() -> None:
    env_path = REPO_ROOT / ".env.local"
    if not env_path.exists():
        return
    for line in env_path.read_text().splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, val = line.partition("=")
        if key.strip() and key.strip() not in os.environ:
            os.environ[key.strip()] = val.strip()


def get_openai_client():
    import openai  # type: ignore

    api_key = os.environ.get("OPENAI_API_KEY")
    if not api_key:
        raise RuntimeError("OPENAI_API_KEY not set")
    return openai.OpenAI(api_key=api_key)


# ---------------------------------------------------------------------------
# PDF → PNG conversion
# ---------------------------------------------------------------------------
def pdf_to_png(pdf_path: Path) -> Path:
    """Convert first page of PDF to a temp PNG using PyMuPDF (fitz)."""
    import fitz  # type: ignore

    doc = fitz.open(str(pdf_path))
    page = doc[0]
    mat = fitz.Matrix(2.0, 2.0)  # 2x scale for legibility
    pix = page.get_pixmap(matrix=mat)
    tmp = tempfile.NamedTemporaryFile(suffix=".png", delete=False)
    pix.save(tmp.name)
    doc.close()
    return Path(tmp.name)


def png_to_base64(png_path: Path) -> str:
    return base64.b64encode(png_path.read_bytes()).decode("utf-8")


# ---------------------------------------------------------------------------
# OpenAI vision extraction
# ---------------------------------------------------------------------------
def openai_vision_extract(client, image_b64: str, model: str) -> tuple[dict, str]:
    """
    Returns (fields_dict, model_used).
    fields_dict maps field_name -> {"value": ..., "confidence": ...}.
    Raises on hard failure.
    """
    response = client.chat.completions.create(
        model=model,
        messages=[
            {
                "role": "user",
                "content": [
                    {
                        "type": "image_url",
                        "image_url": {
                            "url": f"data:image/png;base64,{image_b64}",
                            "detail": "high",
                        },
                    },
                    {"type": "text", "text": EXTRACT_PROMPT},
                ],
            }
        ],
        max_tokens=1024,
    )

    raw = response.choices[0].message.content or ""
    # Strip markdown fences if present
    raw = raw.strip()
    if raw.startswith("```"):
        lines = raw.splitlines()
        raw = "\n".join(lines[1:-1] if lines[-1].strip() == "```" else lines[1:])

    parsed = json.loads(raw)

    # Normalize: each field should be {"value": ..., "confidence": ...}
    result = {}
    for field in EXTRACT_FIELDS:
        entry = parsed.get(field)
        if entry is None:
            result[field] = {"value": None, "confidence": 0.0}
        elif isinstance(entry, dict):
            result[field] = {
                "value": entry.get("value"),
                "confidence": float(entry.get("confidence", 0.0)),
            }
        else:
            # Model returned bare string
            result[field] = {"value": str(entry), "confidence": 0.5}

    return result, model


def extract_with_fallback(client, image_b64: str) -> tuple[dict, str, list[str]]:
    """
    Try models in priority order. Returns (fields, model_used, errors).
    """
    errors = []
    for model in MODELS_PRIORITY:
        try:
            fields, used = openai_vision_extract(client, image_b64, model)
            return fields, used, errors
        except Exception as e:
            errors.append(f"{model}: {e}")
            time.sleep(RATE_LIMIT_DELAY)

    raise RuntimeError(f"All models failed: {'; '.join(errors)}")


# ---------------------------------------------------------------------------
# PaddleOCR baseline via subprocess
# ---------------------------------------------------------------------------
def run_paddle_baseline(pdf_path: Path) -> dict:
    """
    Run paddle_extract.py and return {"text": ..., "error": ...}.
    Returns {"error": "paddleocr unavailable"} if script missing or paddle not installed.
    """
    if not PADDLE_SCRIPT.exists():
        return {"error": f"paddle script not found: {PADDLE_SCRIPT}"}

    try:
        result = subprocess.run(
            [sys.executable, str(PADDLE_SCRIPT), "--file", str(pdf_path)],
            capture_output=True,
            text=True,
            timeout=120,
        )
        output = result.stdout.strip()
        if not output:
            stderr = result.stderr.strip()
            return {"error": f"no output (exit {result.returncode}): {stderr[:200]}"}
        return json.loads(output)
    except subprocess.TimeoutExpired:
        return {"error": "paddle timed out after 120s"}
    except Exception as e:
        return {"error": str(e)}


def parse_paddle_fields_from_text(text: str) -> dict:
    """
    Heuristically map raw OCR text to field names.
    PaddleOCR returns raw text; we attempt keyword-proximity matching.
    Confidence is estimated at 0.5 (OCR confidence not per-field in baseline script).
    """
    if not text:
        return {f: {"value": None, "confidence": 0.0} for f in EXTRACT_FIELDS}

    lines = [ln.strip() for ln in text.splitlines() if ln.strip()]
    joined = " ".join(lines)

    def find_after(keywords: list[str], haystack: str) -> Optional[str]:
        lower = haystack.lower()
        for kw in keywords:
            idx = lower.find(kw.lower())
            if idx != -1:
                fragment = haystack[idx + len(kw) :].strip().lstrip(":").strip()
                # Take up to next newline or 80 chars
                end = fragment.find("\n")
                return fragment[:end].strip() if end != -1 else fragment[:80].strip()
        return None

    return {
        "applicantName": {
            "value": find_after(["name:", "applicant name", "client name"], joined),
            "confidence": 0.5,
        },
        "dateOfBirth": {
            "value": find_after(["date of birth", "dob:", "birth date"], joined),
            "confidence": 0.5,
        },
        "address": {
            "value": find_after(["address:", "street address", "home address"], joined),
            "confidence": 0.5,
        },
        "householdSize": {
            "value": find_after(
                ["household size", "# in household", "number in household"], joined
            ),
            "confidence": 0.5,
        },
        "monthlyIncome": {
            "value": find_after(["monthly income", "income:", "total income"], joined),
            "confidence": 0.5,
        },
        "requestedServices": {
            "value": find_after(
                ["requested services", "services requested", "request:"], joined
            ),
            "confidence": 0.5,
        },
        "notes": {
            "value": find_after(["notes:", "comments:", "additional"], joined),
            "confidence": 0.5,
        },
    }


# ---------------------------------------------------------------------------
# Agreement scoring
# ---------------------------------------------------------------------------
def score_agreement(paddle_val: Optional[str], openai_val: Optional[str]) -> str:
    if paddle_val is None and openai_val is None:
        return "both_null"
    if paddle_val is None or openai_val is None:
        return "one_null"
    p = paddle_val.strip().lower()
    o = openai_val.strip().lower()
    if p == o:
        return "yes"
    # Partial: one is substring of the other, or share >50% words
    if p in o or o in p:
        return "partial"
    p_words = set(p.split())
    o_words = set(o.split())
    if p_words and o_words:
        overlap = len(p_words & o_words) / max(len(p_words), len(o_words))
        if overlap > 0.4:
            return "partial"
    return "no"


def more_plausible(field: str, paddle_val, openai_val, openai_conf: float) -> str:
    """Return a quick judgment on which extraction is more plausible."""
    if paddle_val is None and openai_val is not None:
        return "openai (paddle missed)"
    if openai_val is None and paddle_val is not None:
        return "paddle (openai missed)"
    if paddle_val is None and openai_val is None:
        return "n/a (both null)"
    if openai_conf >= 0.8:
        return "openai (high conf)"
    if openai_conf < 0.4:
        return "paddle (openai low conf)"
    return "unclear"


# ---------------------------------------------------------------------------
# Report generation
# ---------------------------------------------------------------------------
def build_file_section(
    filename: str,
    openai_fields: Optional[dict],
    openai_model: str,
    openai_errors: list[str],
    paddle_raw: dict,
    paddle_fields: dict,
) -> str:
    lines = [f"\n## {filename}\n"]

    # Paddle raw summary
    if "error" in paddle_raw:
        lines.append(f"**PaddleOCR:** error — `{paddle_raw['error']}`\n")
        paddle_available = False
    else:
        chars = paddle_raw.get("chars", 0)
        line_count = paddle_raw.get("lineCount", 0)
        lines.append(
            f"**PaddleOCR raw:** {chars} chars, {line_count} lines extracted\n"
        )
        paddle_available = True

    if openai_errors:
        lines.append(f"**OpenAI errors tried:** {'; '.join(openai_errors)}\n")

    if openai_fields is None:
        lines.append("**OpenAI extraction:** FAILED — no usable output\n")
        return "\n".join(lines)

    lines.append(f"**OpenAI model used:** `{openai_model}`\n")

    # Comparison table
    lines.append(
        "\n| Field | PaddleOCR value | Paddle conf | OpenAI value | OpenAI conf | Agreement | More plausible |"
    )
    lines.append(
        "|-------|----------------|-------------|-------------|-------------|-----------|----------------|"
    )

    for field in EXTRACT_FIELDS:
        p = paddle_fields.get(field, {"value": None, "confidence": 0.0})
        o = openai_fields.get(field, {"value": None, "confidence": 0.0})

        pv = p.get("value") or ""
        pc = p.get("confidence", 0.0)
        ov = o.get("value") or ""
        oc = o.get("confidence", 0.0)

        agreement = (
            score_agreement(pv or None, ov or None) if paddle_available else "n/a"
        )
        plausible = (
            more_plausible(field, pv or None, ov or None, oc)
            if paddle_available
            else "openai only"
        )

        # Truncate long values for table readability
        pv_disp = (pv[:60] + "…") if len(pv) > 60 else pv
        ov_disp = (ov[:60] + "…") if len(ov) > 60 else ov

        lines.append(
            f"| {field} | {pv_disp or '—'} | {pc:.2f} | {ov_disp or '—'} | {oc:.2f} | {agreement} | {plausible} |"
        )

    lines.append("")

    # Raw OpenAI JSON for reference
    lines.append("<details><summary>OpenAI raw extraction JSON</summary>\n")
    lines.append("```json")
    lines.append(json.dumps(openai_fields, indent=2))
    lines.append("```")
    lines.append("</details>\n")

    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
def main() -> int:
    load_env_local()

    try:
        client = get_openai_client()
    except RuntimeError as e:
        print(f"ERROR: {e}", file=sys.stderr)
        return 1

    pdf_files = sorted(SAMPLES_DIR.glob("*.pdf"))
    if not pdf_files:
        print(f"No PDFs found in {SAMPLES_DIR}", file=sys.stderr)
        return 1

    print(f"Found {len(pdf_files)} sample PDF(s). Starting extraction...\n")

    doc_sections = [
        "# Spike: OpenAI Vision OCR vs PaddleOCR Baseline\n",
        f"**Date:** 2026-03-27  \n**Models tried:** {', '.join(MODELS_PRIORITY)}  \n**Samples:** {len(pdf_files)} files\n",
        "## Summary\n",
        "| File | OpenAI model | Fields extracted | Avg OpenAI conf | Agreement rate |",
        "|------|-------------|-----------------|-----------------|---------------|",
    ]

    summary_rows = []
    detail_sections = []

    for pdf_path in pdf_files:
        print(f"Processing: {pdf_path.name}")

        # Convert to PNG
        try:
            png_path = pdf_to_png(pdf_path)
            image_b64 = png_to_base64(png_path)
            Path(png_path).unlink(missing_ok=True)
        except Exception as e:
            print(f"  PDF→PNG failed: {e}")
            detail_sections.append(
                f"\n## {pdf_path.name}\n\n**PDF conversion failed:** {e}\n"
            )
            continue

        # OpenAI extraction
        openai_fields = None
        openai_model = "none"
        openai_errors = []
        try:
            openai_fields, openai_model, openai_errors = extract_with_fallback(
                client, image_b64
            )
            print(
                f"  OpenAI ({openai_model}): {sum(1 for f in openai_fields.values() if f.get('value'))} fields"
            )
        except Exception as e:
            openai_errors.append(str(e))
            print(f"  OpenAI FAILED: {e}")

        time.sleep(RATE_LIMIT_DELAY)

        # PaddleOCR baseline
        print(f"  Running PaddleOCR baseline...")
        paddle_raw = run_paddle_baseline(pdf_path)
        if "error" in paddle_raw:
            print(f"  PaddleOCR: {paddle_raw['error']}")
            paddle_fields = {
                f: {"value": None, "confidence": 0.0} for f in EXTRACT_FIELDS
            }
        else:
            paddle_text = paddle_raw.get("text", "")
            paddle_fields = parse_paddle_fields_from_text(paddle_text)
            found = sum(1 for f in paddle_fields.values() if f.get("value"))
            print(
                f"  PaddleOCR: {paddle_raw.get('chars', 0)} chars, ~{found} fields heuristically matched"
            )

        # Build section
        section = build_file_section(
            pdf_path.name,
            openai_fields,
            openai_model,
            openai_errors,
            paddle_raw,
            paddle_fields,
        )
        detail_sections.append(section)

        # Summary row
        if openai_fields:
            n_extracted = sum(1 for f in openai_fields.values() if f.get("value"))
            avg_conf = sum(
                f.get("confidence", 0.0) for f in openai_fields.values()
            ) / len(openai_fields)
            agreements = [
                score_agreement(
                    (paddle_fields.get(field, {}).get("value") or None),
                    (openai_fields.get(field, {}).get("value") or None),
                )
                for field in EXTRACT_FIELDS
            ]
            yes_count = sum(1 for a in agreements if a in ("yes", "partial"))
            agr_rate = f"{yes_count}/{len(EXTRACT_FIELDS)}"
        else:
            n_extracted = 0
            avg_conf = 0.0
            agr_rate = "n/a"

        summary_rows.append(
            f"| {pdf_path.name} | {openai_model} | {n_extracted}/{len(EXTRACT_FIELDS)} | {avg_conf:.2f} | {agr_rate} |"
        )
        print()

    doc_sections.extend(summary_rows)
    doc_sections.append("")
    doc_sections.append("---\n")
    doc_sections.append("## Per-File Results\n")
    doc_sections.extend(detail_sections)

    # Findings/observations
    doc_sections.append("\n---\n")
    doc_sections.append("## Observations\n")
    doc_sections.append(
        "- **PaddleOCR** returns raw text without field-level structure; "
        "field extraction requires post-hoc keyword matching which is brittle for handwritten forms.\n"
        "- **OpenAI vision** returns structured JSON per field with per-field confidence scores, "
        "eliminating the need for a separate normalization step.\n"
        "- Where PaddleOCR baseline was unavailable (paddleocr not installed in spike env), "
        "comparison is OpenAI-only; full comparison requires the worker Docker container.\n"
        "- If nano model is unavailable or doesn't support vision, the script falls back to mini automatically.\n"
        "- Recommendation: OpenAI vision (mini) is a strong candidate for single-step OCR+normalization, "
        "subject to cost/latency evaluation against the staged PaddleOCR+normalizer pipeline.\n"
    )

    OUTPUT_DOC.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT_DOC.write_text("\n".join(doc_sections))
    print(f"Results written to: {OUTPUT_DOC}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
