# Reviewer Guide

**Your role:** You review intake forms that have been scanned and automatically processed by Northwoods. The system reads each form and extracts the important fields. Your job is to check those extracted values against the original document, fix anything that looks wrong, and finalize the record. You can also search for past intakes and look up an applicant's full history.

---

## Signing In

1. Open your web browser and go to **https://northwoods.muness.com/**
2. You will see the TraverseLite sign-in page.
3. Enter your work email address and password and click **Sign in**.

On the sign-in page there is also a small button in the top-right corner that says "Are you a reviewer, click here..." — clicking it shows a pop-up with demo credentials pre-filled for convenience.

**Demo credentials (for the demo environment):**

| Field    | Value                          |
|----------|--------------------------------|
| Email    | `reviewer@sunrise.example`     |
| Password | `password`                     |

After signing in you will see the **Reviewer Dashboard** — the main screen with three sections across the top (Queue, All Docs, Search) and a large review area to the right.

To sign out, click **Sign out** in the top-right corner.

---

## The Reviewer Dashboard Layout

The screen is split into two parts:

**Left sidebar** — shows a list of documents. Three tabs let you switch what the list shows:
- **Queue** — documents that are ready for you to review right now
- **All Docs** — every document in the system, with their current status
- **Search** — a search box to find any intake by name, keyword, or document ID

**Right detail area** — shows the document you have selected. It has its own two panels side by side:
- Left panel: the original scanned document (PDF viewer)
- Right panel: the extracted fields and actions

The page refreshes automatically every 12 seconds while you are working, so you will see new items appear in the queue without reloading.

---

## Reviewing a Document — Step by Step

### Step 1 — Pick a document from the queue

Click the **Queue** tab in the sidebar. You will see a list of applicant names and their form type. Numbers in amber (orange) circles show how many fields on that form need your attention.

Click on any name to open that document.

### Step 2 — Look at the original document

The left panel shows the original scanned form. You can scroll through it, zoom in, and click **Open in new tab** in the top-left of that panel if you want a larger view.

### Step 3 — Check the extracted fields

The right panel shows each field that the system pulled from the form. Fields are color-coded by how confident the system was:

| Color / Label        | What it means                                                        |
|----------------------|----------------------------------------------------------------------|
| Green — High         | 90% or higher confidence. Almost certainly correct.                  |
| Blue — Good          | 80–89% confidence. Worth a glance but likely fine.                   |
| Amber — Needs review | 65–79% confidence. Compare carefully against the original document.  |
| Red — Review required | Under 65% confidence. The system is not sure — you must check this. |

Fields are sorted so the ones needing the most attention appear at the top.

### Step 4 — Fix any values that are wrong

If a field value does not match what you see on the original form:
1. Click into the text box for that field.
2. Type the correct value.
3. The "Needs review" or "Review required" color will go away once you have edited the field.

You do not need to edit fields that are already correct — only fix what is wrong.

**To understand why the system gave a particular confidence score:** Click the small arrow next to "Why this confidence?" under any field. This shows you which AI providers read the form and what each one said, along with how well they agreed with each other.

### Step 5 — Check additional (discovered) fields

Some forms contain extra information that the system noticed even though it was not a required field. These appear in a collapsible section called **Additional fields**. Click on it to expand and read those values. They are read-only and for your information.

### Step 6 — Review similar cases (if shown)

At the bottom of the right panel you may see a **Similar cases** section. These are past intakes for other applicants whose circumstances closely match the current one. They are there to help you notice patterns or confirm that the information makes sense in context. You can click on any similar case to jump to it; use the browser Back button or click another item in the queue to return to your current document.

### Step 7 — Add a reviewer note (optional)

There is a **Reviewer note** text box at the bottom of the right panel. Use this to record why you changed a value or any other information that should stay with this record. This is optional.

### Step 8 — Finalize the review

Once you have checked all the fields and made any corrections, click the green **Finalize review** button at the bottom of the right panel. A message will say "Review finalized successfully." and the document will be removed from the queue.

---

## What Happens to Documents That Look Perfect?

If all fields had very high confidence (90% or above), the system may auto-accept the document without requiring your manual review. These appear in the queue or document list with the label **Auto-accepted — all fields high confidence**. You can still open them, check the fields, and make corrections if needed. You can still finalize them.

---

## What to Do When Extraction Failed

If a document shows a red **Extraction failed** message in the right panel, it means the system was unable to read the form. You will see a description of what went wrong.

To try again, click the **Retry extraction** button. The document will go back to the processing queue. If it fails again, contact your administrator.

---

## Browsing All Documents

Click the **All Docs** tab in the left sidebar to see every document in the system regardless of status. This is useful for looking up a specific applicant's previously finalized records.

You can type in the filter box at the top of the sidebar to narrow the list by applicant name, form type, or status.

---

## Searching for Intakes

Click the **Search** tab in the left sidebar, type a name, keyword, or value that might appear in a form, and press Enter or click **Search**. Results show the applicant name and a short excerpt of the matching text.

You can also search by document ID. If you paste a full document ID (a long string of letters and numbers separated by dashes) or just the first eight characters, the system will jump directly to that document.

**Keyboard shortcut:** Press the **/** key from anywhere on the page (when you are not already typing in a field) to jump straight to the search box.

---

## Viewing an Applicant's Full History

When you are viewing a document for someone who has other intakes in the system, you will see an **All cases** button near the top of the right panel (next to the applicant's name). Click it to open a timeline showing all documents on file for that person, with dates and statuses. Click any entry in the timeline to open that document. Click **Close** to return to your current document.

---

## Copying a Document ID

Each document has a short ID displayed near the top of the right panel (showing the first eight characters). Click on it to copy the full document ID to your clipboard. This is useful if you need to reference a specific document in an email or ticket.

---

## The RAG Report (System Quality Check)

In the navigation bar at the top of the screen you will see a link to **RAG Report**. This page runs automated tests to check that the system's "similar cases" feature is finding the right matches. This is primarily for system administrators and technical staff — you do not need to use it in your daily work, but you can run it to see how the system is performing.

---

## Common Questions

**I finalized a document by mistake. Can I undo it?**
Finalization cannot be undone from the reviewer interface. Contact your administrator.

**A document has been in "Processing" for a long time and is not showing up in my queue.**
Contact your administrator. They can reset or retry the document.

**I cannot find an applicant I know was submitted recently.**
Try the Search tab and type the applicant's name. If they do not appear, the document may still be processing (check All Docs and filter for their name). If it shows as Failed, use Retry extraction or contact your administrator.

**The PDF viewer shows a blank area instead of the document.**
Some demo documents do not have a stored source file. The extracted fields on the right will still be accurate and available for review. For real intake submissions, the original document will always be viewable.

---

## Need Help?

Contact your supervisor or system administrator.
