"""
Generate General Assistance Intake form PDFs.
Output: /tmp/corpus-staging/general/
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

STAGING = "/tmp/corpus-staging/general"
TEMPLATE_ID = "general-assistance"

HOUSING_OPTS = ["Renting", "Owned", "With family", "Homeless"]
REASONS = [
    "Lost employment last month, unable to pay rent",
    "Medical bills exhausted savings, need utility assistance",
    "Fleeing domestic situation, need temporary housing support",
    "Recently evicted, staying with relatives temporarily",
    "Single parent, childcare costs leaving nothing for food",
    "Disability prevents work, SSI application pending",
    "Fire damaged apartment, displaced with two children",
    "Recently released, need help getting back on feet",
    "Spouse passed away, adjusting to single income",
    "Underpaid seasonal work ended, gap before new job starts",
]

def forms_for_person(person):
    v = person["visits"]
    if v >= 10:
        return 3
    if v >= 4:
        return 2
    return 1

def generate_form(person, visit_num, visit_date, out_path):
    font_name, _ = font_for_person(person["id"])
    color = ink_color()
    do_correction = random.random() < 0.10
    do_marginal = random.random() < 0.10
    blank_emergency = random.random() < 0.20

    housing = random.choices(HOUSING_OPTS, weights=[40, 20, 25, 15])[0]
    household = random.randint(1, 6)
    incomes = [0, 650, 980, 1200, 1450, 1800, 2100, 2400]
    income = random.choice(incomes)
    reason = random.choice(REASONS)
    emg_name = f"{random.choice(['Maria','James','Denise','Tony','Ruth','Carl'])} {person['last']}"
    emg_phone = f"{person['phone'][:3]}-555-{random.randint(1000,9999)}"

    W, H = letter
    c = canvas.Canvas(out_path, pagesize=letter)
    c.setFont("Helvetica-Bold", 13)
    c.setFillColorRGB(0, 0, 0)
    c.drawCentredString(W / 2, H - 50, agency_name(person["tenant"]))
    c.setFont("Helvetica-Bold", 11)
    c.drawCentredString(W / 2, H - 66, "GENERAL ASSISTANCE INTAKE")
    c.setFont("Helvetica", 8)
    c.drawCentredString(W / 2, H - 78, f"Form GA-001  |  Date: {visit_date.strftime('%m/%d/%Y')}")
    c.setStrokeColorRGB(0, 0, 0)
    c.setLineWidth(1)
    c.line(40, H - 84, W - 40, H - 84)

    y = H - 110
    lx = 45
    rx = W / 2 + 10
    fw_full = W - 90
    fw_half = W / 2 - 55

    # Row 1: Name | DOB
    draw_label(c, "Applicant Name", lx, y + 18)
    draw_field_box(c, lx, y, fw_half, 18)
    draw_rotated_text(c, f"{person['first']} {person['last']}", lx + 4, y + 4, font_name, 11, color)

    draw_label(c, "Date of Birth", rx, y + 18)
    draw_field_box(c, rx, y, fw_half - 20, 18)
    dob = date.fromisoformat(person["dob"])
    draw_rotated_text(c, dob.strftime("%m/%d/%Y"), rx + 4, y + 4, font_name, 11, color)
    y -= 34

    # Row 2: SSN last 4 | Phone
    draw_label(c, "Social Security # (last 4)", lx, y + 18)
    draw_field_box(c, lx, y, fw_half, 18)
    draw_rotated_text(c, f"XXX-XX-{person['ssn4']}", lx + 4, y + 4, font_name, 11, color)

    draw_label(c, "Phone Number", rx, y + 18)
    draw_field_box(c, rx, y, fw_half - 20, 18)
    draw_rotated_text(c, person["phone"], rx + 4, y + 4, font_name, 11, color)
    y -= 34

    # Row 3: Address (full width)
    draw_label(c, "Address", lx, y + 18)
    draw_field_box(c, lx, y, fw_full, 18)
    draw_rotated_text(c, f"{person['addr']}, {person['city']}, {person['state']} {person['zip']}", lx + 4, y + 4, font_name, 11, color)
    y -= 34

    # Row 4: Household Size | Monthly Income
    draw_label(c, "Household Size", lx, y + 18)
    draw_field_box(c, lx, y, fw_half, 18)
    draw_rotated_text(c, str(household), lx + 4, y + 4, font_name, 11, color)

    draw_label(c, "Monthly Gross Income ($)", rx, y + 18)
    draw_field_box(c, rx, y, fw_half - 20, 18)
    if do_correction:
        wrong_income = income + random.choice([-200, 200, 300, -300])
        draw_strikethrough_correction(c, f"${wrong_income}", f"${income}", rx + 4, y + 4, font_name, 11, color)
    else:
        draw_rotated_text(c, f"${income}", rx + 4, y + 4, font_name, 11, color)
    y -= 36

    # Housing status checkboxes
    draw_label(c, "Current Housing Status:", lx, y + 4)
    draw_checkbox_row(c, lx + 130, y - 2, HOUSING_OPTS, housing, font_name, color)
    y -= 30

    # Reason for assistance (3 lines)
    draw_label(c, "Reason for Assistance", lx, y + 18)
    for line_i in range(3):
        draw_field_box(c, lx, y - line_i * 22, fw_full, 18)
        if line_i == 0:
            draw_rotated_text(c, reason, lx + 4, y - line_i * 22 + 4, font_name, 10, color)
    y -= 80

    # Emergency contact
    draw_label(c, "Emergency Contact Name", lx, y + 18)
    draw_field_box(c, lx, y, fw_half, 18)
    if not blank_emergency:
        draw_rotated_text(c, emg_name, lx + 4, y + 4, font_name, 11, color)

    draw_label(c, "Emergency Contact Phone", rx, y + 18)
    draw_field_box(c, rx, y, fw_half - 20, 18)
    if not blank_emergency:
        draw_rotated_text(c, emg_phone, rx + 4, y + 4, font_name, 11, color)
    y -= 50

    # Signature
    draw_label(c, "Applicant Signature", lx, y + 18)
    c.setStrokeColorRGB(0.3, 0.3, 0.3)
    c.setLineWidth(0.5)
    c.line(lx, y, lx + 200, y)
    draw_signature_scrawl(c, lx + 5, y + 2, color)

    draw_label(c, "Date", lx + 220, y + 18)
    c.line(lx + 240, y, lx + 340, y)
    draw_rotated_text(c, visit_date.strftime("%m/%d/%Y"), lx + 244, y + 4, font_name, 10, color)

    if do_marginal:
        draw_marginal_note(c, random.choice(["see attached", "updated 3/15", "call back"]), W - 80, H - 150, font_name, color)

    c.save()

def main():
    register_fonts()
    ensure_dir(STAGING)
    font_counts = {}
    errors = []
    total = 0

    for person in PEOPLE:
        n_forms = forms_for_person(person)
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

    print(f"\n=== General Assistance Intake ===")
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
