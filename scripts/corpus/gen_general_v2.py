"""
General Assistance Intake — v2 layout (county-government photocopy style).
Output: /tmp/corpus-v2/general/
"""
import sys, os, random
from datetime import date
sys.path.insert(0, os.path.dirname(__file__))
from people import PEOPLE, font_for_person, TENANT_IDS
from gen_utils_v2 import (
    register_fonts_v2, fill_font_for_person_v2, ink_color_v2, visit_dates,
    draw_v2_header, draw_section_header, draw_field_line, draw_field_block,
    draw_checkbox_v2, draw_signature_v2, draw_correction_v2,
    agency_name_v2, ensure_dir, PRINT_FONT, PRINT_BOLD, RULE_COLOR
)
from reportlab.lib.pagesizes import letter
from reportlab.lib.colors import black, HexColor
from reportlab.pdfgen import canvas

STAGING = "/tmp/corpus-v2/general"
TEMPLATE_ID = "general-assistance"

HOUSING_OPTS = ["Renting", "Owned", "With family", "Homeless", "Unknown"]
REASONS = [
    "Lost employment; unable to cover rent and utilities",
    "Medical emergency depleted savings — seeking bridge assistance",
    "Fled domestic violence situation, no housing",
    "Evicted 3 weeks ago, staying with cousin temporarily",
    "Single parent, childcare taking entire paycheck",
    "SSI application pending, no income currently",
    "Apartment fire — all belongings lost",
    "Recently released from county jail, rebuilding",
    "Widowed 2 months ago, adjusting to reduced income",
    "Seasonal work gap — starts new position in 3 weeks",
]

def forms_for_person(p):
    v = p["visits"]
    if v >= 10: return 3
    if v >= 4:  return 2
    return 1

