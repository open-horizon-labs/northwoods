"""
Alternate visual style for corpus generation — v2.
Design: county-government photocopy aesthetic.
- Two-tone header block (dark bar + agency name reversed out)
- Shaded left sidebar for section labels
- Courier New for all printed structure (typewriter feel)
- Serif/cursive fonts for handwritten fill
- ALL-CAPS bold section headers
- Tighter field rows with bottom-border-only lines (not full boxes)
- Form number in upper-right corner like a government form
- Slightly heavier paper feel via gray page background tint
"""
import os
import random
from datetime import date, timedelta
from reportlab.lib.pagesizes import letter
from reportlab.lib.colors import HexColor, black, white
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas

FILL_FONT_DEFS = {
    "georgia":      "/System/Library/Fonts/Supplemental/Georgia.ttf",
    "georgia_bold": "/System/Library/Fonts/Supplemental/Georgia Bold.ttf",
    "baskerville":  "/System/Library/Fonts/Supplemental/Baskerville.ttc",
    "times":        "/System/Library/Fonts/Supplemental/Times New Roman.ttf",
    "times_italic": "/System/Library/Fonts/Supplemental/Times New Roman Italic.ttf",
    "zapfino":      "/System/Library/Fonts/Supplemental/Zapfino.ttf",
    "courier_new":  "/System/Library/Fonts/Supplemental/Courier New.ttf",
    "courier_bold": "/System/Library/Fonts/Supplemental/Courier New Bold.ttf",
}

PRINT_FONT = "Courier"   # reportlab built-in, no registration needed
PRINT_BOLD = "Courier-Bold"
HEADER_FONT = "Helvetica-Bold"

_registered = set()

def register_fonts_v2():
    for name, path in FILL_FONT_DEFS.items():
        if name in _registered:
            continue
        try:
            is_ttc = path.endswith(".ttc")
            if is_ttc:
                pdfmetrics.registerFont(TTFont(name, path, subfontIndex=0))
            else:
                pdfmetrics.registerFont(TTFont(name, path))
            _registered.add(name)
        except Exception as e:
            print(f"  Warning: {name}: {e}")

def fill_font_for_person_v2(person_id):
    """Assign a fill font — weighted toward serif/cursive, away from sans."""
    import hashlib
    # Exclude zapfino for most people — too hard to read, use for 1 in 8
    pool = ["georgia", "georgia_bold", "baskerville", "times",
            "times_italic", "georgia", "times", "baskerville"]  # repeated to weight
    idx = int(hashlib.md5(("v2" + person_id).encode()).hexdigest(), 16) % len(pool)
    return pool[idx]

def ink_color_v2():
    """60% near-black, 20% faded black (like old ballpoint), 20% dark blue-black."""
    r = random.random()
    if r < 0.6:
        return (0.05, 0.05, 0.05)
    elif r < 0.8:
        return (0.25, 0.22, 0.20)   # faded/aged ink
    else:
        return (0.0, 0.05, 0.25)    # dark navy ballpoint

def visit_dates(n_visits, end_date=date(2026, 3, 27)):
    start = end_date - timedelta(days=90)
    if n_visits == 1:
        mid = start + timedelta(days=random.randint(10, 80))
        return [mid]
    dates = []
    for i in range(n_visits):
        base = start + timedelta(days=int(90 * i / (n_visits - 1)))
        jitter = random.randint(-3, 3)
        d = max(start, min(end_date, base + timedelta(days=jitter)))
        dates.append(d)
    return sorted(set(dates))[:n_visits]

# ── Header ────────────────────────────────────────────────────────────────────

DARK_BAR = HexColor("#1a2433")      # near-black navy
LIGHT_BAR = HexColor("#3d5a7a")     # medium blue for sub-bar
SHADE_BG  = HexColor("#e8e8e8")     # field shading
RULE_COLOR = HexColor("#666666")

def draw_v2_header(c, agency, form_title, form_number, visit_date):
    W, H = letter
    # Dark header bar
    c.setFillColor(DARK_BAR)
    c.rect(0, H - 54, W, 54, stroke=0, fill=1)
    # Agency name reversed out
    c.setFillColor(white)
    c.setFont("Helvetica-Bold", 14)
    c.drawString(18, H - 26, agency.upper())
    # Form number top-right
    c.setFont(PRINT_FONT, 8)
    c.drawRightString(W - 12, H - 14, form_number)
    c.drawRightString(W - 12, H - 26, f"Date: {visit_date.strftime('%m/%d/%Y')}")
    # Light sub-bar with form title
    c.setFillColor(LIGHT_BAR)
    c.rect(0, H - 74, W, 20, stroke=0, fill=1)
    c.setFillColor(white)
    c.setFont("Helvetica-Bold", 10)
    c.drawCentredString(W / 2, H - 68, form_title.upper())

def draw_section_header(c, text, y, width=None):
    """ALL-CAPS section label with full-width rule below."""
    W, H = letter
    w = width or W - 36
    c.setFillColor(SHADE_BG)
    c.rect(18, y - 2, w, 14, stroke=0, fill=1)
    c.setFillColor(black)
    c.setFont(PRINT_BOLD, 8)
    c.drawString(22, y + 1, text.upper())
    # bottom rule
    c.setStrokeColor(RULE_COLOR)
    c.setLineWidth(0.4)
    c.line(18, y - 2, 18 + w, y - 2)
    return y - 18

