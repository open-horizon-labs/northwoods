"""
Narrative arc for the two-cohort corpus.

TIMELINE:
  v2 forms: older county template, 6-9 months ago (June-September 2025)
  v1 forms: new agency template, 0-3 months ago (December 2025-March 2026)
  Form changeover: ~October 2025 (new template rolled out)

FACILITY CONTINUITY:
  Most people stay at the same facility (same tenant).
  A few "transfer" people moved between facilities — they appear in v2 under
  one tenant and in v1 under the other. Their tenant_id changes between cohorts.

  Transfers (sunrise -> lakewood):
    P017 Carlton Hughes: moved to Lakewood for work, transferred care
    P018 Sonya Blackwood: relocated to Lakewood area after housing crisis resolved

  Transfers (lakewood -> sunrise):
    P037 Bernard Oduya: moved to Springfield for family support
    P038 Tasha Greenfield: relocated to Springfield after shelter placement

V2-ONLY people (no v1 forms — either discharged or lost to follow-up):
  P003 Jesse Huang: completed program, discharged
  P007 Anton Petrov: moved out of state
  P023 Victor Pham: lost to follow-up
  P027 Dmitri Volkov: incarcerated
  P031 Calvin Pope: transferred to different program (not in system)

V1-ONLY people (new intakes after form changeover — not in v2):
  P004 Brianna Kowalski: new intake Dec 2025
  P009 Rodrigo Vega: new intake Jan 2026
  P020 Ingrid Larsson: new intake Feb 2026
  P024 Monique Dupont: new intake Jan 2026
  P032 Esperanza Cruz: new intake Dec 2025

NARRATIVE ARCS (what changes between v2 and v1 cohort):

Raymond Castillo (P019) — sunrise frequent flyer, 12 visits total
  v2 era (Jun-Sep 2025): Homeless, $0 income, substance use (alcohol), high housing instability,
    eviction notices, crisis SOAP sessions, suicidal ideation at peak
  v1 era (Dec-Mar 2026): In room rental, part-time income $650/mo, substance use in remission,
    housing more stable though still fragile, SOAP sessions showing progress, risk moderate->low

Gloria Navarro (P039) — lakewood frequent flyer, 11 visits total
  v2 era: Staying with family (overcrowded), financial assistance, behavioral health crisis,
    domestic conflict, high anxiety, medications started
  v1 era: Moved to apartment (voucher came through), income from part-time cleaning work,
    behavioral health stabilizing, still attending sessions, risk low-moderate

Carlton Hughes (P017) — transfers sunrise->lakewood
  v2 era (sunrise): Housing instability, job loss, 3 months behind rent, eviction notice
  v1 era (lakewood): Got new job in Lakewood area, moved — housing stable, follow-up general intake only

Sonya Blackwood (P018) — transfers sunrise->lakewood
  v2 era (sunrise): Fleeing domestic situation, no income, behavioral health intake
  v1 era (lakewood): Relocated, housing voucher, working part-time, emotional support continuing

Bernard Oduya (P037) — transfers lakewood->sunrise
  v2 era (lakewood): Elderly, fixed income, housing instability (landlord selling property),
    SOAP notes for depression
  v1 era (sunrise): Moved to Springfield to live near son, housing stable, still attending for depression

Tasha Greenfield (P038) — transfers lakewood->sunrise
  v2 era (lakewood): Young adult, substance use issue, housing crisis, first behavioral health intake
  v1 era (sunrise): In sober living in Springfield, working, occasional SOAP follow-up

Elijah Santos (P015) — 3 visits, all v1 but behavioral health arc
  Escalating anxiety, stabilizing by visit 3

Nadia Thornton (P014) — 2 visits spanning cohorts
  v2 era: Initial general assistance (job loss)
  v1 era: Follow-up, secured part-time work, housing stable

Tobias Stern (P033) — lakewood, 2 visits spanning cohorts
  v2 era: Housing assessment, room rental at risk
  v1 era: Still in room rental, paying on time, closed case

Alicia Monroe (P034) — lakewood, 3 visits, behavioral health focus
  v2: Depression intake, first time seeking help
  v1: Two follow-up SOAP notes, improving
"""