def generate_form(person, visit_num, vdate, out_path):
    font = fill_font_for_person_v2(person["id"])
    color = ink_color_v2()
    do_correction = random.random() < 0.12
    blank_emergency = random.random() < 0.22

    housing = random.choices(HOUSING_OPTS, weights=[38, 18, 24, 14, 6])[0]
    household = random.randint(1, 7)
    income = random.choice([0, 0, 520, 780, 1050, 1300, 1600, 1950, 2300])
    reason = random.choice(REASONS)
    emg_name = f"{random.choice(['Rita','Gerald','Nora','Floyd','Iris','Clyde'])} {person['last']}"
    emg_phone = f"{person['phone'][:3]}-555-{random.randint(2000,9999)}"

    W, H = letter
    c = canvas.Canvas(out_path, pagesize=letter)

    # Subtle page tint
    c.setFillColor(HexColor("#fafaf8"))
    c.rect(0, 0, W, H, stroke=0, fill=1)

    draw_v2_header(c, agency_name_v2(person["tenant"]),
                   "Application for General Assistance",
                   f"GA-001 (Rev. 03/24)", vdate)

    y = H - 92
    LX = 18
    W_INNER = W - 36

    # Section 1: Applicant Information
    y = draw_section_header(c, "Section 1 — Applicant Information", y)

    dob = date.fromisoformat(person["dob"])
    draw_field_line(c, "Full Legal Name:", f"{person['first']} {person['last']}", LX, y, 120, 340, font, 11, color)
    draw_field_line(c, "Date of Birth:", dob.strftime("%m/%d/%Y"), LX + 470, y, 90, 60, font, 10, color)
    y -= 22

    draw_field_line(c, "Street Address:", f"{person['addr']}", LX, y, 100, 260, font, 10, color)
    draw_field_line(c, "City/State/Zip:", f"{person['city']}, {person['state']} {person['zip']}", LX + 370, y, 90, 145, font, 10, color)
    y -= 22

    draw_field_line(c, "Phone:", person['phone'], LX, y, 60, 130, font, 10, color)
    draw_field_line(c, "SSN (last 4):", f"XXX-XX-{person['ssn4']}", LX + 200, y, 90, 80, font, 10, color)
    draw_field_line(c, "Household Size:", str(household), LX + 390, y, 100, 60, font, 10, color)
    y -= 28

    # Section 2: Financial
    y = draw_section_header(c, "Section 2 — Financial & Housing", y)

    # Monthly income with possible correction
    if do_correction:
        wrong = income + random.choice([-250, 250, 400, -400])
        draw_field_line(c, "Monthly Gross Income:", "", LX, y, 140, 80, font, 10, color)
        draw_correction_v2(c, f"${wrong}", f"${income}", LX + 145, y, font, color)
    else:
        draw_field_line(c, "Monthly Gross Income:", f"${income}", LX, y, 140, 80, font, 10, color)

    y -= 22
    c.setFillColor(black)
    c.setFont(PRINT_FONT, 8)
    c.drawString(LX, y + 2, "Current Housing Status:")
    draw_checkbox_v2(c, LX + 140, y - 2, HOUSING_OPTS, housing, font, color)
    y -= 26

    # Section 3: Reason for Request
    y = draw_section_header(c, "Section 3 — Reason for Assistance", y)
    words = reason.split()
    line1, line2 = " ".join(words[:8]), " ".join(words[8:]) if len(words) > 8 else ""
    y = draw_field_block(c, "Describe your situation and what assistance you are requesting:",
                         [line1, line2, ""], LX, y, W_INNER, 17, font, 9, color)
    y -= 4

    # Section 4: Emergency Contact
    y = draw_section_header(c, "Section 4 — Emergency Contact", y)
    draw_field_line(c, "Name:", emg_name if not blank_emergency else "",
                    LX, y, 50, 200, font, 10, color)
    draw_field_line(c, "Phone:", emg_phone if not blank_emergency else "",
                    LX + 270, y, 50, 130, font, 10, color)
    draw_field_line(c, "Relationship:", random.choice(["Sibling","Parent","Spouse","Friend"]) if not blank_emergency else "",
                    LX + 460, y, 80, 56, font, 9, color)
    y -= 30

    # Section 5: Certification
    y = draw_section_header(c, "Section 5 — Applicant Certification", y)
    c.setFillColor(black)
    c.setFont(PRINT_FONT, 7)
    cert = ("I certify that the information provided is true and complete to the best of my knowledge. "
            "I understand that providing false information may result in denial or termination of benefits.")
    c.drawString(LX, y + 2, cert[:110])
    y -= 14

    c.setFont(PRINT_FONT, 8)
    c.setFillColor(black)
    c.drawString(LX, y + 2, "Applicant Signature:")
    draw_signature_v2(c, LX + 130, y, color, 180)
    draw_field_line(c, "Date:", vdate.strftime("%m/%d/%Y"), LX + 330, y, 40, 100, font, 10, color)
    draw_field_line(c, "Staff ID:", f"SW-{random.randint(100,999)}", LX + 490, y, 55, 50, font, 9, color)

    # Form footer
    y -= 24
    c.setStrokeColor(RULE_COLOR)
    c.setLineWidth(0.3)
    c.line(LX, y, W - LX, y)
    c.setFont(PRINT_FONT, 6)
    c.setFillColor(HexColor("#888888"))
    c.drawString(LX, y - 8, "AGENCY USE ONLY")
    c.drawRightString(W - LX, y - 8, f"Visit #{visit_num:02d}  |  Tenant: {person['tenant'].upper()}")

    c.save()

def main():
    register_fonts_v2()
    ensure_dir(STAGING)
    font_counts = {}
    errors = []
    total = 0

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
                import traceback; traceback.print_exc()

    print(f"\n=== General Assistance v2 ===")
    print(f"PDFs: {total}  |  Fonts: {font_counts}")
    if errors:
        for e in errors: print(f"  ERR: {e}")

if __name__ == "__main__":
    main()
