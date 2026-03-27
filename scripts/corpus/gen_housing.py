"""
Generate Housing Stability Assessment PDFs.
Output: /tmp/corpus-staging/housing/
"""
import sys, os, random
from datetime import date
sys.path.insert(0, os.path.dirname(__file__))
from people import PEOPLE, font_for_person, TENANT_IDS
from gen_utils import (
    register_fonts, ink_color, visit_dates, draw_rotated_text,
    draw_label, draw_field_box, draw_checkbox_row, draw_signature_scrawl,
    draw_strikethrough_correction, draw_marginal_note, agency_name, ensure_dir
)
from reportlab.lib.pagesizes import letter
from reportlab.pdfgen import canvas

STAGING = "/tmp/corpus-staging/housing"
TEMPLATE_ID = "housing-stability"

HOUSING_TYPES = ["Apartment", "House", "Room rental", "Shelter", "Vehicle", "Other"]
INSTABILITY_REASONS = [
    "Rent increase, cannot afford new rate",
    "Job loss resulting in inability to pay rent",
    "Landlord refusing to renew lease",
    "Overcrowded living situation, family conflict",
    "Property condemned by city, forced to vacate",
    "Escaping unsafe living conditions",
    "Recent release from incarceration, no established housing",
    "Aging out of foster care system",
]

# People who get housing assessments — all, but prioritize multi-visit
HOUSING_PEOPLE_IDS = {
    "P014": 2, "P015": 2, "P016": 2,
    "P017": 3, "P018": 2, "P019": 3,
    "P033": 2, "P034": 2, "P035": 2,
    "P036": 1, "P037": 3, "P038": 2, "P039": 3,
}

def forms_for_person(person):
    return HOUSING_PEOPLE_IDS.get(person["id"], 1 if person["visits"] >= 1 else 0)

