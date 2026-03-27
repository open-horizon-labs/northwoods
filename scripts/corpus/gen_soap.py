"""
Generate SOAP Progress Note PDFs.
Output: /tmp/corpus-staging/soap/
"""
import sys, os, random
from datetime import date
sys.path.insert(0, os.path.dirname(__file__))
from people import PEOPLE, font_for_person, TENANT_IDS
from gen_utils import (
    register_fonts, ink_color, visit_dates, draw_rotated_text,
    draw_label, draw_field_box, draw_checkbox_row, draw_signature_scrawl,
    draw_marginal_note, agency_name, ensure_dir
)
from reportlab.lib.pagesizes import letter
from reportlab.pdfgen import canvas

STAGING = "/tmp/corpus-staging/soap"
TEMPLATE_ID = "soap-note"

S_TEXTS = [
    "Client reports feeling overwhelmed by ongoing housing situation. States landlord has not responded to repair requests. Mood described as really low this week.",
    "Client reports some improvement since last session. Sleeping better, completed job application. Still anxious about court date.",
    "Client in crisis — received eviction notice yesterday. Reports not eating, difficulty leaving apartment.",
    "Client reports stable mood. Secured part-time employment, feels more hopeful. Continuing to work on coping strategies.",
    "Client tearful, reports argument with family member over living arrangements. Denies SI. Sleep disrupted.",
    "Client engaged and on time. Reports completing homework assignment. Noticed triggers more clearly this week.",
    "Client reports medication side effects (drowsiness), requesting dose review. Mood flat but stable.",
    "Client missed last session, reports overwhelmed by childcare responsibilities. Apologetic and motivated.",
    "Client reports first full week without panic attack. Credits breathing techniques from prior session.",
    "Client discloses new stressor: partner job loss. Financial stress increasing. Safety plan reviewed.",
]
O_TEXTS = [
    "Client appeared anxious, fidgeting throughout session. Affect constricted, speech pressured at times.",
    "Client calm and engaged. Made good eye contact. Affect brighter than previous session.",
    "Client tearful on arrival, composed by mid-session. Thought process organized.",
    "Client guarded initially, opened up after rapport established. Affect flat but reactive.",
    "Client alert, cooperative. Dressed appropriately. No signs of acute distress.",
    "Client arrived 10 minutes late, appeared rushed. Affect appropriate, mood euthymic.",
]
A_TEXTS = [
    "Adjustment disorder with mixed anxiety and depressed mood (F43.23). Housing instability as primary stressor.",
    "Major depressive episode, moderate (F32.1). Responding to treatment.",
    "PTSD, chronic (F43.10). Trauma triggers related to housing loss.",
    "Generalized anxiety disorder (F41.1). Situational stressors exacerbating baseline anxiety.",
    "Substance use disorder, alcohol, mild (F10.10). In early remission.",
]
P_TEXTS = [
    "Continue weekly CBT sessions. Refer to housing stabilization program. Client to call 211 re emergency rental assistance.",
    "Increase session frequency to 2x/week given current crisis. Coordinate with case manager. Safety plan reviewed and updated.",
    "Step down to biweekly sessions. Client to continue medication management with PCP. Follow up on job placement referral.",
    "Assign thought record homework. Explore coping strategies for financial stress. Schedule medication review.",
    "Refer to peer support group. Continue present-focused CBT. Review safety plan at each session.",
    "Collaborate with housing case manager. Address sleep hygiene. Client to practice progressive muscle relaxation.",
]
CLINICIANS = ["J. Rivera, LCSW", "M. Osei, LMFT", "T. Chen, LPC", "B. Santos, MSW"]
RISK_LEVELS = ["Low", "Moderate", "High"]

# Only people with 3+ visits
SOAP_PEOPLE = {
    "P019": 5, "P039": 5,
    "P017": 2, "P018": 1,
    "P037": 2, "P038": 1,
    "P015": 1, "P034": 1,
}

