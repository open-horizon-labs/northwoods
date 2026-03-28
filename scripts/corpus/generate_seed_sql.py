#!/usr/bin/env python3
"""
Generate infra/postgres/seed_corpus.sql from the people roster and narrative arc.

Run: python3 scripts/corpus/generate_seed_sql.py

Produces a SQL file that seeds:
  - documents (one per form generated)
  - extracted_fields (realistic field values per form type, per narrative era)
  - case_profiles (search text for embedding; embeddings generated at runtime by worker)

Template IDs match init.sql:
  general-assistance, housing-stability, behavioral-health, soap-note

Two cohorts:
  v2 era (June-Sep 2025): older county form, troubled circumstances
  v1 era (Dec 2025-Mar 2026): new agency form, some progression

Facility transfers: P017, P018 move sunrise->lakewood; P037, P038 move lakewood->sunrise.
V2-only: P003, P007, P023, P027, P031 (discharged/lost to follow-up before form changeover).
V1-only: P004, P009, P020, P024, P032 (new intakes after form changeover).
"""
from __future__ import annotations
import sys, os, uuid
from datetime import date
sys.path.insert(0, os.path.dirname(__file__))
from people import PEOPLE
from narrative import (
    V2_ONLY, V1_ONLY, FACILITY_TRANSFERS,
    v2_tenant, v1_tenant, v2_dates, v1_dates,
    general_state, housing_state, behavioral_state, soap_progression,
)

OUT = os.path.join(os.path.dirname(__file__), "../../infra/postgres/seed_corpus.sql")

TENANT_IDS = {"sunrise": "tenant-a", "lakewood": "tenant-b"}

FIELD_SCHEMAS = {
    "general-assistance": ["applicantName", "dateOfBirth", "address", "householdSize", "monthlyIncome", "reasonForAssistance"],
    "housing-stability":  ["applicantName", "dateOfBirth", "housingType", "monthlyRent", "monthsBehind", "evictionNotice", "reasonForInstability"],
    "behavioral-health":  ["clientName", "dateOfBirth", "presentingConcern", "currentMedications", "substanceUse", "traumaHistory", "suicidalIdeation"],
    "soap-note":          ["clientName", "sessionNumber", "subjective", "objective", "assessment", "plan", "riskLevel"],
}

def esc(v) -> str:
    if v is None: return "NULL"
    s = str(v).replace("\x00","").replace("'","''").replace("\\","\\\\")
    return f"'{s}'"

def stable_uuid(seed: str) -> str:
    return str(uuid.uuid5(uuid.NAMESPACE_DNS, f"northwoods-corpus-{seed}"))

def person_key(p: dict) -> str:
    return f"{p['last'].lower()}-{p['dob']}"

