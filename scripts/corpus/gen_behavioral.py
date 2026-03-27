"""
Generate Behavioral Health Intake PDFs.
Output: /tmp/corpus-staging/behavioral/
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

STAGING = "/tmp/corpus-staging/behavioral"
TEMPLATE_ID = "behavioral-health"

SUBSTANCE_OPTS = ["Alcohol", "Cannabis", "Opioids", "Other", "None"]
CONCERNS = [
    "Client reports persistent anxiety related to housing instability and financial stress",
    "Difficulty sleeping, intrusive thoughts following recent job loss",
    "Feeling overwhelmed, reports support system is limited",
    "Grief following loss of family member, difficulty functioning at work",
    "Client self-referred, reports depressed mood for past 3 weeks",
    "Panic attacks increasing in frequency, triggered by financial stress",
    "Client reports relationship conflict, children affected by tension at home",
    "Recent hospitalization for overdose, seeking outpatient support",
    "Client referred by case manager following housing crisis",
]
PRIOR_TREATMENT = [
    "County mental health center, 2022",
    "Private therapist, stopped due to cost",
    "Inpatient, 2019, 10 days",
    "None",
    "School counselor in adolescence",
]
MEDICATIONS = ["None", "Sertraline 50mg", "Trazodone 100mg QHS",
               "Buspirone 15mg BID", "Gabapentin 300mg", "Fluoxetine 20mg"]
CLINICIANS = ["J. Rivera, LCSW", "M. Osei, LMFT", "T. Chen, LPC", "B. Santos, MSW"]

# People assignment
BEHAVIORAL_PEOPLE = {
    "P019": 3, "P039": 3,  # frequent flyers
    "P015": 2, "P016": 2, "P017": 2, "P018": 2,
    "P034": 2, "P035": 2, "P037": 2, "P038": 2,
    "P005": 1, "P006": 1, "P008": 1,
    "P022": 1, "P026": 1, "P028": 1,
}

def generate_form(person, visit_num, visit_date, out_path, is_crisis=False):
    font_name, _ = font_for_person(person["id"])
    color = ink_color()
    do_overflow = random.random() < 0.15
    do_med_correction = random.random() < 0.10
    multi_visit = person["visits"] >= 3

    concern = random.choice(CONCERNS)
    prior_tx = random.choice(PRIOR_TREATMENT)
    prior_tx_yn = "No" if prior_tx == "None" else "Yes"
    med = random.choice(MEDICATIONS)
    substance = random.choices(
        SUBSTANCE_OPTS,
        weights=[25, 15, 10, 10, 40]
    )[0]
    last_use = "none" if substance == "None" else random.choice(["current", "3 months ago", "1 year ago", "last week"])
    trauma = "Yes" if (multi_visit and random.random() < 0.5) else "No"
    suicidal = "Yes" if is_crisis else "No"
    emg_name = f"{random.choice(['Maria','James','Denise','Tony','Ruth'])} {person['last']}"
    emg_rel = random.choice(["Spouse", "Parent", "Sibling", "Friend", "Case manager"])
    emg_phone = f"{person['phone'][:3]}-555-{random.randint(1000,9999)}"
    clinician = random.choice(CLINICIANS)

    W, H = letter
    c = canvas.Canvas(out_path, pagesize=letter)

    c.setFont("Helvetica-Bold", 13)
    c.setFillColorRGB(0, 0, 0)
    c.drawCentredString(W / 2, H - 50, agency_name(person["tenant"]))
    c.setFont("Helvetica-Bold", 11)
    c.drawCentredString(W / 2, H - 66, "BEHAVIORAL HEALTH INTAKE")
    c.setFont("Helvetica", 8)
    c.drawCentredString(W / 2, H - 78, f"Form BH-003  |  Date: {visit_date.strftime('%m/%d/%Y')}")
    c.setLineWidth(1)
    c.line(40, H - 84, W - 40, H - 84)

    y = H - 110
    lx = 45
    rx = W / 2 + 10
    fw_full = W - 90
    fw_half = W / 2 - 55

    # Name | DOB
    draw_label(c, "Client Name", lx, y + 18)
    draw_field_box(c, lx, y, fw_half, 18)
    draw_rotated_text(c, f"{person['first']} {person['last']}", lx + 4, y + 4, font_name, 11, color)

    draw_label(c, "Date of Birth", rx, y + 18)
    draw_field_box(c, rx, y, fw_half - 20, 18)
    dob = date.fromisoformat(person["dob"])
    draw_rotated_text(c, dob.strftime("%m/%d/%Y"), rx + 4, y + 4, font_name, 11, color)
    y -= 34

    # Presenting concern (3 lines)
    draw_label(c, "Presenting Concern", lx, y + 18)
    for li in range(3):
        draw_field_box(c, lx, y - li * 22, fw_full, 18)
        if li == 0:
            text = concern
            if do_overflow:
                # simulate text slightly overflowing — just use full text without truncation
                draw_rotated_text(c, text, lx + 2, y + 4, font_name, 10, color)
            else:
                draw_rotated_text(c, text[:70], lx + 4, y + 4, font_name, 10, color)
    y -= 80

    # Prior treatment
    draw_label(c, "Prior mental health treatment?", lx, y + 4)
    draw_checkbox_row(c, lx + 185, y - 2, ["Yes", "No"], prior_tx_yn, font_name, color)
    y -= 20
    draw_label(c, "If yes, where:", lx, y + 18)
    draw_field_box(c, lx + 70, y, fw_full - 70, 18)
    if prior_tx_yn == "Yes":
        draw_rotated_text(c, prior_tx, lx + 74, y + 4, font_name, 10, color)
    y -= 34

    # Medications (2 lines)
    draw_label(c, "Current Medications (or 'None')", lx, y + 18)
    draw_field_box(c, lx, y, fw_full, 18)
    if do_med_correction:
        wrong_med = random.choice(MEDICATIONS)
        draw_strikethrough_correction(c, wrong_med, med, lx + 4, y + 4, font_name, 10, color)
    else:
        draw_rotated_text(c, med, lx + 4, y + 4, font_name, 10, color)
    y -= 34

    # Substance use checkboxes
    draw_label(c, "Substance Use History:", lx, y + 4)
    draw_checkbox_row(c, lx + 135, y - 2, SUBSTANCE_OPTS, substance, font_name, color)
    y -= 26
    draw_label(c, "Last use:", lx, y + 18)
    draw_field_box(c, lx + 55, y, 150, 18)
    draw_rotated_text(c, last_use, lx + 59, y + 4, font_name, 10, color)
    y -= 34

    # Trauma | Suicidal
    draw_label(c, "Trauma history:", lx, y + 4)
    draw_checkbox_row(c, lx + 95, y - 2, ["Yes", "No"], trauma, font_name, color)
    draw_label(c, "Suicidal ideation?", rx, y + 4)
    draw_checkbox_row(c, rx + 110, y - 2, ["Yes", "No"], suicidal, font_name, color)
    y -= 30

    # Emergency contact
    draw_label(c, "Emergency Contact", lx, y + 18)
    draw_field_box(c, lx, y, fw_half - 20, 18)
    draw_rotated_text(c, emg_name, lx + 4, y + 4, font_name, 10, color)

    draw_label(c, "Relationship", rx - 10, y + 18)
    draw_field_box(c, rx - 10, y, 80, 18)
    draw_rotated_text(c, emg_rel, rx - 6, y + 4, font_name, 9, color)

    draw_label(c, "Phone", rx + 80, y + 18)
    draw_field_box(c, rx + 80, y, fw_half - 60, 18)
    draw_rotated_text(c, emg_phone, rx + 84, y + 4, font_name, 10, color)
    y -= 40

    # Clinician
    draw_label(c, "Clinician", lx, y + 18)
    draw_field_box(c, lx, y, fw_half, 18)
    draw_rotated_text(c, clinician, lx + 4, y + 4, font_name, 10, color)

    draw_label(c, "Signature", rx, y + 18)
    c.setStrokeColorRGB(0.3, 0.3, 0.3)
    c.setLineWidth(0.5)
    c.line(rx, y, rx + 160, y)
    draw_signature_scrawl(c, rx + 5, y + 2, color)

    draw_label(c, "Date", rx + 170, y + 18)
    c.line(rx + 185, y, rx + 270, y)
    draw_rotated_text(c, visit_date.strftime("%m/%d/%Y"), rx + 188, y + 4, font_name, 10, color)

    c.save()

def main():
    register_fonts()
    ensure_dir(STAGING)
    font_counts = {}
    errors = []
    total = 0

    for person in PEOPLE:
        n_forms = BEHAVIORAL_PEOPLE.get(person["id"], 0)
        if n_forms == 0:
            continue
        dates = visit_dates(person["visits"])
        form_dates = dates[:n_forms]

        for visit_num, vdate in enumerate(form_dates, 1):
            fname = f"{person['id']}_{visit_num:02d}_{TEMPLATE_ID}.pdf"
            out_path = os.path.join(STAGING, fname)
            font_name, _ = font_for_person(person["id"])
            font_counts[font_name] = font_counts.get(font_name, 0) + 1
            # Mark middle visit of frequent flyer as crisis for suicidal ideation
            is_crisis = (person["id"] in ("P019", "P039") and visit_num == 2)
            try:
                generate_form(person, visit_num, vdate, out_path, is_crisis=is_crisis)
                total += 1
            except Exception as e:
                errors.append(f"{fname}: {e}")

    print(f"\n=== Behavioral Health Intake ===")
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
