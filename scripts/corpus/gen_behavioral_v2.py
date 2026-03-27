"""
Behavioral Health Intake — v2 layout.
Output: /tmp/corpus-v2/behavioral/
"""
import sys, os, random
from datetime import date
sys.path.insert(0, os.path.dirname(__file__))
from people import PEOPLE
from gen_utils_v2 import (
    register_fonts_v2, fill_font_for_person_v2, ink_color_v2, visit_dates,
    draw_v2_header, draw_section_header, draw_field_line, draw_field_block,
    draw_checkbox_v2, draw_signature_v2, draw_correction_v2,
    agency_name_v2, ensure_dir, PRINT_FONT, RULE_COLOR
)
from reportlab.lib.pagesizes import letter
from reportlab.lib.colors import black, HexColor
from reportlab.pdfgen import canvas

STAGING = "/tmp/corpus-v2/behavioral"
TEMPLATE_ID = "behavioral-health"

SUBSTANCE_OPTS = ["Alcohol", "Cannabis", "Opioids", "Stimulants", "None"]
CONCERNS = [
    "Persistent anxiety, difficulty sleeping since job loss",
    "Depressed mood, loss of interest in daily activities",
    "Intrusive thoughts following recent traumatic event",
    "Grief — lost spouse 6 weeks ago, unable to function",
    "Self-referred; reports increasing panic attacks",
    "Family conflict escalating; children witnessing arguments",
    "Post-eviction stress, feeling hopeless about future",
    "Reported by case manager after missed appointments",
]
PRIOR = ["None", "County mental health, 2021-22", "Private therapist, stopped 2023 — cost",
         "Inpatient psychiatric, 2019", "School counselor as adolescent"]
MEDS = ["None", "Sertraline 50mg daily", "Trazodone 100mg QHS",
        "Buspirone 15mg BID", "Fluoxetine 20mg daily", "Gabapentin 300mg TID"]
CLINICIANS = ["J. Rivera, LCSW", "M. Osei, LMFT", "T. Chen, LPC", "B. Santos, MSW"]

BEHAVIORAL_PEOPLE = {
    "P019": 3, "P039": 3,
    "P015": 2, "P016": 2, "P017": 2, "P018": 2,
    "P034": 2, "P035": 2, "P037": 2, "P038": 2,
    "P005": 1, "P006": 1, "P008": 1,
    "P022": 1, "P026": 1, "P028": 1,
}

