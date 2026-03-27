"""
SOAP Progress Note — v2 layout.
Output: /tmp/corpus-v2/soap/
"""
import sys, os, random
from datetime import date
sys.path.insert(0, os.path.dirname(__file__))
from people import PEOPLE
from gen_utils_v2 import (
    register_fonts_v2, fill_font_for_person_v2, ink_color_v2, visit_dates,
    draw_v2_header, draw_section_header, draw_field_line, draw_field_block,
    draw_checkbox_v2, draw_signature_v2,
    agency_name_v2, ensure_dir, PRINT_FONT, RULE_COLOR
)
from reportlab.lib.pagesizes import letter
from reportlab.lib.colors import black, HexColor
from reportlab.pdfgen import canvas

STAGING = "/tmp/corpus-v2/soap"
TEMPLATE_ID = "soap-note"

S_TEXTS = [
    ["Reports feeling much more hopeful this week.", "Secured part-time work, starts Monday.", "Sleep improving — averaging 6 hrs."],
    ["States: 'I can't stop thinking about what happened.'", "Flashbacks most nights, avoids leaving apartment.", "Denies SI. Support system limited."],
    ["Eviction notice received yesterday. Very distressed.", "Not eating, reports staying in bed most of day.", "Called crisis line once but hung up."],
    ["Mood described as 'flat but okay.'", "Completed housing application with assistance.", "Practicing breathing exercises between sessions."],
    ["Reports argument with partner escalated last week.", "Children present. No physical altercation.", "Client tearful, ambivalent about leaving."],
    ["Good week overall. Job interview went well.", "Medication side effects improving (less drowsy).", "Engaged with peer support group twice."],
]
O_TEXTS = [
    "Calm, cooperative. Affect appropriate. Eye contact good. Speech clear and organized.",
    "Tearful on arrival. Affect constricted. Thought process coherent. No acute distress by end of session.",
    "Anxious presentation. Fidgeting throughout. Pressured speech early in session, modulated later.",
    "Alert and engaged. Better groomed than previous session. Mood subjectively improved.",
    "Guarded initially. Opened gradually. Affect flat but reactive to topic. No observed SI/HI.",
]
A_TEXTS = [
    "Adjustment disorder w/ mixed anxiety and depressed mood (F43.23). Stressor: housing/financial.",
    "Major depressive episode, moderate (F32.1). Showing early treatment response.",
    "PTSD, chronic (F43.10). Avoidance and hypervigilance prominent.",
    "Generalized anxiety disorder (F41.1). Functional impairment moderate.",
    "Substance use disorder, alcohol, mild (F10.10). Sustained remission, 6 weeks.",
]
P_TEXTS = [
    ["Continue weekly CBT. Assign thought record worksheet.",
     "Refer to housing stabilization program (see attached).",
     "Follow up on job placement referral next session."],
    ["Increase to 2x/week given crisis presentation.",
     "Safety plan reviewed and updated — copy given to client.",
     "Coordinate with case manager re: emergency housing."],
    ["Step down to biweekly sessions — client stable.",
     "PCP referral for medication management.",
     "Explore peer support / NAMI connection."],
]
CLINICIANS = ["J. Rivera, LCSW", "M. Osei, LMFT", "T. Chen, LPC", "B. Santos, MSW"]
RISK = ["Low", "Moderate", "High"]

SOAP_PEOPLE = {
    "P019": 5, "P039": 5,
    "P017": 2, "P018": 1,
    "P037": 2, "P038": 1,
    "P015": 1, "P034": 1,
}

