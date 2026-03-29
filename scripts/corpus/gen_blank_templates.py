"""
Generate BLANK v2 template PDFs for each template type.

These are empty forms (no handwritten data, no signatures, no checkbox
selections) using the same v2 county-government layout as the seed corpus.

Output: samples/intakes/blank-{template-id}.pdf
"""
import sys, os

sys.path.insert(0, os.path.dirname(__file__))
from gen_utils_v2 import (
    register_fonts_v2,
    draw_v2_header,
    draw_section_header,
    draw_field_line,
    draw_field_block,
    ensure_dir,
    PRINT_FONT,
    RULE_COLOR,
)
from reportlab.lib.pagesizes import letter
from reportlab.lib.colors import black, HexColor
from reportlab.pdfgen import canvas
from datetime import date

# Use a fixed agency name for blank templates (generic)
AGENCY = "County Dept. of Social Services"


def _blank_checkbox(c, x, y, options):
    """Draw checkboxes with NO selection — just empty circles + labels."""
    cx = x
    for opt in options:
        c.setStrokeColor(RULE_COLOR)
        c.setLineWidth(0.6)
        c.circle(cx + 5, y + 4, 5, stroke=1, fill=0)
        c.setFillColor(black)
        c.setFont(PRINT_FONT, 7.5)
        c.drawString(cx + 12, y + 1, opt)
        cx += 12 + len(opt) * 4.5 + 10


def _blank_signature_line(c, x, y, label="Signature:", width=180):
    """Draw a labeled signature underline with no actual signature."""
    c.setFillColor(black)
    c.setFont(PRINT_FONT, 8)
    c.drawString(x, y + 2, label)
    label_w = c.stringWidth(label, PRINT_FONT, 8) + 6
    c.setStrokeColor(RULE_COLOR)
    c.setLineWidth(0.5)
    c.line(x + label_w, y, x + label_w + width, y)


def _footer(c, y, LX, W, template_label):
    c.setStrokeColor(RULE_COLOR)
    c.setLineWidth(0.3)
    c.line(LX, y, W - LX, y)
    c.setFont(PRINT_FONT, 6)
    c.setFillColor(HexColor("#888888"))
    c.drawString(LX, y - 8, "AGENCY USE ONLY")
    c.drawRightString(W - LX, y - 8, template_label)


# ── General Assistance ───────────────────────────────────────────────────────


def gen_general(out_path):
    W, H = letter
    c = canvas.Canvas(out_path, pagesize=letter)
    c.setFillColor(HexColor("#fafaf8"))
    c.rect(0, 0, W, H, stroke=0, fill=1)

    draw_v2_header(c, AGENCY, "Application for General Assistance",
                   "GA-001 (Rev. 03/24)", date.today())

    y = H - 92
    LX = 18
    W_INNER = W - 36

    # Section 1
    y = draw_section_header(c, "Section 1 -- Applicant Information", y)
    draw_field_line(c, "Full Legal Name:", "", LX, y, 120, 340, "Courier", 10, (0, 0, 0))
    draw_field_line(c, "Date of Birth:", "", LX + 470, y, 90, 60, "Courier", 10, (0, 0, 0))
    y -= 22
    draw_field_line(c, "Street Address:", "", LX, y, 100, 260, "Courier", 10, (0, 0, 0))
    draw_field_line(c, "City/State/Zip:", "", LX + 370, y, 90, 145, "Courier", 10, (0, 0, 0))
    y -= 22
    draw_field_line(c, "Phone:", "", LX, y, 60, 130, "Courier", 10, (0, 0, 0))
    draw_field_line(c, "SSN (last 4):", "", LX + 200, y, 90, 80, "Courier", 10, (0, 0, 0))
    draw_field_line(c, "Household Size:", "", LX + 390, y, 100, 60, "Courier", 10, (0, 0, 0))
    y -= 28

    # Section 2
    y = draw_section_header(c, "Section 2 -- Financial & Housing", y)
    draw_field_line(c, "Monthly Gross Income:", "", LX, y, 140, 80, "Courier", 10, (0, 0, 0))
    y -= 22
    c.setFillColor(black)
    c.setFont(PRINT_FONT, 8)
    c.drawString(LX, y + 2, "Current Housing Status:")
    _blank_checkbox(c, LX + 140, y - 2,
                    ["Renting", "Owned", "With family", "Homeless", "Unknown"])
    y -= 26

    # Section 3
    y = draw_section_header(c, "Section 3 -- Reason for Assistance", y)
    y = draw_field_block(c, "Describe your situation and what assistance you are requesting:",
                         ["", "", ""], LX, y, W_INNER, 17, "Courier", 9, (0, 0, 0))
    y -= 4

    # Section 4
    y = draw_section_header(c, "Section 4 -- Emergency Contact", y)
    draw_field_line(c, "Name:", "", LX, y, 50, 200, "Courier", 10, (0, 0, 0))
    draw_field_line(c, "Phone:", "", LX + 270, y, 50, 130, "Courier", 10, (0, 0, 0))
    draw_field_line(c, "Relationship:", "", LX + 460, y, 80, 56, "Courier", 9, (0, 0, 0))
    y -= 30

    # Section 5
    y = draw_section_header(c, "Section 5 -- Applicant Certification", y)
    c.setFillColor(black)
    c.setFont(PRINT_FONT, 7)
    cert = ("I certify that the information provided is true and complete to the best of my knowledge. "
            "I understand that providing false information may result in denial or termination of benefits.")
    c.drawString(LX, y + 2, cert[:110])
    y -= 14
    _blank_signature_line(c, LX, y, "Applicant Signature:", 180)
    draw_field_line(c, "Date:", "", LX + 330, y, 40, 100, "Courier", 10, (0, 0, 0))
    draw_field_line(c, "Staff ID:", "", LX + 490, y, 55, 50, "Courier", 9, (0, 0, 0))

    y -= 24
    _footer(c, y, LX, W, "General Assistance")
    c.save()
    print(f"  -> {out_path}")