def generate_form(person, visit_num, session_num, visit_date, out_path, risk_override=None):
    font_name, _ = font_for_person(person["id"])
    # SOAP notes: lean toward cleaner fonts
    clean_fonts = ["verdana", "arial", "trebuchet"]
    if font_name not in clean_fonts and random.random() < 0.6:
        font_name = random.choice(clean_fonts)

    color = ink_color()
    do_marginal = random.random() < 0.10
    blank_next_appt = random.random() < 0.10

    s_text = random.choice(S_TEXTS)
    o_text = random.choice(O_TEXTS)
    a_text = random.choice(A_TEXTS)
    p_items = random.sample(P_TEXTS, 2)
    risk = risk_override or random.choices(RISK_LEVELS, weights=[60, 30, 10])[0]
    clinician = random.choice(CLINICIANS)
    next_appt = (visit_date.replace(day=min(visit_date.day + 14, 28))).strftime("%m/%d/%Y") if not blank_next_appt else "TBD"

    W, H = letter
    c = canvas.Canvas(out_path, pagesize=letter)

    c.setFont("Helvetica-Bold", 13)
    c.setFillColorRGB(0, 0, 0)
    c.drawCentredString(W / 2, H - 50, agency_name(person["tenant"]))
    c.setFont("Helvetica-Bold", 11)
    c.drawCentredString(W / 2, H - 66, "SOAP PROGRESS NOTE")
    c.setFont("Helvetica", 8)
    c.drawCentredString(W / 2, H - 78, f"Form PN-004  |  Date: {visit_date.strftime('%m/%d/%Y')}")
    c.setLineWidth(1)
    c.line(40, H - 84, W - 40, H - 84)

    y = H - 108
    lx = 45
    rx = W / 2 + 10
    fw_full = W - 90
    fw_half = W / 2 - 55

    # Client name | Session #
    draw_label(c, "Client Name", lx, y + 18)
    draw_field_box(c, lx, y, fw_half, 18)
    draw_rotated_text(c, f"{person['first']} {person['last']}", lx + 4, y + 4, font_name, 11, color)

    draw_label(c, "Session #", rx, y + 18)
    draw_field_box(c, rx, y, 80, 18)
    draw_rotated_text(c, f"Session {session_num}", rx + 4, y + 4, font_name, 10, color)
    y -= 32

    # S — Subjective (4 lines)
    draw_label(c, "S — Subjective (client-reported):", lx, y + 18)
    words = s_text.split()
    lines = []
    cur = ""
    for w in words:
        if len(cur) + len(w) + 1 < 80:
            cur += (" " if cur else "") + w
        else:
            lines.append(cur)
            cur = w
    if cur:
        lines.append(cur)
    lines = (lines + ["", "", ""])[:4]
    for li, line in enumerate(lines):
        draw_field_box(c, lx, y - li * 20, fw_full, 18)
        if line:
            draw_rotated_text(c, line, lx + 4, y - li * 20 + 3, font_name, 9, color)
    y -= 94

    # O — Objective (3 lines)
    draw_label(c, "O — Objective (clinician-observed):", lx, y + 18)
    o_lines = (o_text.split(". ") + ["", ""])[:3]
    for li, line in enumerate(o_lines):
        draw_field_box(c, lx, y - li * 20, fw_full, 18)
        if line:
            draw_rotated_text(c, line.strip(), lx + 4, y - li * 20 + 3, font_name, 9, color)
    y -= 72

    # A — Assessment (2 lines)
    draw_label(c, "A — Assessment:", lx, y + 18)
    draw_field_box(c, lx, y, fw_full, 18)
    draw_rotated_text(c, a_text[:85], lx + 4, y + 4, font_name, 9, color)
    draw_field_box(c, lx, y - 20, fw_full, 18)
    y -= 50

    # P — Plan (3 lines with dash items)
    draw_label(c, "P — Plan:", lx, y + 18)
    plan_lines = [f"- {p}" for p in p_items] + [""]
    for li, line in enumerate(plan_lines[:3]):
        draw_field_box(c, lx, y - li * 20, fw_full, 18)
        if line:
            draw_rotated_text(c, line[:85], lx + 4, y - li * 20 + 3, font_name, 9, color)
    y -= 72

    # Risk | Next appt
    draw_label(c, "Risk Level:", lx, y + 4)
    draw_checkbox_row(c, lx + 70, y - 2, RISK_LEVELS, risk, font_name, color)

    draw_label(c, "Next Appointment:", rx, y + 18)
    draw_field_box(c, rx, y, fw_half - 20, 18)
    draw_rotated_text(c, next_appt, rx + 4, y + 4, font_name, 10, color)
    y -= 34

    # Clinician signature
    draw_label(c, "Clinician", lx, y + 18)
    draw_field_box(c, lx, y, fw_half - 20, 18)
    draw_rotated_text(c, clinician, lx + 4, y + 4, font_name, 10, color)

    draw_label(c, "Signature", rx, y + 18)
    c.setStrokeColorRGB(0.3, 0.3, 0.3)
    c.setLineWidth(0.5)
    c.line(rx, y, rx + 150, y)
    draw_signature_scrawl(c, rx + 5, y + 2, color)

    if do_marginal:
        draw_marginal_note(c, random.choice(["see prior note", "f/u housing", "med review needed"]),
                           W - 80, H - 160, font_name, color)

    c.save()

def main():
    register_fonts()
    ensure_dir(STAGING)
    font_counts = {}
    errors = []
    total = 0

    for person in PEOPLE:
        n_forms = SOAP_PEOPLE.get(person["id"], 0)
        if n_forms == 0:
            continue
        dates = visit_dates(person["visits"])
        soap_dates = dates[:n_forms]

        for visit_num, vdate in enumerate(soap_dates, 1):
            fname = f"{person['id']}_{visit_num:02d}_{TEMPLATE_ID}.pdf"
            out_path = os.path.join(STAGING, fname)
            font_name, _ = font_for_person(person["id"])
            font_counts[font_name] = font_counts.get(font_name, 0) + 1
            # High risk for crisis session of frequent flyers
            risk_override = "High" if (person["id"] in ("P019", "P039") and visit_num == 3) else None
            try:
                generate_form(person, visit_num, visit_num, vdate, out_path, risk_override=risk_override)
                total += 1
            except Exception as e:
                errors.append(f"{fname}: {e}")

    print(f"\n=== SOAP Progress Notes ===")
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
