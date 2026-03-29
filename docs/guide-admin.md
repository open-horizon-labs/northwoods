# Administrator Guide

**Your role:** You manage the Northwoods configuration for your organization. This includes setting up intake form templates, attaching blank PDF files to those templates so workers can print them, archiving templates that are no longer needed, and performing system maintenance tasks like wiping or reprocessing documents.

> **Important:** Administrator actions that delete or reset data cannot be undone. Read each section carefully before proceeding.

---

## Signing In

1. Open your web browser and go to **https://northwoods.muness.com/**
2. Enter your admin email address and password and click **Sign in**.

**Demo credentials (for the demo environment):**

| Organization   | Email                        | Password   |
|----------------|------------------------------|------------|
| Sunrise        | `admin@sunrise.example`      | `password` |
| Lakewood       | `admin@lakewood.example`     | `password` |

After signing in you will land on the **Template Management** page. This is the only page administrators see after login.

To sign out, click the **Sign out** button in the top-right corner.

---

## The Template Management Page

This page lists all intake form templates for your organization. Templates define which fields the system will try to extract from a scanned form. The page is divided into two areas:

- **Active templates** — templates that intake workers can currently use
- **Archived** — templates that have been retired and are no longer available to workers

Each active template card shows:
- The template's name and internal ID
- How many fields it has, and what those fields are called
- A "PDF uploaded" badge if a blank form file has been attached
- Buttons to **Edit** or **Archive** the template
- An uploader to attach or replace the blank PDF

---

## Creating a New Template

1. Click the blue **+ New template** button at the top right.
2. Fill in the form:
   - **Template ID** — a short code in lowercase letters and hyphens, for example `general-assistance`. This cannot be changed after you create the template.
   - **Name** — the human-readable name workers will see in the dropdown, for example "General Assistance Intake".
   - **Fields** — the list of pieces of information the system should extract. For each field, set:
     - **Key** — the internal name for the field, in camelCase with no spaces (for example `applicantName`, `dateOfBirth`)
     - **Type** — the kind of value: Text, Date, Number, Currency, or List
     - **Required** — check this box if the field must be present on every form
3. Use the **+ Add field** button to add more fields. Use the X button on any field row to remove it.
4. Click **Create template** to save. Click **Cancel** to discard.

The new template will appear in the active list immediately and will be available to intake workers.

---

## Editing an Existing Template

1. Find the template in the active list and click **Edit**.
2. You can change the name and modify fields (add, remove, or update them).
3. Click **Save changes** when done, or **Cancel** to discard your changes.

> **Note:** Changing a template affects how future documents are processed. It does not change data that has already been extracted from previously submitted forms.

---

## Uploading a Blank PDF for a Template

Attaching a blank PDF allows intake workers to download and print an empty copy of the form to hand to clients.

1. Find the template in the active list.
2. Below the field list for that template, find the **Upload blank PDF** section.
3. Click the file picker and select a PDF file.
4. The file uploads automatically. When complete, the template card shows a "PDF uploaded" badge.

To replace the current blank PDF, use the same uploader — it will overwrite the previous file.

---

## Archiving a Template

Archiving removes a template from the list that intake workers see. It does not delete any documents or data that were already submitted using that template.

1. Find the template in the active list and click the red **Archive** button.
2. The template moves to the **Archived** section at the bottom of the page.

Archived templates cannot be restored from the interface. If you need to reactivate one, contact your system administrator or technical support.

---

## System Maintenance

The following actions are available through the API for administrators. They are not exposed as buttons in the template management UI, so they require a tool that can make HTTP requests (such as a script or a REST client). Both require your admin login token.

### Wipe All Documents

**What it does:** Permanently deletes every document, extracted field, extraction attempt, audit event, and case profile for your organization. This leaves your templates intact but removes all submitted intake data.

**When to use this:** When resetting a demo environment, starting a new test cycle, or decommissioning a tenant. Do not use this in a live production environment unless you are certain all data has been backed up elsewhere.

**API call:**
```
DELETE /admin/documents
Authorization: Bearer <your-admin-token>
```

The response tells you how many documents were deleted.

### Reprocess All Documents

**What it does:** Resets every document that has been processed (finalized, completed, ready for review, or failed) back to "Queued" status. All extracted fields and extraction attempts for those documents are cleared. The system will then re-run AI extraction on each document from scratch.

**When to use this:** When you have updated the extraction model or made significant changes to a template and want existing documents to be re-extracted with the new settings.

**API call:**
```
POST /admin/reprocess
Authorization: Bearer <your-admin-token>
```

The response tells you how many documents were queued for reprocessing.

---

## Demo Tenant Overview

The demo environment has two organizations (tenants) that are completely isolated from each other. Users from one organization cannot see the other organization's documents, even if they have the same email prefix.

| Tenant ID  | Organization | Intake Worker               | Reviewer                      | Admin                        |
|------------|--------------|-----------------------------|------------------------------ |------------------------------|
| `tenant-a` | Sunrise      | `worker@sunrise.example`    | `reviewer@sunrise.example`    | `admin@sunrise.example`      |
| `tenant-b` | Lakewood     | `worker@lakewood.example`   | `reviewer@lakewood.example`   | `admin@lakewood.example`     |

All demo accounts use the password `password`.

---

## Common Questions

**Can I undo an archive?**
Not through the interface. Contact your technical team if you need to restore an archived template.

**Can I change a template's ID after it is created?**
No. The ID is permanent. If you need a different ID, create a new template with the correct ID and archive the old one.

**A worker says a template is not showing up in their dropdown.**
Check that the template is in the Active list and not in the Archived section. If it was recently created and is still not visible, have the worker refresh their browser page.

**I need to add a new user to the system.**
User management is not available through the interface. Contact your technical administrator to add or remove user accounts directly in the database.

**I ran Reprocess and now all my documents are back in the queue. How long will it take?**
Processing time depends on the number of documents and the current load on the system. A small batch (under 50 documents) typically completes within a few minutes. Larger batches may take longer.

---

## Need Help?

Contact your technical administrator or system support team.
