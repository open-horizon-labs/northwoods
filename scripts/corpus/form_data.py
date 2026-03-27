"""
Generates realistic form field data for each person/visit combination.

Each person has a consistent identity but visit-specific details:
- Varying dates across 2-3 month windows
- Different form types per visit for multi-visit people
- Realistic narratives that evolve across visits for frequent flyers
"""

import hashlib
import random
from datetime import date, timedelta

from people import PEOPLE

# ---------- template IDs matching init.sql ----------
TEMPLATES = [
    "general-assistance",
    "housing-stability",
    "financial-assistance",
    "clinical-soap-note",
]

# ---------- Realistic content pools ----------

HOUSING_STATUSES = [
    "Renting", "Doubled up with family", "Shelter", "Own home",
    "Transitional housing", "Motel/hotel", "Unsheltered",
]

EVICTION_RISKS = ["None", "Low", "Moderate", "High", "Imminent"]

REASONS_FOR_ASSISTANCE = [
    "Behind on rent due to job loss",
    "Utility shutoff notice received",
    "Need help with food and clothing for children",
    "Recently separated, need emergency housing",
    "Medical bills causing financial hardship",
    "Lost job during facility closure",
    "Domestic violence situation, seeking safe housing",
    "Aging parent care costs",
    "Vehicle repair needed for employment",
    "Child care costs exceeding budget",
]

INCOME_TYPES = [
    "Employment (full-time)", "Employment (part-time)", "SSI/SSDI",
    "TANF", "Unemployment benefits", "No income", "Self-employment",
    "Pension/retirement", "Child support",
]

EMPLOYMENT_STATUSES = [
    "Employed full-time", "Employed part-time", "Unemployed - seeking",
    "Unemployed - not seeking", "Disabled", "Retired", "Student",
]

SUPPORT_TYPES = [
    "Emergency rent assistance", "Utility payment", "Food assistance",
    "Transportation voucher", "Job training referral", "Child care subsidy",
    "Medical bill assistance", "Clothing/household goods", "Legal aid referral",
]

ENCOUNTER_TYPES = [
    "Initial assessment", "Follow-up", "Crisis intervention",
    "Care coordination", "Discharge planning", "Medication review",
]

PRESENTING_CONCERNS = [
    "Anxiety and difficulty sleeping",
    "Depressed mood, loss of interest in activities",
    "Substance use relapse after 6 months sober",
    "Anger management difficulties at home",
    "Grief following loss of family member",
    "PTSD symptoms related to domestic violence",
    "Social isolation and withdrawal",
    "Panic attacks occurring 2-3 times per week",
    "Difficulty managing stress from housing instability",
    "Medication non-compliance due to side effects",
]

MEDICATIONS = [
    "Sertraline 50mg daily", "Bupropion 150mg daily",
    "Trazodone 50mg at bedtime", "Gabapentin 300mg TID",
    "Hydroxyzine 25mg PRN anxiety", "None currently",
    "Quetiapine 25mg at bedtime", "Fluoxetine 20mg daily",
    "Buspirone 10mg BID", "Naltrexone 50mg daily",
]

SOAP_SUBJECTIVE = [
    "Client reports feeling 'a little better this week.' Sleep improving with medication.",
    "Client states she has been arguing more with her partner. Feels overwhelmed.",
    "Reports 3 days of sobriety after brief relapse. Attending NA meetings again.",
    "Says he has been isolating. Missed two shifts at work. Appetite poor.",
    "Client tearful today. Reports anniversary of mother's death approaching.",
    "Describes panic attack at grocery store yesterday. Avoided leaving home today.",
    "States housing situation is stable for now. Less anxious about eviction.",
    "Reports improved mood since starting new medication. Some nausea noted.",
    "Client frustrated with job search. Feeling hopeless about future.",
    "Says she is sleeping better but still waking at 3am most nights.",
]