def build_rows(person: dict, era: str, visit_num: int, vdate: date,
               template_id: str, tenant: str) -> tuple[str, str, str]:
    """Return (doc_row, fields_rows, profile_row) SQL."""
    doc_id = stable_uuid(f"{person['id']}-{era}-{visit_num}-{template_id}")
    tenant_id = TENANT_IDS.get(tenant, tenant)  # normalize sunrise->tenant-a etc.
    file_key = f"{tenant_id}/seed/{person['id']}_{visit_num:02d}_{era}_{template_id}.pdf"
    file_name = f"{person['id']}_{visit_num:02d}_{era}_{template_id}.pdf"
    pkey = person_key(person)
    tenant = tenant_id  # use normalized ID throughout

    doc_sql = (
        f"  ({esc(doc_id)}, {esc(tenant)}, {esc(template_id)}, "
        f"(SELECT id FROM users WHERE tenant_id = {esc(tenant)} AND role = 'IntakeWorker' LIMIT 1), "
        f"{esc(file_key)}, {esc(file_name)}, 'finalized')"
    )

    # Build field values
    fields: dict[str,str] = {}
    full_name = f"{person['first']} {person['last']}"
    dob_str = date.fromisoformat(person["dob"]).strftime("%m/%d/%Y")

    if template_id == "general-assistance":
        s = general_state(person["id"], era, visit_num)
        fields = {
            "applicantName": full_name,
            "dateOfBirth": dob_str,
            "address": f"{person['addr']}, {person['city']}, {person['state']} {person['zip']}",
            "householdSize": str(s["household"]),
            "monthlyIncome": f"${s['income']}",
            "reasonForAssistance": s["reason"],
        }
        profile_text = (
            f"{full_name} DOB:{dob_str} "
            f"housing:{s['housing']} income:${s['income']} "
            f"household:{s['household']} {s['reason']}"
        )

    elif template_id == "housing-stability":
        s = housing_state(person["id"], era, visit_num)
        fields = {
            "applicantName": full_name,
            "dateOfBirth": dob_str,
            "housingType": s["htype"],
            "monthlyRent": f"${s['rent']}",
            "monthsBehind": str(s["behind"]),
            "evictionNotice": s["eviction"],
            "reasonForInstability": s["reason"],
        }
        profile_text = (
            f"{full_name} DOB:{dob_str} "
            f"housing:{s['htype']} rent:${s['rent']} behind:{s['behind']}mo "
            f"eviction:{s['eviction']} {s['reason']}"
        )

    elif template_id == "behavioral-health":
        s = behavioral_state(person["id"], era, visit_num)
        fields = {
            "clientName": full_name,
            "dateOfBirth": dob_str,
            "presentingConcern": s["concern"],
            "currentMedications": s["med"],
            "substanceUse": s["substance"],
            "traumaHistory": s["trauma"],
            "suicidalIdeation": s["si"],
        }
        profile_text = (
            f"{full_name} DOB:{dob_str} "
            f"concern:{s['concern']} medications:{s['med']} "
            f"substance:{s['substance']} trauma:{s['trauma']} si:{s['si']}"
        )

    elif template_id == "soap-note":
        # Determine total sessions for this person across both eras
        from narrative import SOAP_PEOPLE
        total = SOAP_PEOPLE.get(person["id"], 1)
        s = soap_progression(person["id"], visit_num, total, era)
        p_text = " / ".join(s["p"]) if isinstance(s["p"], list) else s["p"]
        fields = {
            "clientName": full_name,
            "sessionNumber": str(visit_num),
            "subjective": s["s"],
            "objective": s["o"],
            "assessment": s["a"],
            "plan": p_text,
            "riskLevel": s["risk"],
        }
        profile_text = (
            f"{full_name} DOB:{dob_str} session:{visit_num} "
            f"S:{s['s'][:80]} A:{s['a']} risk:{s['risk']}"
        )
    else:
        return None, None, None

    # Field rows
    field_rows = []
    for key, val in fields.items():
        conf = _confidence(key, val, era)
        field_id = stable_uuid(f"{doc_id}-{key}")
        field_rows.append(
            f"  ({esc(field_id)}, {esc(doc_id)}, {esc(tenant)}, {esc(key)}, "
            f"{esc(val)}, NULL, {conf:.4f}, {str(conf < 0.75).lower()})"
        )
    fields_sql = ",\n".join(field_rows)

    # Profile row
    profile_sql = (
        f"  ({esc(doc_id)}, {esc(tenant)}, {esc(template_id)}, "
        f"{esc(full_name)}, {esc(dob_str)}, "
        f"{esc(person['addr'])}, {esc(profile_text)}, NULL)"
    )

    return doc_sql, fields_sql, profile_sql