def draw_field_line(c, label, value, x, y, label_width=140, field_width=220, font_name="georgia", font_size=10, color=None):
    """Label on left in Courier, value on right as underlined fill."""
    if color is None:
        color = ink_color_v2()
    # Label
    c.setFillColor(black)
    c.setFont(PRINT_FONT, 8)
    c.drawString(x, y + 1, label)
    # Underline for field
    c.setStrokeColor(RULE_COLOR)
    c.setLineWidth(0.5)
    field_x = x + label_width
    c.line(field_x, y, field_x + field_width, y)
    # Fill value with slight rotation
    if value:
        angle = random.uniform(-0.8, 0.8)
        r, g, b = color
        c.saveState()
        c.setFillColorRGB(r, g, b)
        c.translate(field_x + 3, y + 2)
        c.rotate(angle)
        try:
            c.setFont(font_name, font_size)
        except Exception:
            c.setFont("Times-Roman", font_size)
        c.drawString(0, 0, value)
        c.restoreState()

def draw_field_block(c, label, lines_of_text, x, y, field_width, line_height=18, font_name="georgia", font_size=9, color=None):
    """Multi-line field with label above and ruled lines below."""
    if color is None:
        color = ink_color_v2()
    c.setFillColor(black)
    c.setFont(PRINT_BOLD, 7)
    c.drawString(x, y + 3, label.upper())
    y -= 4
    c.setStrokeColor(RULE_COLOR)
    c.setLineWidth(0.4)
    for i, text in enumerate(lines_of_text):
        ly = y - i * line_height
        c.line(x, ly, x + field_width, ly)
        if text:
            angle = random.uniform(-0.6, 0.6)
            r, g, b = color
            c.saveState()
            c.setFillColorRGB(r, g, b)
            c.translate(x + 2, ly + 3)
            c.rotate(angle)
            try:
                c.setFont(font_name, font_size)
            except Exception:
                c.setFont("Times-Roman", font_size)
            c.drawString(0, 0, text[:90])
            c.restoreState()
    return y - len(lines_of_text) * line_height - 8

def draw_checkbox_v2(c, x, y, options, selected, font_name, color):
    """Round-ish checkboxes with label, filled circle for selected."""
    cx = x
    r, g, b = color
    for opt in options:
        # outer circle
        c.setStrokeColor(RULE_COLOR)
        c.setLineWidth(0.6)
        c.circle(cx + 5, y + 4, 5, stroke=1, fill=0)
        if opt == selected:
            # filled dot
            c.setFillColorRGB(r, g, b)
            c.circle(cx + 5, y + 4, 2.5, stroke=0, fill=1)
        c.setFillColor(black)
        c.setFont(PRINT_FONT, 7.5)
        c.drawString(cx + 12, y + 1, opt)
        cx += 12 + len(opt) * 4.5 + 10

def draw_signature_v2(c, x, y, color, width=160):
    """Loopier signature scrawl."""
    r, g, b = color
    c.setStrokeColorRGB(r, g, b)
    c.setLineWidth(0.9)
    p = c.beginPath()
    p.moveTo(x, y)
    p.curveTo(x + 15, y + 10, x + 30, y - 6, x + 50, y + 4)
    p.curveTo(x + 65, y + 12, x + 85, y - 2, x + 110, y + 6)
    p.curveTo(x + 130, y + 12, x + 150, y - 4, x + width, y)
    c.drawPath(p, stroke=1, fill=0)
    # underline
    c.setStrokeColor(RULE_COLOR)
    c.setLineWidth(0.3)
    c.line(x, y - 2, x + width, y - 2)

def draw_correction_v2(c, orig, corrected, x, y, font_name, color):
    """Strikethrough orig, write corrected to the right with caret."""
    r, g, b = color
    c.saveState()
    c.setFillColorRGB(r, g, b)
    try:
        c.setFont(font_name, 9)
        w = c.stringWidth(orig, font_name, 9)
    except Exception:
        c.setFont("Times-Roman", 9)
        w = len(orig) * 5.5
    c.drawString(x, y, orig)
    c.setStrokeColorRGB(r, g, b)
    c.setLineWidth(0.7)
    c.line(x, y + 4, x + w, y + 4)
    # caret + correction
    c.setFont(PRINT_FONT, 7)
    c.setFillColor(black)
    c.drawString(x + w + 4, y, "^")
    try:
        c.setFont(font_name, 9)
    except Exception:
        c.setFont("Times-Roman", 9)
    c.setFillColorRGB(r, g, b)
    c.drawString(x + w + 10, y, corrected)
    c.restoreState()

def agency_name_v2(tenant):
    if tenant == "sunrise":
        return "Sunrise County Dept. of Social Services"
    return "Lakewood County Human Services Division"

def ensure_dir(path):
    os.makedirs(path, exist_ok=True)