# ── Housing Stability ────────────────────────────────────────────────────────


def gen_housing(out_path):
    W, H = letter
    c = canvas.Canvas(out_path, pagesize=letter)
    c.setFillColor(HexColor("#fafaf8"))
    c.rect(0, 0, W, H, stroke=0, fill=1)

    draw_v2_header(c, AGENCY, "Housing Stability Assessment",
                   "HS-002 (Rev. 01/24)", date.today())

    y = H - 92
    LX = 18
    W_INNER = W - 36

    y = draw_section_header(c, "Section 1 -- Client Identification", y)
    draw_field_line(c, "Client Name:", "", LX, y, 90, 280, "Courier", 11, (0, 0, 0))
    draw_field_line(c, "DOB:", "", LX + 390, y, 40, 100, "Courier", 10, (0, 0, 0))
    y -= 22
    draw_field_line(c, "Address:", "", LX, y, 70, 420, "Courier", 10, (0, 0, 0))
    y -= 26

    y = draw_section_header(c, "Section 2 -- Current Housing", y)
    c.setFillColor(black)
    c.setFont(PRINT_FONT, 8)
    c.drawString(LX, y + 2, "Type of Housing:")
    _blank_checkbox(c, LX + 110, y - 2,
                    ["Apartment", "House", "Room/Board", "Shelter", "Vehicle/Tent", "Other"])
    y -= 26

    draw_field_line(c, "Monthly Rent/Mortgage:", "", LX, y, 150, 70, "Courier", 10, (0, 0, 0))
    draw_field_line(c, "Months Behind:", "", LX + 250, y, 100, 40, "Courier", 10, (0, 0, 0))
    draw_field_line(c, "Time at Address:", "", LX + 420, y, 100, 60, "Courier", 10, (0, 0, 0))
    y -= 22

    c.setFillColor(black)
    c.setFont(PRINT_FONT, 8)
    c.drawString(LX, y + 2, "Eviction notice received?")
    _blank_checkbox(c, LX + 160, y - 2, ["Yes", "No"])
    c.drawString(LX + 280, y + 2, "Has Section 8 / voucher?")
    _blank_checkbox(c, LX + 440, y - 2, ["Yes", "No"])
    y -= 26

    draw_field_line(c, "Landlord Name:", "", LX, y, 110, 200, "Courier", 10, (0, 0, 0))
    draw_field_line(c, "Landlord Phone:", "", LX + 330, y, 110, 130, "Courier", 10, (0, 0, 0))
    y -= 22
    draw_field_line(c, "Prior address (12 mo):", "", LX, y, 140, 300, "Courier", 10, (0, 0, 0))
    y -= 26

    y = draw_section_header(c, "Section 3 -- Reason for Instability", y)
    y = draw_field_block(c, "Describe circumstances leading to housing instability:",
                         ["", "", ""], LX, y, W_INNER, 17, "Courier", 9, (0, 0, 0))
    y -= 4

    y = draw_section_header(c, "Section 4 -- Certification", y)
    c.setFont(PRINT_FONT, 7)
    c.setFillColor(black)
    c.drawString(LX, y + 2,
                 "I certify the above information is accurate. I understand false statements may result in denial of services.")
    y -= 18
    _blank_signature_line(c, LX, y, "Client Signature:", 180)
    draw_field_line(c, "Date:", "", LX + 320, y, 40, 100, "Courier", 10, (0, 0, 0))
    draw_field_line(c, "Worker:", "", LX + 480, y, 50, 56, "Courier", 9, (0, 0, 0))

    y -= 22
    _footer(c, y, LX, W, "Housing Stability")
    c.save()
    print(f"  -> {out_path}")


