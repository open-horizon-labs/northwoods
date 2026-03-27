"""
Housing Stability Assessment — v2 layout.
Output: /tmp/corpus-v2/housing/
"""
import sys, os, random
from datetime import date
sys.path.insert(0, os.path.dirname(__file__))
from people import PEOPLE, TENANT_IDS
from gen_utils_v2 import (
    register_fonts_v2, fill_font_for_person_v2, ink_color_v2, visit_dates,
    draw_v2_header, draw_section_header, draw_field_line, draw_field_block,
    draw_checkbox_v2, draw_signature_v2, draw_correction_v2,
    agency_name_v2, ensure_dir, PRINT_FONT, RULE_COLOR
)
from reportlab.lib.pagesizes import letter
from reportlab.lib.colors import black, HexColor
from reportlab.pdfgen import canvas

STAGING = "/tmp/corpus-v2/housing"
TEMPLATE_ID = "housing-stability"

HOUSING_TYPES = ["Apartment", "House", "Room/Board", "Shelter", "Vehicle/Tent", "Other"]
INSTABILITY = [
    "Rent increase, no longer affordable",
    "Job loss, 2 months behind",
    "Landlord refuses to renew, no reason given",
    "Unsafe conditions — mold, no heat",
    "Overcrowding with relatives, asked to leave",
    "Escaped unsafe relationship",
    "Released from incarceration, no housing",
    "Fire/flood damage to unit",
]

HOUSING_PEOPLE_IDS = {
    "P014": 2, "P015": 2, "P016": 2,
    "P017": 3, "P018": 2, "P019": 3,
    "P033": 2, "P034": 2, "P035": 2,
    "P036": 1, "P037": 3, "P038": 2, "P039": 3,
}

def forms_for_person(p):
    return HOUSING_PEOPLE_IDS.get(p["id"], 1)