def generate_form(person, visit_num, vdate, out_path, is_crisis=False):
    font = fill_font_for_person_v2(person["id"])
    color = ink_color_v2()
    multi = person["visits"] >= 3
    do_med_corr = random.random() < 0.10

    concern = random.choice(CONCERNS)
    prior = random.choice(PRIOR)
    prior_yn = "No" if prior == "None" else "Yes"
    med = random.choice(MEDS)
    substance = random.choices(SUBSTANCE_OPTS, weights=[24, 16, 10, 8, 42])[0]
    last_use = "None" if substance == "None" else random.choice(["Current", "Last week", "3 months ago", "1 year ago"])
    trauma = "Yes" if multi and random.random() < 0.55 else "No"
    si = "Yes" if is_crisis else "No"
    emg = f"{random.choice(['Rosa','James','Hank','Carol','Lou'])} {person['last']}"
    emg_rel = random.choice(["Sibling", "Parent", "Spouse", "Friend", "Case mgr."])
    emg_ph = f"{person['phone'][:3]}-555-{random.randint(2000,9999)}"
    clinician = random.choice(CLINICIANS)

    W, H = letter
    c = canvas.Canvas(out_path, pagesize=letter)
    c.setFillColor(HexColor("#fafaf8"))
    c.rect(0, 0, W, H, stroke=0, fill=1)

    draw_v2_header(c, agency_name_v2(person["tenant"]),
                   "Behavioral Health — Initial Intake Assessment", f"BH-003 (Rev. 06/23)", vdate)

    y = H - 92
    LX = 18
    W_INNER = W - 36
    dob = date.fromisoformat(person["dob"])

    y = draw_section_header(c, "Section 1 — Client Identification", y)
    draw_field_line(c, "Client Name:", f"{person['first']} {person['last']}", LX, y, 90, 280, font, 11, color)
    draw_field_line(c, "DOB:", dob.strftime("%m/%d/%Y"), LX + 390, y, 40, 100, font, 10, color)
    y -= 26

    y = draw_section_header(c, "Section 2 — Presenting Concern", y)
    words = concern.split()
    l1 = " ".join(words[:9]); l2 = " ".join(words[9:]) if len(words) > 9 else ""
    y = draw_field_block(c, "Describe the primary reason for seeking services today:",
                         [l1, l2, ""], LX, y, W_INNER, 17, font, 9, color)
    y -= 4

    y = draw_section_header(c, "Section 3 — Mental Health & Substance Use History", y)
    c.setFont(PRINT_FONT, 8); c.setFillColor(black)
    c.drawString(LX, y + 2, "Prior mental health treatment?")
    draw_checkbox_v2(c, LX + 190, y - 2, ["Yes", "No"], prior_yn, font, color)
    y -= 20
    draw_field_line(c, "If yes, provider/date:", prior if prior_yn == "Yes" else "", LX, y, 140, 350, font, 9, color)
    y -= 22
    if do_med_corr:
        wrong_med = random.choice(MEDS)
        draw_field_line(c, "Current medications:", "", LX, y, 140, 30, font, 9, color)
        draw_correction_v2(c, wrong_med, med, LX + 176, y, font, color)
    else:
        draw_field_line(c, "Current medications:", med, LX, y, 140, 340, font, 9, color)
    y -= 22
    c.setFont(PRINT_FONT, 8); c.setFillColor(black)
    c.drawString(LX, y + 2, "Substance use:")
    draw_checkbox_v2(c, LX + 100, y - 2, SUBSTANCE_OPTS, substance, font, color)
    draw_field_line(c, "Last use:", last_use, LX + 420, y, 60, 90, font, 9, color)
    y -= 26

    y = draw_section_header(c, "Section 4 — Risk Assessment", y)
    c.setFont(PRINT_FONT, 8); c.setFillColor(black)
    c.drawString(LX, y + 2, "Trauma history:")
    draw_checkbox_v2(c, LX + 100, y - 2, ["Yes", "No"], trauma, font, color)
    c.drawString(LX + 240, y + 2, "Current suicidal ideation:")
    draw_checkbox_v2(c, LX + 390, y - 2, ["Yes", "No"], si, font, color)
    y -= 26

    y = draw_section_header(c, "Section 5 — Emergency Contact", y)
    draw_field_line(c, "Contact Name:", emg, LX, y, 100, 200, font, 10, color)
    draw_field_line(c, "Relationship:", emg_rel, LX + 320, y, 90, 80, font, 9, color)
    draw_field_line(c, "Phone:", emg_ph, LX + 510, y, 50, 46, font, 9, color)
    y -= 26

    y = draw_section_header(c, "Section 6 — Clinician Signature", y)
    draw_field_line(c, "Clinician Name:", clinician, LX, y, 100, 200, font, 10, color)
    draw_signature_v2(c, LX + 320, y, color, 150)
    draw_field_line(c, "Date:", vdate.strftime("%m/%d/%Y"), LX + 490, y, 40, 76, font, 10, color)

    y -= 22
    c.setStrokeColor(RULE_COLOR); c.setLineWidth(0.3)
    c.line(LX, y, W - LX, y)
    c.setFont(PRINT_FONT, 6); c.setFillColor(HexColor("#888888"))
    c.drawString(LX, y - 8, "AGENCY USE ONLY — CONFIDENTIAL HEALTH RECORD")
    c.drawRightString(W - LX, y - 8, f"Visit #{visit_num:02d}  |  {person['tenant'].upper()}")

    c.save()

def main():
    register_fonts_v2()
    ensure_dir(STAGING)
    font_counts = {}; errors = []; total = 0

    for person in PEOPLE:
        n = BEHAVIORAL_PEOPLE.get(person["id"], 0)
        if not n: continue
        dates = visit_dates(person["visits"])
        for vn, vdate in enumerate(dates[:n], 1):
            fname = f"{person['id']}_{vn:02d}_v2_{TEMPLATE_ID}.pdf"
            out = os.path.join(STAGING, fname)
            fn = fill_font_for_person_v2(person["id"])
            font_counts[fn] = font_counts.get(fn, 0) + 1
            is_crisis = person["id"] in ("P019", "P039") and vn == 2
            try:
                generate_form(person, vn, vdate, out, is_crisis)
                total += 1
            except Exception as e:
                errors.append(f"{fname}: {e}")
                import traceback; traceback.print_exc()

    print(f"\n=== Behavioral Health v2 ===")
    print(f"PDFs: {total}  |  Fonts: {font_counts}")
    if errors:
        for e in errors: print(f"  ERR: {e}")

if __name__ == "__main__":
    main()