from datetime import date, timedelta
import random

# Date ranges
V2_START = date(2025, 6, 1)
V2_END   = date(2025, 9, 30)
V1_START = date(2025, 12, 1)
V1_END   = date(2026, 3, 27)

# People who transfer facilities between cohorts
# Maps person_id -> (v2_tenant, v1_tenant)
FACILITY_TRANSFERS = {
    "P017": ("sunrise", "lakewood"),
    "P018": ("sunrise", "lakewood"),
    "P037": ("lakewood", "sunrise"),
    "P038": ("lakewood", "sunrise"),
}

# People only in v2 (no v1 forms)
V2_ONLY = {"P003", "P007", "P023", "P027", "P031"}

# People only in v1 (no v2 forms)
V1_ONLY = {"P004", "P009", "P020", "P024", "P032"}

def v2_tenant(person_id, default_tenant):
    if person_id in FACILITY_TRANSFERS:
        return FACILITY_TRANSFERS[person_id][0]
    return default_tenant

def v1_tenant(person_id, default_tenant):
    if person_id in FACILITY_TRANSFERS:
        return FACILITY_TRANSFERS[person_id][1]
    return default_tenant

def spread_dates(n, start, end):
    """Space n dates evenly between start and end with ±3 day jitter."""
    if n == 1:
        mid = start + timedelta(days=(end - start).days // 2 + random.randint(-5, 5))
        return [max(start, min(end, mid))]
    dates = []
    span = (end - start).days
    for i in range(n):
        base = start + timedelta(days=int(span * i / (n - 1)))
        jitter = random.randint(-3, 3)
        d = max(start, min(end, base + timedelta(days=jitter)))
        dates.append(d)
    return sorted(set(dates))[:n]

def v2_dates(person_id, n):
    return spread_dates(n, V2_START, V2_END)

def v1_dates(person_id, n):
    return spread_dates(n, V1_START, V1_END)

# ── Narrative state per person ────────────────────────────────────────────────
# Returns a dict of field values for a given person in a given era.
# era = "v2" (older, troubled) or "v1" (more recent, some progression)

def general_state(person_id, era, visit_num=1):
    """Return field values for general assistance intake."""
    # Base states seeded per person_id for consistency
    import hashlib
    seed = int(hashlib.md5(f"{person_id}{era}{visit_num}".encode()).hexdigest(), 16)
    rng = random.Random(seed)

    if person_id == "P019":
        if era == "v2":
            return dict(housing="Homeless", income=0, household=1,
                reason="Lost job 4 months ago. Currently sleeping at shelter. Rent arrears from prior unit still owed.",
                income_changed=False)
        else:
            return dict(housing="Room rental", income=650, household=1,
                reason="Secured part-time work at warehouse. Need help with first/last month rent deposit.",
                income_changed=True)

    if person_id == "P039":
        if era == "v2":
            return dict(housing="With family", income=0, household=5,
                reason="Staying with sister — overcrowded household of 5. No income. Need help paying for childcare to work.",
                income_changed=False)
        else:
            return dict(housing="Renting", income=780, household=2,
                reason="Moved to own apartment via Section 8 voucher. Part-time cleaning work. Need help with utility setup.",
                income_changed=False)

    if person_id == "P017":
        if era == "v2":  # sunrise
            return dict(housing="Renting", income=0, household=2,
                reason="Laid off 2 months ago. Behind 3 months on rent. Received pay-or-quit notice.",
                income_changed=False)
        else:  # lakewood — transferred, new job
            return dict(housing="Renting", income=2200, household=2,
                reason="Relocated for new employment. Need one-time assistance with moving deposit.",
                income_changed=False)

    if person_id == "P018":
        if era == "v2":  # sunrise
            return dict(housing="Homeless", income=0, household=1,
                reason="Left unsafe home situation. Staying at shelter. No income. Seeking emergency housing assistance.",
                income_changed=False)
        else:  # lakewood — relocated, stabilizing
            return dict(housing="Renting", income=980, household=1,
                reason="Relocated to Lakewood. Housing voucher approved. Need help with food costs while establishing.",
                income_changed=False)

    if person_id == "P037":
        if era == "v2":  # lakewood
            return dict(housing="Renting", income=1100, household=1,
                reason="Landlord selling property, given 60-day notice. Fixed income. Limited options in current area.",
                income_changed=False)
        else:  # sunrise — moved near son
            return dict(housing="With family", income=1100, household=3,
                reason="Moved to Springfield to live with son. Household adjusting. Need help with medication costs.",
                income_changed=False)

    if person_id == "P038":
        if era == "v2":  # lakewood
            return dict(housing="Homeless", income=0, household=1,
                reason="Substance use program ended, no transition housing available. Staying in car.",
                income_changed=False)
        else:  # sunrise — sober living
            return dict(housing="Renting", income=520, household=1,
                reason="In sober living house in Springfield. Working part-time. Need help with food and bus pass.",
                income_changed=False)

    if person_id == "P014":
        if era == "v2":
            return dict(housing="Renting", income=0, household=2,
                reason="Laid off from retail position. Unemployment pending. Need help with rent for this month.",
                income_changed=False)
        else:
            return dict(housing="Renting", income=1450, household=2,
                reason="Follow-up — secured part-time admin work. Requesting utility assistance while building savings.",
                income_changed=False)

    if person_id == "P033":
        if era == "v2":
            return dict(housing="Room rental", income=780, household=1,
                reason="Income reduced after hours cut. Behind 1 month on room rental. Requesting bridge assistance.",
                income_changed=False)
        else:
            return dict(housing="Room rental", income=1050, household=1,
                reason="Follow-up intake. Back to full hours, payments current. Requesting case closure documentation.",
                income_changed=False)

    # Generic progression for everyone else
    base_incomes_v2 = [0, 0, 520, 780, 980]
    base_incomes_v1 = [780, 980, 1200, 1450, 1800]
    housing_v2 = rng.choices(["Renting", "With family", "Homeless", "Room rental"], weights=[30,30,25,15])[0]
    housing_v1 = rng.choices(["Renting", "Room rental", "With family"], weights=[55,30,15])[0]
    reasons_v2 = [
        "Unexpected job loss. Behind on rent. Need bridge assistance.",
        "Medical expenses depleted savings. Requesting utility and food help.",
        "Evicted from prior unit. Staying with relatives temporarily.",
        "Hours reduced at work. Cannot cover rent and childcare.",
        "Recently separated. Adjusting to single income.",
    ]
    reasons_v1 = [
        "Follow-up intake. Situation stabilizing but still need support.",
        "New part-time work. Requesting help with transportation costs.",
        "Housing secured. Need one-time assistance with utility deposit.",
        "Income improving. Requesting case review and service adjustment.",
        "Returning client. Seasonal work gap. Short-term bridge needed.",
    ]
    if era == "v2":
        return dict(housing=housing_v2, income=rng.choice(base_incomes_v2),
            household=rng.randint(1,4), reason=rng.choice(reasons_v2), income_changed=False)
    else:
        return dict(housing=housing_v1, income=rng.choice(base_incomes_v1),
            household=rng.randint(1,3), reason=rng.choice(reasons_v1), income_changed=True)


def housing_state(person_id, era, visit_num=1):
    import hashlib
    seed = int(hashlib.md5(f"h{person_id}{era}{visit_num}".encode()).hexdigest(), 16)
    rng = random.Random(seed)

    if person_id == "P019":
        if era == "v2":
            return dict(htype="Shelter", rent=0, behind=0, eviction="No",
                section8="No", months_there=2, reason="Lost apartment after job loss. In emergency shelter.",
                instability_trend="worsening")
        else:
            return dict(htype="Room rental", rent=550, behind=0, eviction="No",
                section8="No", months_there=3, reason="Moved from shelter to room rental. Stable for now.",
                instability_trend="improving")

    if person_id == "P039":
        if era == "v2":
            return dict(htype="Apartment", rent=850, behind=2, eviction="Yes",
                section8="No", months_there=14, reason="Behind 2 months. Domestic conflict affecting ability to work.",
                instability_trend="worsening")
        else:
            return dict(htype="Apartment", rent=750, behind=0, eviction="No",
                section8="Yes", months_there=4, reason="New apartment via voucher program. Stable.",
                instability_trend="stable")

    if person_id == "P017":
        if era == "v2":
            return dict(htype="Apartment", rent=900, behind=3, eviction="Yes",
                section8="No", months_there=24, reason="Job loss. Pay-or-quit notice received.",
                instability_trend="worsening")
        else:
            return dict(htype="Apartment", rent=950, behind=0, eviction="No",
                section8="No", months_there=1, reason="New apartment near new job in Lakewood.",
                instability_trend="stable")

    if person_id == "P037":
        if era == "v2":
            return dict(htype="Apartment", rent=800, behind=0, eviction="Yes",
                section8="No", months_there=60, reason="No-fault eviction — landlord selling.",
                instability_trend="worsening")
        else:
            return dict(htype="House", rent=0, behind=0, eviction="No",
                section8="No", months_there=2, reason="Living with son in Springfield. No rent cost.",
                instability_trend="stable")

    # Generic
    htype_v2 = rng.choices(["Apartment","Room rental","Shelter","With family"], weights=[35,25,20,20])[0]
    htype_v1 = rng.choices(["Apartment","Room rental","House","With family"], weights=[45,30,15,10])[0]
    behind_v2 = rng.randint(1,4)
    behind_v1 = rng.randint(0,1)
    eviction_v2 = "Yes" if behind_v2 >= 3 else "No"
    eviction_v1 = "No"
    reasons_v2 = ["Income reduced, falling behind.", "Eviction risk — need rapid rehousing.", "Unsafe conditions."]
    reasons_v1 = ["Follow-up — situation improving.", "Stable housing maintained.", "New placement working well."]
    return dict(htype=htype_v1 if era=="v1" else htype_v2,
        rent=rng.choice([600,750,850] if era=="v1" else [0,550,750]),
        behind=behind_v1 if era=="v1" else behind_v2,
        eviction=eviction_v1 if era=="v1" else eviction_v2,
        section8="Yes" if rng.random()<0.2 else "No",
        months_there=rng.randint(3,24),
        reason=rng.choice(reasons_v1 if era=="v1" else reasons_v2),
        instability_trend="improving" if era=="v1" else "worsening")


def behavioral_state(person_id, era, visit_num=1):
    import hashlib
    seed = int(hashlib.md5(f"b{person_id}{era}{visit_num}".encode()).hexdigest(), 16)
    rng = random.Random(seed)

    if person_id == "P019":
        if era == "v2":
            concerns = ["Severe depression, not leaving shelter. Reports passive SI.", "Alcohol use daily. Not sleeping.", "Panic attacks, unable to work."]
            meds = ["None", "None", "Prescribed Sertraline 50mg — not taking consistently"]
            substance = "Alcohol"
        else:
            concerns = ["Mood improved since housing stabilized. Still anxious.", "Alcohol use reduced significantly — occasional weekend.", "Sleeping better, attending AA."]
            meds = ["Sertraline 50mg daily", "Sertraline 50mg daily", "Sertraline 50mg daily — taking consistently"]
            substance = "Alcohol"
    elif person_id == "P039":
        if era == "v2":
            concerns = ["Overwhelmed by domestic conflict and financial stress.", "Panic attacks at home. Partner controlling finances.", "Depressed mood, difficulty caring for children."]
            meds = ["None", "None", "Started Buspirone 15mg — 2 weeks ago"]
            substance = "None"
        else:
            concerns = ["Mood more stable since separation and new housing.", "Anxiety reduced. Using coping skills from therapy.", "Continuing to process domestic trauma. Progress noted."]
            meds = ["Buspirone 15mg BID", "Buspirone 15mg BID", "Buspirone 15mg BID — tolerating well"]
            substance = "None"
    else:
        concerns_v2 = ["Persistent anxiety and low mood following life stressor.",
                        "Difficulty functioning. First time seeking mental health support.",
                        "Sleep disruption, appetite changes, isolating from support network."]
        concerns_v1 = ["Mood improving with treatment. Some residual anxiety.",
                        "Responding well to CBT. Engagement improving.",
                        "Functional improvement noted. Working toward goals."]
        meds_v2 = ["None", "None", rng.choice(["Sertraline 50mg","Trazodone 100mg QHS","Buspirone 15mg"])]
        meds_v1 = [rng.choice(["Sertraline 50mg","Buspirone 15mg BID"]),
                   rng.choice(["Sertraline 50mg","Fluoxetine 20mg"]),
                   rng.choice(["Sertraline 50mg — well tolerated","Fluoxetine 20mg — dose stable"])]
        concerns = concerns_v2 if era == "v2" else concerns_v1
        meds = meds_v2 if era == "v2" else meds_v1
        substance = rng.choice(["None","Alcohol","Cannabis"]) if era=="v2" else rng.choice(["None","None","Alcohol"])

    idx = min(visit_num - 1, len(concerns) - 1)
    trauma = "Yes" if era == "v2" else rng.choice(["Yes","No"])
    si = "Yes" if (person_id == "P019" and era == "v2" and visit_num == 2) else "No"
    return dict(concern=concerns[idx], med=meds[idx], substance=substance,
                trauma=trauma, si=si)


def soap_progression(person_id, session_num, total_sessions, era):
    """Return S/O/A/P/risk content that shows arc across sessions."""
    import hashlib
    seed = int(hashlib.md5(f"s{person_id}{session_num}{era}".encode()).hexdigest(), 16)
    rng = random.Random(seed)

    progress_ratio = session_num / total_sessions  # 0.0 = first session, 1.0 = last

    if person_id == "P019":
        s_arc = [
            "Sitting in back of room, would not make eye contact. States 'I don't see the point.' Denies active plan.",
            "Slightly more engaged today. Reports sleeping at shelter. Still drinking nightly. No SI.",
            "Attended AA twice this week. 'First time in years I made it two days sober.' Cautiously hopeful.",
            "Secured room rental. Mood noticeably improved. Drinking 2-3x/week. Working part-time.",
            "Three weeks sober. 'I actually feel like myself.' Back at warehouse job. Housing stable.",
        ]
        o_arc = [
            "Disheveled, malodorous. Affect flat. Eye contact poor. Thought process coherent. Risk assessed.",
            "Better groomed. Affect slightly brighter. Tremor noted — alcohol withdrawal possible.",
            "Alert, engaged. Good eye contact. Affect appropriate. Speech organized and goal-directed.",
            "Well-dressed. Animated. Affect full range. Laughed once. Significant change from initial.",
            "Confident presentation. Affect bright. No signs of intoxication. Sobriety markers present.",
        ]
        risk_arc = ["High", "High", "Moderate", "Moderate", "Low"]
    elif person_id == "P039":
        s_arc = [
            "Reports partner monitors her phone and finances. 'I can't do anything without him knowing.' Afraid.",
            "Moved to sister's house after incident last week. Scared but relieved. Children with her.",
            "Submitted housing voucher application. 'I'm doing this.' Mood improved. Still anxious.",
            "Apartment approved. Moving this weekend. Overwhelmed but positive. Children adjusting.",
            "In new apartment 3 weeks. Feels safe. Attending parenting support group. Anxious about finances.",
        ]
        o_arc = [
            "Hypervigilant. Checked door twice. Whispered at times. Affect fearful. Bruising noted on forearm.",
            "More relaxed than prior session. Tearful when discussing children. Affect appropriate.",
            "Hopeful affect. Engaged throughout. Organized thought process. Plans concrete and realistic.",
            "Tired but positive. Good eye contact. Affect congruent. Strong motivation evident.",
            "Calm and grounded. Well-groomed. Affect bright. Good insight. Therapeutic alliance strong.",
        ]
        risk_arc = ["High", "Moderate", "Moderate", "Low", "Low"]
    else:
        # Generic arc based on progress_ratio
        if progress_ratio < 0.33:
            s = rng.choice(["Client distressed. Difficulty articulating concerns. Affect constricted.",
                             "Reports overwhelming stress. Denies SI. Support system minimal."])
            o = rng.choice(["Anxious presentation. Fidgeting. Avoidant eye contact.",
                             "Guarded. Slow to warm. Affect flat but cooperative."])
            risk = rng.choice(["Moderate", "Moderate", "Low"])
        elif progress_ratio < 0.66:
            s = rng.choice(["Some improvement noted. Still struggling but more hopeful.",
                             "Using coping tools. Occasional setbacks. Mood variable."])
            o = rng.choice(["More engaged than prior sessions. Affect appropriate.",
                             "Calm, cooperative. Eye contact improved. Speech organized."])
            risk = "Low"
        else:
            s = rng.choice(["Client reports significant improvement. Goals largely met.",
                             "Stable mood. Functional in all domains. Discussing discharge."])
            o = rng.choice(["Confident, well-groomed. Affect bright. Strong insight.",
                             "Engaged and motivated. Thought process organized. Progress evident."])
            risk = "Low"

        a_opts = ["Adjustment disorder w/ mixed anxiety and depressed mood (F43.23).",
                  "Major depressive episode, moderate (F32.1). Responding to treatment.",
                  "PTSD, chronic (F43.10). Avoidance and hypervigilance.",
                  "Generalized anxiety disorder (F41.1). Functional improvement noted."]
        p_opts = [["Continue weekly CBT.", "Assign thought record worksheet.", "Safety plan reviewed."],
                  ["Increase to 2x/week given recent stressor.", "Coordinate with case manager.", "Safety plan updated."],
                  ["Step down to biweekly — client stable.", "PCP referral for medication.", "Peer support referral."],
                  ["Working toward discharge goals.", "Continue skill consolidation.", "Schedule follow-up in 30 days."]]
        return dict(s=s, o=o, a=rng.choice(a_opts), p=rng.choice(p_opts), risk=risk)

    idx = min(session_num - 1, len(s_arc) - 1)
    a_dx = {
        "P019": "Alcohol use disorder, severe (F10.20). Major depressive episode (F32.1).",
        "P039": "PTSD, acute (F43.10). Adjustment disorder secondary to domestic violence (F43.20).",
    }.get(person_id, "Adjustment disorder w/ depressed mood (F43.21).")
    p_progression = [
        ["Crisis stabilization. Safety plan established.", "Referral to emergency shelter.", "Daily check-in scheduled."],
        ["Continue crisis support.", "Substance use assessment referral.", "Increase session frequency to 2x/week."],
        ["Introduce CBT framework.", "Coping skills homework assigned.", "Coordinate with case manager re: housing."],
        ["Consolidate gains.", "Transition to standard weekly sessions.", "Discuss vocational goals."],
        ["Maintenance phase.", "Discuss step-down to biweekly.", "Celebrate progress and reinforce stability."],
    ]
    pidx = min(session_num - 1, len(p_progression) - 1)
    return dict(s=s_arc[idx], o=o_arc[idx], a=a_dx,
                p=p_progression[pidx], risk=risk_arc[idx])

# Exported constants referenced by generate_seed_sql.py
HOUSING_PEOPLE_IDS = {
    "P014": 2, "P015": 2, "P016": 2,
    "P017": 3, "P018": 2, "P019": 3,
    "P033": 2, "P034": 2, "P035": 2,
    "P036": 1, "P037": 3, "P038": 2, "P039": 3,
}

BEHAVIORAL_PEOPLE = {
    "P019": 3, "P039": 3,
    "P015": 2, "P016": 2, "P017": 2, "P018": 2,
    "P034": 2, "P035": 2, "P037": 2, "P038": 2,
    "P005": 1, "P006": 1, "P008": 1,
    "P022": 1, "P026": 1, "P028": 1,
}

SOAP_PEOPLE = {
    "P019": 5, "P039": 5,
    "P017": 2, "P018": 1,
    "P037": 2, "P038": 1,
    "P015": 1, "P034": 1,
}
