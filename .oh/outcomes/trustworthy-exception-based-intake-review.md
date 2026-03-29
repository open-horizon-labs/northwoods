---
id: trustworthy-exception-based-intake-review
title: "Enable tenant-safe exception-based intake review"
status: delivered
mechanism: "Deliver a multi-tenant upload → extract → confidence → review → finalize workflow so intake workers and reviewers trust the system enough to review only uncertain fields rather than manually transcribing entire packets."
files:
  - docs/exercise.md
  - .oh/cto-judgement.md
---

This outcome anchors the product behavior we want the exercise to demonstrate.

Success means intake workers can submit handwritten intake packets against a known template, the system extracts structured case data with visible confidence scores, and reviewers intervene only where the machine is uncertain. The review experience must keep the scanned document, extracted fields, correction actions, and finalization path in one place.

Why it matters:
- It turns the assignment from a checklist into a coherent operating model.
- It concentrates implementation depth where trust is won or lost.
- It makes the RAG and audit requirements support a real workflow instead of reading like disconnected embellishments.

This outcome is only credible if tenant boundaries are enforced across upload, storage, processing, review, search, and retrieval. A fast workflow without isolation is a failed exercise.