# ── Behavioral Health ────────────────────────────────────────────────────────


def gen_behavioral(out_path):
    W, H = letter
    c = canvas.Canvas(out_path, pagesize=letter)
    c.setFillColor(HexColor("#fafaf8"))
    c.rect(0, 0, W, H, stroke=0, fill=1)

    draw_v2_header(c, AGENCY,
                   "Behavioral Health -- Initial Intake Assessment",
                   "BH-003 (Rev. 06/23)", date.today())

    y = H - 92
    LX = 18
    W_INNER = W - 36

    y = draw_section_header(c, "Section 1 -- Client Identification", y)
    draw_field_line(c, "Client Name:", "", LX, y, 90, 280, "Courier", 11, (0, 0, 0))
    draw_field_line(c, "DOB:", "", LX + 390, y, 40, 100, "Courier", 10, (0, 0, 0))
    y -= 26

    y = draw_section_header(c, "Section 2 -- Presenting Concern", y)
    y = draw_field_block(c, "Describe the primary reason for seeking services today:",
                         ["", "", ""], LX, y, W_INNER, 17, "Courier", 9, (0, 0, 0))
    y -= 4

    y = draw_section_header(c, "Section 3 -- Mental Health & Substance Use History", y)
    c.setFont(PRINT_FONT, 8)
    c.setFillColor(black)
    c.drawString(LX, y + 2, "Prior mental health treatment?")
    _blank_checkbox(c, LX + 190, y - 2, ["Yes", "No"])
    y -= 20
    draw_field_line(c, "If yes, provider/date:", "", LX, y, 140, 350, "Courier", 9, (0, 0, 0))
    y -= 22
    draw_field_line(c, "Current medications:", "", LX, y, 140, 340, "Courier", 9, (0, 0, 0))
    y -= 22
    c.setFont(PRINT_FONT, 8)
    c.setFillColor(black)
    c.drawString(LX, y + 2, "Substance use:")
    _blank_checkbox(c, LX + 100, y - 2,
                    ["Alcohol", "Cannabis", "Opioids", "Stimulants", "None"])
    draw_field_line(c, "Last use:", "", LX + 420, y, 60, 90, "Courier", 9, (0, 0, 0))
    y -= 26

    y = draw_section_header(c, "Section 4 -- Risk Assessment", y)
    c.setFont(PRINT_FONT, 8)
    c.setFillColor(black)
    c.drawString(LX, y + 2, "Trauma history:")
    _blank_checkbox(c, LX + 100, y - 2, ["Yes", "No"])
    c.drawString(LX + 240, y + 2, "Current suicidal ideation:")
    _blank_checkbox(c, LX + 390, y - 2, ["Yes", "No"])
    y -= 26

    y = draw_section_header(c, "Section 5 -- Emergency Contact", y)
    draw_field_line(c, "Contact Name:", "", LX, y, 100, 200, "Courier", 10, (0, 0, 0))
    draw_field_line(c, "Relationship:", "", LX + 320, y, 90, 80, "Courier", 9, (0, 0, 0))
    draw_field_line(c, "Phone:", "", LX + 510, y, 50, 46, "Courier", 9, (0, 0, 0))
    y -= 26

    y = draw_section_header(c, "Section 6 -- Clinician Signature", y)
    draw_field_line(c, "Clinician Name:", "", LX, y, 100, 200, "Courier", 10, (0, 0, 0))
    _blank_signature_line(c, LX + 320, y, "Signature:", 150)
    draw_field_line(c, "Date:", "", LX + 490, y, 40, 76, "Courier", 10, (0, 0, 0))

    y -= 22
    _footer(c, y, LX, W, "Behavioral Health")
    c.save()
    print(f"  -> {out_path}")


