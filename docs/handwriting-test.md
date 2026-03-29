# Handwriting Test: Mobile Upload

Test the full pipeline with real handwriting on your phone.

---

## Setup

- **URL**: https://northwoods.muness.com
- **Login**: Use the "Reviewing Muness's submission?" button for quick credentials, or:
  - Worker: `worker@sunrise.example` / `password` (tenant-a)
  - Reviewer: `reviewer@sunrise.example` / `password` (tenant-a)

## Step 1: Download a blank form

1. Log in as **worker** on your phone
2. From the intake submission page, pick **General Assistance Intake** from the dropdown
3. Tap the **Download blank form** link to get the printable PDF
4. Print it (or display it on a second screen)

## Step 2: Fill it out by hand

Write the following in the fields (use your actual handwriting, messy is fine):

| Field | What to write |
|-------|---------------|
| Full Name | **Jordan Rivera** |
| Date of Birth | **04/15/1988** |
| Address | **742 Evergreen Terrace, Springfield, IL 62704** |
| Phone | **217-555-0193** |
| Emergency Contact | **Maria Rivera (mother) 217-555-0142** |
| Reason for Visit | **Requesting help with rent -- lost job two weeks ago, behind on utilities** |
| Income | **$0 currently, was $2,400/mo** |
| Household Size | **3** |

Leave any other fields blank or add extra notes -- the system handles partial forms.

## Step 3: Upload via mobile

1. Log in as **worker** on your phone
2. Select **General Assistance Intake**
3. Tap "Choose file" and use your phone camera to photograph the filled form
4. Submit -- you'll see it go to **Queued** then **Processing**
5. Wait ~30 seconds for extraction to complete (status becomes **Ready for Review**)

## Step 4: Review the extraction

1. Log out, log back in as **reviewer**
2. The new submission should appear in the review queue
3. Open it -- compare the extracted fields against what you wrote
4. Check confidence colors:
   - Green fields were read correctly with high confidence
   - Amber/red fields may need correction
5. Fix any misreads, add a note if you like, then **Finalize**

## Step 5: Verify RAG retrieval

1. After finalizing, search for "Jordan Rivera" in the search bar
2. The document should appear in results
3. Open any other document in review -- "Jordan Rivera" may appear in Similar Cases if the name/address overlaps with existing corpus data

## What to look for

- Does the camera-to-extraction pipeline work end-to-end?
- Are confidence scores reasonable for handwritten input?
- Do corrections persist after finalize?
- Does the new document appear in search immediately after finalization?

---

## Notes

- This adds one real document to the corpus alongside the 184 synthetic ones
- It will NOT affect the RAG Report -- that queries existing data patterns
- To clean up afterward: the admin can delete individual documents, or use `scripts/reset_demo.py` for a full reset