def generate_form(person, visit_num, visit_date, out_path):
    font_name, _ = font_for_person(person["id"])
    color = ink_color()
    do_correction = random.random() < 0.10
    do_marginal = random.random() < 0.10
    blank_landlord = random.random() < 0.15

    unstable = person["visits"] >= 3
    housing_type = random.choices(
        HOUSING_TYPES,
        weights=[35, 15, 20, 20, 5, 5]
    )[0]
    rent_options = [0, 550, 750, 900, 1100, 1350]
    rent = random.choice(rent_options)
    months_behind = random.randint(1, 4) if unstable else random.randint(0, 1)
    eviction = "Yes" if (unstable and random.random() < 0.40) else "No"
    landlord_name = f"{random.choice(['Mike','Sandra','Bob','Yolanda','Jim','Priya'])} {random.choice(['Smith','Johnson','Lee','Garcia','Brown','Davis'])}"
    landlord_phone = f"217-555-{random.randint(2000,9999)}"
    months_at_addr = random.randint(1, 36)
    prior_addr = "Same" if random.random() < 0.60 else f"{random.randint(100,999)} {random.choice(['Oak','Elm','Pine','Main'])} St, Springfield, IL"
    reason = random.choice(INSTABILITY_REASONS)
    section8 = "Yes" if random.random() < 0.25 else "No"

    W, H = letter
    c = canvas.Canvas(out_path, pagesize=letter)

    c.setFont("Helvetica-Bold", 13)
    c.setFillColorRGB(0, 0, 0)
    c.drawCentredString(W / 2, H - 50, agency_name(person["tenant"]))
    c.setFont("Helvetica-Bold", 11)
    c.drawCentredString(W / 2, H - 66, "HOUSING STABILITY ASSESSMENT")
    c.setFont("Helvetica", 8)
    c.drawCentredString(W / 2, H - 78, f"Form HS-002  |  Date: {visit_date.strftime('%m/%d/%Y')}")
    c.setLineWidth(1)
    c.line(40, H - 84, W - 40, H - 84)

    y = H - 110
    lx = 45
    rx = W / 2 + 10
    fw_full = W - 90
    fw_half = W / 2 - 55

    # Name | DOB
    draw_label(c, "Applicant Name", lx, y + 18)
    draw_field_box(c, lx, y, fw_half, 18)
    draw_rotated_text(c, f"{person['first']} {person['last']}", lx + 4, y + 4, font_name, 11, color)

    draw_label(c, "Date of Birth", rx, y + 18)
    draw_field_box(c, rx, y, fw_half - 20, 18)
    dob = date.fromisoformat(person["dob"])
    draw_rotated_text(c, dob.strftime("%m/%d/%Y"), rx + 4, y + 4, font_name, 11, color)
    y -= 34

    # Housing type checkboxes
    draw_label(c, "Current Housing Type:", lx, y + 4)
    draw_checkbox_row(c, lx + 125, y - 2, HOUSING_TYPES, housing_type, font_name, color)
    y -= 30

    # Rent | Months behind
    draw_label(c, "Monthly Rent/Mortgage ($)", lx, y + 18)
    draw_field_box(c, lx, y, fw_half, 18)
    if do_correction:
        wrong = rent + random.choice([-100, 100, 150, -150])
        draw_strikethrough_correction(c, f"${wrong}", f"${rent}", lx + 4, y + 4, font_name, 11, color)
    else:
        draw_rotated_text(c, f"${rent}", lx + 4, y + 4, font_name, 11, color)

    draw_label(c, "Months Behind on Rent", rx, y + 18)
    draw_field_box(c, rx, y, fw_half - 20, 18)
    draw_rotated_text(c, str(months_behind), rx + 4, y + 4, font_name, 11, color)
    y -= 34

    # Eviction | Section 8
    draw_label(c, "Eviction Notice Received?", lx, y + 4)
    draw_checkbox_row(c, lx + 155, y - 2, ["Yes", "No"], eviction, font_name, color)
    draw_label(c, "Has Section 8 / Voucher?", rx, y + 4)
    draw_checkbox_row(c, rx + 150, y - 2, ["Yes", "No"], section8, font_name, color)
    if eviction == "Yes" and random.random() < 0.20 and do_marginal:
        draw_marginal_note(c, "court date 2/14", W - 80, y, font_name, color)
    y -= 30

    # Landlord name | phone
    draw_label(c, "Landlord Name", lx, y + 18)
    draw_field_box(c, lx, y, fw_half, 18)
    if not blank_landlord:
        draw_rotated_text(c, landlord_name, lx + 4, y + 4, font_name, 11, color)

    draw_label(c, "Landlord Phone", rx, y + 18)
    draw_field_box(c, rx, y, fw_half - 20, 18)
    if not blank_landlord:
        draw_rotated_text(c, landlord_phone, rx + 4, y + 4, font_name, 11, color)
    y -= 34

    # Time at address | Prior address
    draw_label(c, "How long at current address?", lx, y + 18)
    draw_field_box(c, lx, y, fw_half, 18)
    draw_rotated_text(c, f"{months_at_addr} months", lx + 4, y + 4, font_name, 11, color)

    draw_label(c, "Prior address (last 12 mo)", rx, y + 18)
    draw_field_box(c, rx, y, fw_half - 20, 18)
    draw_rotated_text(c, prior_addr[:35], rx + 4, y + 4, font_name, 10, color)
    y -= 34

    # Reason for instability (2 lines)
    draw_label(c, "Reason for Instability", lx, y + 18)
    for li in range(2):
        draw_field_box(c, lx, y - li * 22, fw_full, 18)
        if li == 0:
            draw_rotated_text(c, reason, lx + 4, y + 4, font_name, 10, color)
    y -= 58

    # Worker signature
    draw_label(c, "Worker Signature", lx, y + 18)
    c.setStrokeColorRGB(0.3, 0.3, 0.3)
    c.setLineWidth(0.5)
    c.line(lx, y, lx + 200, y)
    draw_signature_scrawl(c, lx + 5, y + 2, color)

    draw_label(c, "Date", lx + 220, y + 18)
    c.line(lx + 240, y, lx + 340, y)
    draw_rotated_text(c, visit_date.strftime("%m/%d/%Y"), lx + 244, y + 4, font_name, 10, color)

    c.save()

def main():
    register_fonts()
    ensure_dir(STAGING)
    font_counts = {}
    errors = []
    total = 0

    for person in PEOPLE:
        n_forms = forms_for_person(person)
        if n_forms == 0:
            continue
        dates = visit_dates(person["visits"])
        form_dates = dates[:n_forms]

        for visit_num, vdate in enumerate(form_dates, 1):
            fname = f"{person['id']}_{visit_num:02d}_{TEMPLATE_ID}.pdf"
            out_path = os.path.join(STAGING, fname)
            font_name, _ = font_for_person(person["id"])
            font_counts[font_name] = font_counts.get(font_name, 0) + 1
            try:
                generate_form(person, visit_num, vdate, out_path)
                total += 1
            except Exception as e:
                errors.append(f"{fname}: {e}")

    print(f"\n=== Housing Stability Assessment ===")
    print(f"PDFs generated: {total}")
    print(f"Font distribution: {font_counts}")
    if errors:
        print(f"Errors ({len(errors)}):")
        for e in errors:
            print(f"  {e}")
    else:
        print("No errors.")

if __name__ == "__main__":
    main()
