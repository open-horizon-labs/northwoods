#!/usr/bin/env python3
"""Generate SQL seed statements for synthetic corpus documents."""

from __future__ import annotations

import uuid
from pathlib import Path

from form_data import PEOPLE, generate_visits


TEMPLATE_FIELD_MAP: dict[str, dict[str, str]] = {
    "general-assistance": {
        "Applicant Name": "applicantName",
        "Date of Birth": "dateOfBirth",
        "Address": "address",
        "Household Size": "householdSize",
        "Monthly Income": "monthlyIncome",
        "Requested Services": "requestedServices",
    },
    "housing-stability": {
        "Applicant Name": "applicantName",
        "Date of Birth": "dateOfBirth",
        "Current Address": "currentAddress",
        "Household Members": "householdMembers",
        "Monthly Rent": "monthlyRent",
        "Eviction Risk Level": "evictionRiskLevel",
        "Requested Assistance": "requestedAssistance",
    },
    "financial-assistance": {
        "Applicant Name": "applicantName",
        "Date of Birth": "dateOfBirth",
        "Income Type": "incomeType",
        "Total Monthly Income": "totalMonthlyIncome",
        "Support Type Requested": "supportType",
        "Employment Status": "employmentStatus",
        "Case Notes": "caseNotes",
    },
    "clinical-soap-note": {
        "Patient Name": "patientName",
        "Visit Date": "visitDate",
        "Encounter Type": "encounterType",
        "Clinician": "clinicianName",
        "S (Subjective)": "subjective",
        "O (Objective)": "objective",
        "A (Assessment)": "assessment",
        "P (Plan)": "plan",
        "Emergency Contact": "emergencyContact",
    },
}

TENANT_IDS = {
    "sunrise": "tenant-a",
    "lakewood": "tenant-b",
}


def _sql_escape(value) -> str:
    if value is None:
        return "NULL"

    text = str(value)
    text = text.replace("\x00", "")
    text = text.replace("\\", "\\\\")
    text = text.replace("\n", "\\n").replace("\r", "\\r")
    text = text.replace("'", "''")
    return f"'{text}'"


def _lookup_person(person_id: str) -> dict[str, str]:
    for person in PEOPLE:
        if person["id"] == person_id:
            return person
    raise KeyError(f"Unknown person id: {person_id}")


def _stable_uuid(seed: str) -> str:
    return str(uuid.uuid5(uuid.NAMESPACE_DNS, seed))


def _to_scalar(value):
    if isinstance(value, list):
        return ", ".join(str(v) for v in value)
    return value


def _conf(seed: str) -> float:
    digest = int(uuid.uuid5(uuid.NAMESPACE_DNS, seed).hex[:8], 16)
    confidence = 0.90 + (digest % 80) / 1000.0
    return round(confidence, 3)


def _case_profile_fields(fields: dict[str, str]) -> tuple[str, str, str]:
    applicant = fields.get("Applicant Name") or fields.get("Patient Name") or ""
    dob = fields.get("Date of Birth") or fields.get("Visit Date") or ""
    address = fields.get("Address") or fields.get("Current Address") or ""
    return applicant, dob, address


def _search_text(fields: dict[str, str]) -> str:
    return " | ".join(f"{key}: {value}" for key, value in fields.items())


def build_seed_sql() -> str:
    visits = list(generate_visits())

    doc_rows: list[str] = []
    field_rows: list[str] = []
    profile_rows: list[str] = []

    for visit in visits:
        person = _lookup_person(visit["person"]["id"])
        tenant_key = person["tenant"]
        tenant_id = TENANT_IDS[tenant_key]
        template = visit["template"]
        fields = visit["form"]["fields"]

        sequence = visit["visit_idx"] + 1
        file_name = f"{person['id']}_{sequence:02d}_{template}.pdf"
        file_key = f"{tenant_key}/seed/{file_name}"
        document_id = _stable_uuid(f"northwoods-seed::{tenant_key}::{template}::{person['id']}::{sequence}")

        doc_rows.append(
            f"    ('{document_id}', '{tenant_id}', '{template}', "
            f"(SELECT id FROM users WHERE tenant_id = '{tenant_id}' AND role = 'IntakeWorker' LIMIT 1), "
            f"'{file_key}', '{file_name}', 'finalized')"
        )

        for source_key, mapped_key in TEMPLATE_FIELD_MAP[template].items():
            if source_key not in fields:
                continue

            value = _to_scalar(fields[source_key])
            confidence = _conf(f"{document_id}:{source_key}")
            field_rows.append(
                f"    ('{document_id}', '{tenant_id}', '{mapped_key}', "
                f"{_sql_escape(value)}, {confidence:.3f}, false)"
            )

        applicant_name, date_of_birth, address = _case_profile_fields(fields)
        profile_rows.append(
            f"    ('{document_id}', '{tenant_id}', '{template}', "
            f"{_sql_escape(applicant_name)}, {_sql_escape(date_of_birth)}, {_sql_escape(address)}, "
            f"{_sql_escape(_search_text(fields))})"
        )

    sql: list[str] = []
    sql.append("-- Seed synthetic corpus documents")
    sql.append("INSERT INTO documents (id, tenant_id, template_id, uploaded_by, original_file_key, original_file_name, status)")
    sql.append("VALUES")
    sql.append(",\n".join(doc_rows))
    sql.append("ON CONFLICT (id) DO NOTHING;")

    sql.append("")
    sql.append("INSERT INTO extracted_fields (document_id, tenant_id, field_key, extracted_value, confidence, requires_review)")
    sql.append("VALUES")
    sql.append(",\n".join(field_rows))
    sql.append("ON CONFLICT (document_id, field_key) DO NOTHING;")

    sql.append("")
    sql.append("INSERT INTO case_profiles (document_id, tenant_id, template_id, applicant_name, date_of_birth, address, search_text)")
    sql.append("VALUES")
    sql.append(",\n".join(profile_rows))
    sql.append("ON CONFLICT (document_id) DO NOTHING;")

    return "\n".join(sql) + "\n"


def main() -> None:
    output_path = Path(__file__).resolve().parents[2] / "infra/postgres/seed_corpus.sql"
    output_path = output_path.resolve()
    output_path.write_text(build_seed_sql(), encoding="utf-8")
    print(f"Wrote seed corpus SQL to {output_path}")


if __name__ == "__main__":
    main()