# ── SOAP Note ────────────────────────────────────────────────────────────────


def gen_soap(out_path):
    W, H = letter
    c = canvas.Canvas(out_path, pagesize=letter)
    c.setFillColor(HexColor("#fafaf8"))
    c.rect(0, 0, W, H, stroke=0, fill=1)

    draw_v2_header(c, AGENCY, "Progress Note (SOAP Format)",
                   "PN-004 (Rev. 09/23)", date.today())

    y = H - 92
    LX = 18
    W_INNER = W - 36

    y = draw_section_header(c, "Client Identification", y)
    draw_field_line(c, "Client:", "", LX, y, 55, 240, "Courier", 11, (0, 0, 0))
    draw_field_line(c, "DOB:", "", LX + 310, y, 40, 90, "Courier", 10, (0, 0, 0))
    draw_field_line(c, "Session #:", "", LX + 460, y, 65, 56, "Courier", 10, (0, 0, 0))
    y -= 26

    y = draw_section_header(c, "S  --  Subjective  (Client-Reported)", y)
    y = draw_field_block(c, "", ["", "", "", ""], LX, y, W_INNER, 17, "Courier", 9, (0, 0, 0))
    y -= 2

    y = draw_section_header(c, "O  --  Objective  (Clinician-Observed)", y)
    y = draw_field_block(c, "", ["", "", ""], LX, y, W_INNER, 17, "Courier", 9, (0, 0, 0))
    y -= 2

    y = draw_section_header(c, "A  --  Assessment", y)
    y = draw_field_block(c, "", ["", ""], LX, y, W_INNER, 17, "Courier", 9, (0, 0, 0))
    y -= 2

    y = draw_section_header(c, "P  --  Plan", y)
    y = draw_field_block(c, "", ["", "", "", ""], LX, y, W_INNER, 17, "Courier", 9, (0, 0, 0))
    y -= 4

    y = draw_section_header(c, "Risk & Next Steps", y)
    c.setFont(PRINT_FONT, 8)
    c.setFillColor(black)
    c.drawString(LX, y + 2, "Risk Level:")
    _blank_checkbox(c, LX + 75, y - 2, ["Low", "Moderate", "High"])
    draw_field_line(c, "Next Appointment:", "", LX + 280, y, 120, 180, "Courier", 9, (0, 0, 0))
    y -= 26

    c.setFont(PRINT_FONT, 8)
    c.setFillColor(black)
    c.drawString(LX, y + 2, "Clinician:")
    draw_field_line(c, "", "", LX + 65, y, 0, 160, "Courier", 10, (0, 0, 0))
    _blank_signature_line(c, LX + 240, y, "Signature:", 150)
    draw_field_line(c, "Date:", "", LX + 480, y, 40, 76, "Courier", 10, (0, 0, 0))

    y -= 22
    _footer(c, y, LX, W, "SOAP Progress Note")
    c.save()
    print(f"  -> {out_path}")


# ── Main ─────────────────────────────────────────────────────────────────────


def main():
    register_fonts_v2()

    repo_root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    out_dir = os.path.join(repo_root, "samples", "intakes")
    ensure_dir(out_dir)

    print("Generating blank v2 template PDFs...")
    gen_general(os.path.join(out_dir, "blank-general-assistance.pdf"))
    gen_housing(os.path.join(out_dir, "blank-housing-stability.pdf"))
    gen_behavioral(os.path.join(out_dir, "blank-behavioral-health.pdf"))
    gen_soap(os.path.join(out_dir, "blank-soap-note.pdf"))
    print("Done.")


if __name__ == "__main__":
    main()
