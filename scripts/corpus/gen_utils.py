"""Shared utilities for corpus PDF generation."""

import os
import random
from datetime import date, timedelta

from reportlab.lib.pagesizes import letter
from reportlab.lib.units import inch
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas


# Font configuration tries both macOS and Linux paths so generated PDFs work in CI.
FONT_DEFS = {
    "verdana": [
        ("/System/Library/Fonts/Supplemental/Verdana.ttf", False),
        ("/usr/share/fonts/truetype/msttcorefonts/Verdana.ttf", False),
        ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", False),
    ],
    "arial": [
        ("/System/Library/Fonts/Supplemental/Arial.ttf", False),
        ("/usr/share/fonts/truetype/msttcorefonts/Arial.ttf", False),
        ("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", False),
    ],
    "trebuchet": [
        ("/System/Library/Fonts/Supplemental/Trebuchet MS.ttf", False),
        ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", False),
    ],
    "comic_sans": [
        ("/System/Library/Fonts/Supplemental/Comic Sans MS.ttf", False),
        ("/usr/share/fonts/truetype/msttcorefonts/Comic_Sans_MS.ttf", False),
        ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", False),
    ],
    "bradley_hand": [
        ("/System/Library/Fonts/Supplemental/Bradley Hand Bold.ttf", False),
        ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", False),
    ],
    "marker_felt": [
        ("/System/Library/Fonts/MarkerFelt.ttc", True),
        ("/usr/share/fonts/truetype/msttcorefonts/Comic_Sans_MS.ttf", False),
    ],
    "noteworthy": [
        ("/System/Library/Fonts/Noteworthy.ttc", True),
        ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", False),
    ],
    "chalkboard": [
        ("/System/Library/Fonts/Supplemental/ChalkboardSE.ttc", True),
        ("/usr/share/fonts/truetype/freefont/FreeSans.ttf", False),
    ],
}

_registered = set()


def _resolve_font_path(candidates):
    """Return the first existing font file from candidate paths."""
    for path, _ in candidates:
        if os.path.exists(path):
            return path
    return None


def register_fonts():
    for name, candidates in FONT_DEFS.items():
        if name in _registered:
            continue

        font_path = _resolve_font_path(candidates)
        is_ttc = next((is_ttc for path, is_ttc in candidates if path == font_path), False)

        if not font_path:
            print(f"  Warning: could not register font {name}: no candidate font files found. Will use Helvetica fallback.")
            continue

        try:
            if is_ttc:
                pdfmetrics.registerFont(TTFont(name, font_path, subfontIndex=0))
            else:
                pdfmetrics.registerFont(TTFont(name, font_path))
            _registered.add(name)
        except Exception as e:
            print(f"  Warning: could not register font {name}: {e}")




def ink_color():
    """80% black, 20% dark blue."""
    if random.random() < 0.2:
        return (0.05, 0.05, 0.4)
    return (0, 0, 0)


def visit_dates(n_visits, end_date=date(2026, 3, 27)):
    """Space N visits evenly over 3 months with ±3 day jitter."""
    start = end_date - timedelta(days=90)
    if n_visits == 1:
        mid = start + timedelta(days=random.randint(0, 90))
        return [mid]

    dates = []
    for i in range(n_visits):
        base = start + timedelta(days=int(90 * i / (n_visits - 1)))
        jitter = random.randint(-3, 3)
        d = base + timedelta(days=jitter)
        d = max(start, min(end_date, d))
        dates.append(d)

    return sorted(set(dates))[:n_visits]


def draw_rotated_text(c, text, x, y, font_name, font_size, color):
    """Draw text with slight random rotation to simulate handwriting."""
    angle = random.uniform(-1.5, 1.5)
    r, g, b = color
    c.saveState()
    c.setFillColorRGB(r, g, b)
    c.translate(x, y)
    c.rotate(angle)
    try:
        c.setFont(font_name, font_size)
        c.drawString(0, 0, text)
    except Exception:
        c.setFont("Helvetica", font_size)
        c.drawString(0, 0, text)
    c.restoreState()


def draw_label(c, text, x, y, size=8):
    c.setFont("Helvetica-Bold", size)
    c.setFillColorRGB(0, 0, 0)
    c.drawString(x, y, text)


def draw_field_box(c, x, y, width, height=16):
    c.setStrokeColorRGB(0.3, 0.3, 0.3)
    c.setLineWidth(0.5)
    c.rect(x, y, width, height, stroke=1, fill=0)


def draw_checkbox_row(c, x, y, options, selected, font_name, color, box_size=10):
    """Draw a row of labeled checkboxes, marking selected with X."""
    cx = x
    for opt in options:
        c.setStrokeColorRGB(0.3, 0.3, 0.3)
        c.setLineWidth(0.5)
        c.rect(cx, y, box_size, box_size, stroke=1, fill=0)
        if opt == selected:
            draw_rotated_text(c, "X", cx + 1, y + 1, font_name, 9, color)

        c.setFont("Helvetica", 8)
        c.setFillColorRGB(0, 0, 0)
        c.drawString(cx + box_size + 2, y + 1, opt)
        cx += box_size + len(opt) * 5 + 14


def draw_signature_scrawl(c, x, y, color):
    """Draw a wavy bezier line to simulate a signature."""
    r, g, b = color
    c.setStrokeColorRGB(r, g, b)
    c.setLineWidth(0.8)
    p = c.beginPath()
    p.moveTo(x, y)
    p.curveTo(x + 20, y + 8, x + 35, y - 5, x + 55, y + 3)
    p.curveTo(x + 70, y + 10, x + 85, y - 3, x + 100, y)
    c.drawPath(p, stroke=1, fill=0)


def draw_strikethrough_correction(c, orig_text, corrected_text, x, y, font_name, font_size, color):
    """Draw original text with strikethrough, then corrected text above."""
    r, g, b = color
    c.setFont(font_name, font_size)
    c.setFillColorRGB(r, g, b)
    try:
        w = c.stringWidth(orig_text, font_name, font_size)
    except Exception:
        w = len(orig_text) * 6
    c.drawString(x, y, orig_text)

    c.setStrokeColorRGB(r, g, b)
    c.setLineWidth(0.8)
    c.line(x, y + font_size * 0.4, x + w, y + font_size * 0.4)

    # correction above
    draw_rotated_text(c, corrected_text, x, y + font_size + 2, font_name, font_size - 1, color)


def draw_marginal_note(c, text, x, y, font_name, color):
    r, g, b = color
    c.saveState()
    c.setFillColorRGB(r, g, b)
    try:
        c.setFont(font_name, 7)
    except Exception:
        c.setFont("Helvetica", 7)
    c.drawString(x, y, text)
    c.restoreState()


def agency_name(tenant):
    return "Sunrise Family Services" if tenant == "sunrise" else "Lakewood Community Services"


def ensure_dir(path):
    os.makedirs(path, exist_ok=True)