def _confidence(field_key: str, value: str, era: str) -> float:
    """
    Assign realistic confidence:
    - Structured fields (name, DOB) get high confidence in v1, moderate in v2
    - Narrative fields (reason, concern) get lower confidence
    - Era: v2 forms are older and harder to read => slightly lower overall
    """
    high_conf_fields = {"applicantName","clientName","dateOfBirth","sessionNumber"}
    low_conf_fields  = {"reasonForAssistance","reasonForInstability","presentingConcern",
                        "subjective","objective","assessment","plan"}
    import random, hashlib
    seed = int(hashlib.md5(f"{field_key}{value}{era}".encode()).hexdigest(),16)
    rng = random.Random(seed)

    if field_key in high_conf_fields:
        base = rng.uniform(0.88, 0.98) if era == "v1" else rng.uniform(0.78, 0.92)
    elif field_key in low_conf_fields:
        base = rng.uniform(0.55, 0.78) if era == "v1" else rng.uniform(0.42, 0.68)
    else:
        base = rng.uniform(0.70, 0.90) if era == "v1" else rng.uniform(0.60, 0.82)

    # Blank values get low confidence
    if not value or value in ("None","$0","0"):
        base = min(base, 0.55)
    return round(base, 4)


def v2_forms(person: dict) -> list[tuple[str,str,str,date]]:
    """Returns list of (era, visit_num, template_id, date) for v2 cohort."""
    pid = person["id"]
    if pid in V1_ONLY:
        return []
    tenant = v2_tenant(pid, person["tenant"])
    visits = person["visits"]
    rows = []

    # General assistance: everyone in v2 gets at least 1
    n_gen = 3 if visits >= 10 else 2 if visits >= 4 else 1
    gen_dates = v2_dates(pid, n_gen)
    for i, d in enumerate(gen_dates, 1):
        rows.append(("v2", i, "general-assistance", d, tenant))

    # Housing: multi-visit people
    from narrative import HOUSING_PEOPLE_IDS
    n_house = HOUSING_PEOPLE_IDS.get(pid, 1 if visits >= 2 else 0)
    if n_house:
        house_dates = v2_dates(pid, n_house)
        for i, d in enumerate(house_dates, 1):
            rows.append(("v2", i, "housing-stability", d, tenant))

    # Behavioral: targeted subset
    from narrative import BEHAVIORAL_PEOPLE
    n_beh = BEHAVIORAL_PEOPLE.get(pid, 0)
    if n_beh:
        beh_dates = v2_dates(pid, n_beh)
        for i, d in enumerate(beh_dates, 1):
            rows.append(("v2", i, "behavioral-health", d, tenant))

    # SOAP: chronic cases only — split sessions across eras
    from narrative import SOAP_PEOPLE
    total_soap = SOAP_PEOPLE.get(pid, 0)
    if total_soap:
        n_v2 = (total_soap + 1) // 2  # first half in v2
        soap_dates = v2_dates(pid, n_v2)
        for i, d in enumerate(soap_dates, 1):
            rows.append(("v2", i, "soap-note", d, tenant))

    return rows