SOAP_OBJECTIVE = [
    "Alert and oriented x4. Affect congruent with mood. Good eye contact.",
    "Disheveled appearance. Flat affect. Minimal eye contact. Speech slow.",
    "Cooperative. Mildly anxious. Fidgeting with hands. Speech normal rate.",
    "Neat appearance. Tearful at times. Affect labile. Oriented x4.",
    "Guarded initially, warmed up. No SI/HI. Judgment fair.",
    "Agitated. Pacing during session. Speech pressured. Denies SI.",
    "Calm, cooperative. Affect bright. Good rapport. Weight stable.",
    "Drowsy appearance. Reports medication side effects. BAC negative.",
    "Appropriate dress. Affect restricted. Limited spontaneous speech.",
    "Well-groomed. Affect anxious but improving. PHQ-9 score: 12 (moderate).",
]

SOAP_ASSESSMENT = [
    "Generalized Anxiety Disorder, improving with medication and therapy.",
    "Major Depressive Disorder, recurrent, moderate. Partial response to SSRI.",
    "Alcohol Use Disorder, moderate severity. Early recovery, high relapse risk.",
    "Adjustment Disorder with mixed anxiety and depression. Housing stressor primary.",
    "PTSD, chronic. Hypervigilance and avoidance symptoms persist.",
    "Panic Disorder without agoraphobia. Frequency decreasing with treatment.",
    "Bereavement. Normal grief process complicated by pre-existing depression.",
    "Cannabis Use Disorder, mild. Co-occurring social anxiety.",
    "Persistent Depressive Disorder. Functional impairment at work.",
    "Insomnia Disorder, secondary to anxiety. Sleep hygiene education ongoing.",
]

SOAP_PLAN = [
    "Continue current medications. Follow up in 2 weeks. Refer to support group.",
    "Increase sertraline to 100mg. Schedule CBT session. Safety plan reviewed.",
    "Continue NA attendance. Random UDS next visit. Therapy weekly.",
    "Refer to housing case manager. Continue therapy biweekly. PRN hydroxyzine.",
    "EMDR session next week. Continue trauma-focused CBT. Medication stable.",
    "Teach grounding techniques. Exposure hierarchy in next session. Follow up 1 week.",
    "Grief support group referral. Continue individual therapy. Reassess in 4 weeks.",
    "Motivational interviewing ongoing. Consider psychiatric evaluation for anxiety.",
    "Vocational rehabilitation referral. Increase bupropion to 300mg. Follow up 2 weeks.",
    "Sleep diary for next 2 weeks. Trazodone 100mg trial. Relaxation exercises.",
]

CLINICIAN_NAMES = [
    "Dr. Sarah Chen, LCSW", "Mark Rodriguez, LMFT",
    "Dr. Amelia Barnes, PsyD", "James Whitfield, LCPC",
    "Dr. Priya Kapoor, MD", "Rebecca Torres, LCSW",
]

LANDLORD_NAMES = [
    "Springfield Property Mgmt", "Lakeview Realty", "Northside Housing LLC",
    "Prairie Land Associates", "Heritage Property Co", "Midwest Rentals Inc",
]

MARGINAL_NOTES = [
    "See attached", "Verified by phone 3/12",
    "Updated per client", "Needs follow-up",
    "Copy to case file", "Illegible - confirmed verbally",
    "", "", "", "", "", "",  # Most forms have no marginal notes
]

# ---------- visit schedule generation ----------

BASE_DATE = date(2025, 10, 1)  # Corpus visits span Oct 2025 - Jan 2026


def _rng(person_id: str, visit_idx: int) -> random.Random:
    """Deterministic RNG per person-visit for reproducibility."""
    seed = hashlib.md5(f"{person_id}-{visit_idx}".encode()).hexdigest()
    return random.Random(seed)