def generate_form(person, visit_num, session_num, vdate, out_path, risk_override=None):
    # SOAP notes: lean toward cleaner serifs
    clean = ["georgia", "times", "baskerville"]
    font = fill_font_for_person_v2(person["id"])
    if font not in clean and random.random() < 0.65:
        font = random.choice(clean)
    color = ink_color_v2()
    blank_next = random.random() < 0.10
    risk = risk_override or random.choices(RISK, weights=[58, 32, 10])[0]
    s_lines = random.choice(S_TEXTS)
    o_text = random.choice(O_TEXTS)
    a_text = random.choice(A_TEXTS)
    p_lines = random.choice(P_TEXTS)
    clinician = random.choice(CLINICIANS)
    next_appt = vdate.replace(day=min(vdate.day + 14, 28)).strftime("%m/%d/%Y") if not blank_next else "TBD — client to call"

    W, H = letter
    c = canvas.Canvas(out_path, pagesize=letter)
    c.setFillColor(HexColor("#fafaf8"))
    c.rect(0, 0, W, H, stroke=0, fill=1)

    draw_v2_header(c, agency_name_v2(person["tenant"]),
                   "Progress Note (SOAP Format)", f"PN-004 (Rev. 09/23)", vdate)

    y = H - 92
    LX = 18
    W_INNER = W - 36
    dob = date.fromisoformat(person["dob"])

    y = draw_section_header(c, "Client Identification", y)
    draw_field_line(c, "Client:", f"{person['first']} {person['last']}", LX, y, 55, 240, font, 11, color)
    draw_field_line(c, "DOB:", dob.strftime("%m/%d/%Y"), LX + 310, y, 40, 90, font, 10, color)
    draw_field_line(c, "Session #:", str(session_num), LX + 460, y, 65, 56, font, 10, color)
    y -= 26

    y = draw_section_header(c, "S  —  Subjective  (Client-Reported)", y)
    y = draw_field_block(c, "", s_lines + [""], LX, y, W_INNER, 17, font, 9, color)
    y -= 2

    y = draw_section_header(c, "O  —  Objective  (Clinician-Observed)", y)
    words = o_text.split()
    l1 = " ".join(words[:10]); l2 = " ".join(words[10:]) if len(words) > 10 else ""
    y = draw_field_block(c, "", [l1, l2, ""], LX, y, W_INNER, 17, font, 9, color)
    y -= 2

    y = draw_section_header(c, "A  —  Assessment", y)
    y = draw_field_block(c, "", [a_text, ""], LX, y, W_INNER, 17, font, 9, color)
    y -= 2

    y = draw_section_header(c, "P  —  Plan", y)
    y = draw_field_block(c, "", [f"• {l}" for l in p_lines] + [""], LX, y, W_INNER, 17, font, 9, color)
    y -= 4

    y = draw_section_header(c, "Risk & Next Steps", y)
    c.setFont(PRINT_FONT, 8); c.setFillColor(black)
    c.drawString(LX, y + 2, "Risk Level:")
    draw_checkbox_v2(c, LX + 75, y - 2, RISK, risk, font, color)
    draw_field_line(c, "Next Appointment:", next_appt, LX + 280, y, 120, 180, font, 9, color)
    y -= 26

    c.setFont(PRINT_FONT, 8); c.setFillColor(black)
    c.drawString(LX, y + 2, "Clinician:")
    draw_field_line(c, "", clinician, LX + 65, y, 0, 160, font, 10, color)
    c.drawString(LX + 240, y + 2, "Signature:")
    draw_signature_v2(c, LX + 310, y, color, 150)
    draw_field_line(c, "Date:", vdate.strftime("%m/%d/%Y"), LX + 480, y, 40, 76, font, 10, color)

    y -= 22
    c.setStrokeColor(RULE_COLOR); c.setLineWidth(0.3)
    c.line(LX, y, W - LX, y)
    c.setFont(PRINT_FONT, 6); c.setFillColor(HexColor("#888888"))
    c.drawString(LX, y - 8, "CONFIDENTIAL — DO NOT FILE IN GENERAL RECORD")
    c.drawRightString(W - LX, y - 8, f"Session {session_num}  |  {person['tenant'].upper()}")

    c.save()

def main():
    register_fonts_v2()
    ensure_dir(STAGING)
    font_counts = {}; errors = []; total = 0

    for person in PEOPLE:
        n = SOAP_PEOPLE.get(person["id"], 0)
        if not n: continue
        dates = visit_dates(person["visits"])
        for vn, vdate in enumerate(dates[:n], 1):
            fname = f"{person['id']}_{vn:02d}_v2_{TEMPLATE_ID}.pdf"
            out = os.path.join(STAGING, fname)
            fn = fill_font_for_person_v2(person["id"])
            font_counts[fn] = font_counts.get(fn, 0) + 1
            risk_override = "High" if person["id"] in ("P019", "P039") and vn == 3 else None
            try:
                generate_form(person, vn, vn, vdate, out, risk_override)
                total += 1
            except Exception as e:
                errors.append(f"{fname}: {e}")
                import traceback; traceback.print_exc()

    print(f"\n=== SOAP Notes v2 ===")
    print(f"PDFs: {total}  |  Fonts: {font_counts}")
    if errors:
        for e in errors: print(f"  ERR: {e}")

if __name__ == "__main__":
    main()
