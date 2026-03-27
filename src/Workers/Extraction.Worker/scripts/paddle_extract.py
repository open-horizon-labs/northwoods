#!/usr/bin/env python3
import argparse
import json
import os
import sys


def read_text_file(path: str) -> str:
    with open(path, 'r', encoding='utf-8', errors='ignore') as f:
        return f.read()


def read_pdf_text(path: str) -> str:
    try:
        from pypdf import PdfReader  # type: ignore
    except Exception:
        return ""

    try:
        reader = PdfReader(path)
        parts = []
        for page in reader.pages:
            parts.append(page.extract_text() or "")
        return "\n".join(parts)
    except Exception:
        return ""


def run_paddle_ocr(path: str) -> str:
    from paddleocr import PaddleOCR  # type: ignore

    ocr = PaddleOCR(use_angle_cls=True, lang='en', show_log=False)
    result = ocr.ocr(path, cls=True)

    lines = []
    for block in result or []:
        for item in block or []:
            if len(item) < 2:
                continue
            text_conf = item[1]
            if not text_conf:
                continue
            text = text_conf[0] if isinstance(text_conf, (list, tuple)) and len(text_conf) > 0 else ""
            if text:
                lines.append(text.strip())

    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--file', required=True)
    args = parser.parse_args()

    file_path = args.file
    if not os.path.exists(file_path):
        print(json.dumps({"error": f"file does not exist: {file_path}"}))
        return 2

    ext = os.path.splitext(file_path)[1].lower()

    text = ""
    if ext in {'.txt', '.md'}:
        text = read_text_file(file_path)
    elif ext == '.pdf':
        text = read_pdf_text(file_path)
        if not text.strip():
            try:
                text = run_paddle_ocr(file_path)
            except Exception as ex:
                print(json.dumps({"error": f"paddleocr failed on pdf: {str(ex)}"}))
                return 3
    else:
        try:
            text = run_paddle_ocr(file_path)
        except Exception as ex:
            print(json.dumps({"error": f"paddleocr failed: {str(ex)}"}))
            return 4

    payload = {
        "text": text,
        "chars": len(text),
        "lineCount": len([ln for ln in text.splitlines() if ln.strip()]),
    }
    print(json.dumps(payload))
    return 0


if __name__ == '__main__':
    sys.exit(main())