def generate_form(person, visit_num, vdate, out_path):
    font = fill_font_for_person_v2(person["id"])
    color = ink_color_v2()
    unstable = person["visits"] >= 3
    do_correction = random.random() < 0.10
    blank_landlord = random.random() < 0.18

    htype = random.choices(HOUSING_TYPES, weights=[33, 14, 22, 18, 7, 6])[0]
    rent = random.choice([0, 0, 500, 700, 850, 1000, 1250, 1400])
    behind = random.randint(1, 5) if unstable else random.randint(0, 1)
    eviction = "Yes" if unstable and random.random() < 0.40 else "No"
    section8 = "Yes" if random.random() < 0.25 else "No"
    ll_name = f"{random.choice(['Harold','Donna','Ray','Connie','Pete'])} {random.choice(['Walsh','Murray','Diaz','Kim','Brown'])}"
    ll_phone = f"217-555-{random.randint(2000,9999)}"
    months_there = random.randint(1, 42)
    prior = "Same address" if random.random() < 0.55 else f"{random.randint(100,900)} {random.choice(['Oak','River','Park','Main'])} St"
    reason = random.choice(INSTABILITY)

    W, H = letter
    c = canvas.Canvas(out_path, pagesize=letter)
    c.setFillColor(HexColor("#fafaf8"))
    c.rect(0, 0, W, H, stroke=0, fill=1)

    draw_v2_header(c, agency_name_v2(person["tenant"]),
                   "Housing Stability Assessment", f"HS-002 (Rev. 01/24)", vdate)

    y = H - 92
    LX = 18
    W_INNER = W - 36

    y = draw_section_header(c, "Section 1 — Client Identification", y)
    dob = date.fromisoformat(person["dob"])
    draw_field_line(c, "Client Name:", f"{person['first']} {person['last']}", LX, y, 90, 280, font, 11, color)
    draw_field_line(c, "DOB:", dob.strftime("%m/%d/%Y"), LX + 390, y, 40, 100, font, 10, color)
    y -= 22
    draw_field_line(c, "Address:", f"{person['addr']}, {person['city']}", LX, y, 70, 420, font, 10, color)
    y -= 26

    y = draw_section_header(c, "Section 2 — Current Housing", y)
    c.setFillColor(black); c.setFont(PRINT_FONT, 8)
    c.drawString(LX, y + 2, "Type of Housing:")
    draw_checkbox_v2(c, LX + 110, y - 2, HOUSING_TYPES, htype, font, color)
    y -= 26

    if do_correction:
        draw_field_line(c, "Monthly Rent/Mortgage:", "", LX, y, 150, 70, font, 10, color)
        draw_correction_v2(c, f"${rent + random.choice([-100,150,-150,100])}", f"${rent}", LX + 156, y, font, color)
    else:
        draw_field_line(c, "Monthly Rent/Mortgage:", f"${rent}", LX, y, 150, 70, font, 10, color)
    draw_field_line(c, "Months Behind:", str(behind), LX + 250, y, 100, 40, font, 10, color)
    draw_field_line(c, "Time at Address:", f"{months_there} mo.", LX + 420, y, 100, 60, font, 10, color)
    y -= 22

    c.setFillColor(black); c.setFont(PRINT_FONT, 8)
    c.drawString(LX, y + 2, "Eviction notice received?")
    draw_checkbox_v2(c, LX + 160, y - 2, ["Yes", "No"], eviction, font, color)
    c.drawString(LX + 280, y + 2, "Has Section 8 / voucher?")
    draw_checkbox_v2(c, LX + 440, y - 2, ["Yes", "No"], section8, font, color)
    y -= 26

    draw_field_line(c, "Landlord Name:", ll_name if not blank_landlord else "",
                    LX, y, 110, 200, font, 10, color)
    draw_field_line(c, "Landlord Phone:", ll_phone if not blank_landlord else "",
                    LX + 330, y, 110, 130, font, 10, color)
    y -= 22
    draw_field_line(c, "Prior address (12 mo):", prior, LX, y, 140, 300, font, 10, color)
    y -= 26

    y = draw_section_header(c, "Section 3 — Reason for Instability", y)
    words = reason.split()
    l1 = " ".join(words[:9]); l2 = " ".join(words[9:]) if len(words) > 9 else ""
    y = draw_field_block(c, "Describe circumstances leading to housing instability:",
                         [l1, l2, ""], LX, y, W_INNER, 17, font, 9, color)
    y -= 4

    y = draw_section_header(c, "Section 4 — Certification", y)
    c.setFont(PRINT_FONT, 7); c.setFillColor(black)
    c.drawString(LX, y + 2, "I certify the above information is accurate. I understand false statements may result in denial of services.")
    y -= 18
    c.setFont(PRINT_FONT, 8)
    c.drawString(LX, y + 2, "Client Signature:")
    draw_signature_v2(c, LX + 120, y, color, 180)
    draw_field_line(c, "Date:", vdate.strftime("%m/%d/%Y"), LX + 320, y, 40, 100, font, 10, color)
    draw_field_line(c, "Worker:", f"SW-{random.randint(100,999)}", LX + 480, y, 50, 56, font, 9, color)

    y -= 22
    c.setStrokeColor(RULE_COLOR); c.setLineWidth(0.3)
    c.line(LX, y, W - LX, y)
    c.setFont(PRINT_FONT, 6); c.setFillColor(HexColor("#888888"))
    c.drawString(LX, y - 8, "AGENCY USE ONLY")
    c.drawRightString(W - LX, y - 8, f"Visit #{visit_num:02d}  |  {person['tenant'].upper()}")

    c.save()

def main():
    register_fonts_v2()
    ensure_dir(STAGING)
    font_counts = {}; errors = []; total = 0

    for person in PEOPLE:
        n = forms_for_person(person)
        dates = visit_dates(person["visits"])
        for vn, vdate in enumerate(dates[:n], 1):
            fname = f"{person['id']}_{vn:02d}_v2_{TEMPLATE_ID}.pdf"
            out = os.path.join(STAGING, fname)
            fn = fill_font_for_person_v2(person["id"])
            font_counts[fn] = font_counts.get(fn, 0) + 1
            try:
                generate_form(person, vn, vdate, out)
                total += 1
            except Exception as e:
                errors.append(f"{fname}: {e}")

    print(f"\n=== Housing Stability v2 ===")
    print(f"PDFs: {total}  |  Fonts: {font_counts}")
    if errors:
        for e in errors: print(f"  ERR: {e}")

if __name__ == "__main__":
    main()
