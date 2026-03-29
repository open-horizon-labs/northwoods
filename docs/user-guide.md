# Northwoods User Guide

This guide covers the three roles in the system: Intake Worker, Reviewer, and Admin.

---

## Signing In

Go to the application URL and enter your email and password. A popup on the login page can pre-fill demo credentials for convenience.

After signing in, you land on your role's dashboard automatically.

---

## Intake Worker

**Your job:** Upload scanned intake forms so the system can extract and digitize the information.

### Submitting a form

1. Select the form type (e.g., "General Assistance Intake") from the dropdown.
2. Attach the scanned PDF or photo. One file per submission.
3. Click **Submit**. The form appears in your submissions list with a live status badge.

### Status lifecycle

| Status | Meaning |
|--------|---------|
| Queued | Received, waiting to be processed |
| Processing | AI is reading the form and extracting fields |
| Ready for Review | Extraction done, waiting for a reviewer |
| Auto-Accepted | All fields high confidence, may still be reviewed |
| Finalized | Reviewer confirmed, intake complete |
| Failed | Processing error -- contact your supervisor |

Once a submission reaches "Ready for Review," your part is done.

### Blank forms

Blank printable PDFs are available for each form type. Download and print them to hand to clients.

---

## Reviewer

**Your job:** Verify extracted fields against the original document, correct mistakes, and finalize records.

### The review workflow

1. **Pick a document** from the review queue. Items needing the most attention are highlighted.
2. **Compare** the original scanned form (PDF viewer) with the extracted fields. Fields are color-coded by confidence:
   - **Green (90%+)** -- almost certainly correct
   - **Blue (80-89%)** -- worth a glance
   - **Amber (65-79%)** -- compare carefully
   - **Red (<65%)** -- must check against the original
3. **Correct** any wrong values by editing the field directly.
4. **Review similar cases** shown below the fields -- these are historical intakes with similar applicant information, surfaced by the RAG pipeline. They help you spot patterns and validate data.
5. **Add a note** (optional) to record why you changed something.
6. **Finalize** the review. The document leaves the queue and its embedding is regenerated with corrected values.

### Search and case history

- **Search** across all intakes by name, keyword, or document ID.
- **Case view** shows all documents for a single applicant on a timeline, useful for tracking longitudinal care.

### Auto-accepted documents

If all fields had very high confidence, the system may auto-accept the document. These are still available for you to open, check, and correct if needed.

### Failed extractions

If a document shows "Extraction failed," you can retry extraction. If it fails again, contact your administrator.

---

## Admin

**Your job:** Manage form templates and perform system maintenance.

### Template management

- **Create templates** with a name, ID, and field schema (each field has a key, type, and required flag).
- **Edit templates** to add or change fields. Changes affect future extractions only, not previously processed documents.
- **Upload blank PDFs** for each template so workers can download and print them.
- **Archive templates** to retire them from the worker dropdown. Archiving does not delete submitted documents.

### System maintenance (API only)

These operations are available via the API with an admin token:

- **Wipe all documents** (`DELETE /admin/documents`) -- permanently removes all intake data for your tenant. Templates are preserved.
- **Reprocess all documents** (`POST /admin/reprocess`) -- re-queues all documents for fresh AI extraction. Use after changing extraction models or templates.

---

## Multi-tenancy

Each organization's data is completely isolated. Users from one tenant cannot see another tenant's documents, templates, or search results. This isolation is enforced at the database level.