def _visit_date(person_id: str, visit_idx: int, total_visits: int) -> date:
    """Spread visits across the date window."""
    rng = _rng(person_id, visit_idx)
    window_days = 120  # ~4 months
    if total_visits == 1:
        offset = rng.randint(0, window_days)
    else:
        slot = (window_days // total_visits) * visit_idx
        offset = slot + rng.randint(0, 14)
    return BASE_DATE + timedelta(days=min(offset, window_days))


def _pick(pool: list, rng: random.Random) -> str:
    return rng.choice(pool)


def _pick_n(pool: list, n: int, rng: random.Random) -> list:
    return rng.sample(pool, min(n, len(pool)))


def _template_for_visit(person_id: str, visit_idx: int, total_visits: int) -> str:
    """Assign form type per visit. Single-visit: random. Multi-visit: spread across types."""
    rng = _rng(person_id, visit_idx)
    if total_visits == 1:
        return rng.choice(TEMPLATES)
    # Frequent flyers cycle through types, with SOAP notes for later visits
    pattern = TEMPLATES * 4  # enough to cover 10+ visits
    base_idx = int(hashlib.md5(person_id.encode()).hexdigest()[:4], 16) % len(TEMPLATES)
    return pattern[(base_idx + visit_idx) % len(TEMPLATES)]


def _monthly_income(rng: random.Random) -> str:
    amount = rng.randint(400, 5200)
    return f"${amount:,}"


def _monthly_rent(rng: random.Random) -> str:
    amount = rng.choice([450, 550, 650, 750, 850, 950, 1050, 1200, 1400, 1600])
    return f"${amount:,}"


# ---------- Form generators ----------

def _general_assistance(person: dict, visit_idx: int, vdate: date) -> dict:
    rng = _rng(person["id"], visit_idx)
    services = _pick_n(SUPPORT_TYPES, rng.randint(1, 3), rng)
    return {
        "template": "general-assistance",
        "title": "GENERAL ASSISTANCE INTAKE FORM",
        "fields": {
            "Applicant Name": f"{person['first']} {person['last']}",
            "Date of Birth": person["dob"],
            "SSN (last 4)": person["ssn4"],
            "Address": f"{person['addr']}, {person['city']}, {person['state']} {person['zip']}",
            "Phone": person["phone"],
            "Date": vdate.strftime("%m/%d/%Y"),
            "Household Size": str(rng.randint(1, 6)),
            "Monthly Income": _monthly_income(rng),
            "Housing Status": _pick(HOUSING_STATUSES, rng),
            "Reason for Assistance": _pick(REASONS_FOR_ASSISTANCE, rng),
            "Requested Services": ", ".join(services),
            "Notes": _pick(MARGINAL_NOTES, rng),
        },
    }


def _housing_stability(person: dict, visit_idx: int, vdate: date) -> dict:
    rng = _rng(person["id"], visit_idx)
    return {
        "template": "housing-stability",
        "title": "HOUSING STABILITY ASSESSMENT",
        "fields": {
            "Applicant Name": f"{person['first']} {person['last']}",
            "Date of Birth": person["dob"],
            "Assessment Date": vdate.strftime("%m/%d/%Y"),
            "Current Address": f"{person['addr']}, {person['city']}, {person['state']} {person['zip']}",
            "Phone": person["phone"],
            "Household Members": str(rng.randint(1, 5)),
            "Monthly Rent": _monthly_rent(rng),
            "Landlord/Property Manager": _pick(LANDLORD_NAMES, rng),
            "Lease Start Date": (vdate - timedelta(days=rng.randint(90, 730))).strftime("%m/%d/%Y"),
            "Months Behind on Rent": str(rng.choice([0, 0, 1, 1, 2, 2, 3, 4, 5])),
            "Eviction Notice Received": rng.choice(["Yes", "No", "No", "No"]),
            "Eviction Risk Level": _pick(EVICTION_RISKS, rng),
            "Housing History (past 2 years)": f"{rng.randint(1, 4)} addresses",
            "Requested Assistance": _pick(SUPPORT_TYPES[:3], rng),
            "Notes": _pick(MARGINAL_NOTES, rng),
        },
    }


def _financial_assistance(person: dict, visit_idx: int, vdate: date) -> dict:
    rng = _rng(person["id"], visit_idx)
    support = _pick_n(SUPPORT_TYPES, rng.randint(1, 3), rng)
    return {
        "template": "financial-assistance",
        "title": "FINANCIAL ASSISTANCE INTAKE",
        "fields": {
            "Applicant Name": f"{person['first']} {person['last']}",
            "Date of Birth": person["dob"],
            "SSN (last 4)": person["ssn4"],
            "Date": vdate.strftime("%m/%d/%Y"),
            "Address": f"{person['addr']}, {person['city']}, {person['state']} {person['zip']}",
            "Phone": person["phone"],
            "Income Type": _pick(INCOME_TYPES, rng),
            "Total Monthly Income": _monthly_income(rng),
            "Employment Status": _pick(EMPLOYMENT_STATUSES, rng),
            "Employer (if applicable)": rng.choice(["N/A", "Walmart", "McDonald's", "FedEx", "Self", "County Hospital", "School District"]),
            "Support Type Requested": ", ".join(support),
            "Case Notes": _pick(REASONS_FOR_ASSISTANCE, rng),
        },
    }


def _soap_note(person: dict, visit_idx: int, vdate: date) -> dict:
    rng = _rng(person["id"], visit_idx)
    return {
        "template": "clinical-soap-note",
        "title": "BEHAVIORAL HEALTH SOAP NOTE",
        "fields": {
            "Patient Name": f"{person['first']} {person['last']}",
            "Date of Birth": person["dob"],
            "Visit Date": vdate.strftime("%m/%d/%Y"),
            "Encounter Type": _pick(ENCOUNTER_TYPES, rng),
            "Clinician": _pick(CLINICIAN_NAMES, rng),
            "Presenting Concern": _pick(PRESENTING_CONCERNS, rng),
            "Current Medications": _pick(MEDICATIONS, rng),
            "S (Subjective)": _pick(SOAP_SUBJECTIVE, rng),
            "O (Objective)": _pick(SOAP_OBJECTIVE, rng),
            "A (Assessment)": _pick(SOAP_ASSESSMENT, rng),
            "P (Plan)": _pick(SOAP_PLAN, rng),
            "Emergency Contact": f"{rng.choice(['Spouse', 'Parent', 'Sibling', 'Friend'])} - {person['phone']}",
        },
    }


GENERATORS = {
    "general-assistance": _general_assistance,
    "housing-stability": _housing_stability,
    "financial-assistance": _financial_assistance,
    "clinical-soap-note": _soap_note,
}


def generate_visits() -> list[dict]:
    """
    Returns a list of visit records:
    {
        "person": dict,
        "visit_idx": int,
        "date": date,
        "template": str,
        "form": dict  # {template, title, fields}
    }
    """
    visits = []
    for person in PEOPLE:
        n = person["visits"]
        for vi in range(n):
            tmpl = _template_for_visit(person["id"], vi, n)
            vdate = _visit_date(person["id"], vi, n)
            gen = GENERATORS[tmpl]
            form = gen(person, vi, vdate)
            visits.append({
                "person": person,
                "visit_idx": vi,
                "date": vdate,
                "template": tmpl,
                "form": form,
            })
    return visits


if __name__ == "__main__":
    vlist = generate_visits()
    print(f"Total visits: {len(vlist)}")
    by_template = {}
    for v in vlist:
        by_template.setdefault(v["template"], []).append(v)
    for t, vs in sorted(by_template.items()):
        print(f"  {t}: {len(vs)}")
    by_person = {}
    for v in vlist:
        by_person.setdefault(v["person"]["id"], []).append(v)
    print(f"People: {len(by_person)}")
    multi = [pid for pid, vs in by_person.items() if len(vs) > 1]
    print(f"Multi-visit people: {len(multi)}")