def v1_forms(person: dict) -> list[tuple[str,str,str,date]]:
    """Returns list of (era, visit_num, template_id, date) for v1 cohort."""
    pid = person["id"]
    if pid in V2_ONLY:
        return []
    tenant = v1_tenant(pid, person["tenant"])
    visits = person["visits"]
    rows = []

    # General assistance: v1-only people get 1; returning people get 1-2 more
    is_returning = pid not in V1_ONLY
    n_gen = 2 if visits >= 10 else 1
    # Offset visit_num to continue from v2 for returning people
    v2_gen_offset = (3 if visits >= 10 else 2 if visits >= 4 else 1) if is_returning else 0
    gen_dates = v1_dates(pid, n_gen)
    for i, d in enumerate(gen_dates, 1):
        rows.append(("v1", v2_gen_offset + i, "general-assistance", d, tenant))

    # Housing: returning multi-visit people get 1 more in v1
    from narrative import HOUSING_PEOPLE_IDS
    if pid in HOUSING_PEOPLE_IDS and is_returning:
        v2_house_offset = HOUSING_PEOPLE_IDS.get(pid, 0)
        house_dates = v1_dates(pid, 1)
        for i, d in enumerate(house_dates, 1):
            rows.append(("v1", v2_house_offset + i, "housing-stability", d, tenant))

    # Behavioral: returning behavioral people get 1 more
    from narrative import BEHAVIORAL_PEOPLE
    if pid in BEHAVIORAL_PEOPLE and is_returning:
        v2_beh_offset = BEHAVIORAL_PEOPLE.get(pid, 0)
        beh_dates = v1_dates(pid, 1)
        for i, d in enumerate(beh_dates, 1):
            rows.append(("v1", v2_beh_offset + i, "behavioral-health", d, tenant))

    # SOAP: second half of sessions in v1
    from narrative import SOAP_PEOPLE
    total_soap = SOAP_PEOPLE.get(pid, 0)
    if total_soap and is_returning:
        n_v2 = (total_soap + 1) // 2
        n_v1 = total_soap - n_v2
        if n_v1 > 0:
            soap_dates = v1_dates(pid, n_v1)
            for i, d in enumerate(soap_dates, 1):
                rows.append(("v1", n_v2 + i, "soap-note", d, tenant))

    return rows


def main():
    all_doc_rows = []
    all_field_rows = []
    all_profile_rows = []
    doc_count = 0
    field_count = 0

    for person in PEOPLE:
        all_forms = v2_forms(person) + v1_forms(person)
        for (era, visit_num, template_id, vdate, tenant) in all_forms:
            doc_sql, fields_sql, profile_sql = build_rows(
                person, era, visit_num, vdate, template_id, tenant
            )
            if doc_sql is None:
                continue
            all_doc_rows.append(doc_sql)
            if fields_sql:
                all_field_rows.append(fields_sql)
                field_count += len(fields_sql.split("\n"))
            all_profile_rows.append(profile_sql)
            doc_count += 1

    out_path = os.path.normpath(OUT)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)

    with open(out_path, "w") as f:
        f.write("-- ==========================================================================\n")
        f.write("-- Synthetic corpus seed — generated by scripts/corpus/generate_seed_sql.py\n")
        f.write("-- Two cohorts: v2 (Jun-Sep 2025, old form), v1 (Dec 2025-Mar 2026, new form)\n")
        f.write("-- Facility transfers: P017/P018 sunrise->lakewood; P037/P038 lakewood->sunrise\n")
        f.write("-- Re-run generator to regenerate. Embeddings populated at runtime by worker.\n")
        f.write("-- ==========================================================================\n\n")

        f.write("-- Documents\n")
        f.write("INSERT INTO documents\n")
        f.write("  (id, tenant_id, template_id, uploaded_by, original_file_key, original_file_name, status)\n")
        f.write("VALUES\n")
        f.write(",\n".join(all_doc_rows))
        f.write("\nON CONFLICT (id) DO NOTHING;\n\n")

        f.write("-- Extracted fields\n")
        f.write("INSERT INTO extracted_fields\n")
        f.write("  (id, document_id, tenant_id, field_key, extracted_value, corrected_value, confidence, requires_review)\n")
        f.write("VALUES\n")
        f.write(",\n".join(all_field_rows))
        f.write("\nON CONFLICT (id) DO NOTHING;\n\n")

        f.write("-- Case profiles (embeddings populated at runtime)\n")
        f.write("INSERT INTO case_profiles\n")
        f.write("  (document_id, tenant_id, template_id, applicant_name, date_of_birth, address, search_text, embedding)\n")
        f.write("VALUES\n")
        f.write(",\n".join(all_profile_rows))
        f.write("\nON CONFLICT (document_id) DO NOTHING;\n")

    print(f"Written: {out_path}")
    print(f"Documents: {doc_count}  |  Field sets: {len(all_field_rows)}  |  Profiles: {len(all_profile_rows)}")


if __name__ == "__main__":
    main()
