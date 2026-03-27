# Open Horizons Framework

**The shift:** Action is cheap. Knowing what to do is scarce.

**The sequence:** aim -> problem-space -> problem-statement -> solution-space -> execute -> ship

Each phase can run as a scoped agent in Claude Code (`.claude/agents/`) to keep context focused by phase. Use `.oh/` session notes to pass decisions and constraints between phases.

**Where to start (triggers):**
- Can't explain why you're building this -> `oh-aim` agent (or `/aim`)
- Keep hitting recurring blockers/constraints -> `oh-problem-space` agent (or `/problem-space`)
- Framing feels wrong -> `oh-problem-statement` agent (or `/problem-statement`)
- About to choose implementation approach -> `oh-solution-space` agent (or `/solution-space`)
- Ready to build with bounded scope -> `oh-execute` agent (or `/execute`)
- Implementation complete, preparing delivery -> `oh-ship` agent (or `/ship`)
- Thrashing/reversals -> `/salvage`

**Reflection skills (use anytime):**
- `/review` -- alignment and quality check before merge
- `/dissent` -- challenge assumptions before one-way doors
- `/salvage` -- extract learning before restart
- `/distill` -- consolidate recurring patterns and guardrails

**Key insight:** Enter at the altitude you need. Climb back up when you drift.

---

# Project Context

## Purpose
Northwoods processes intake documents into structured, confidence-scored fields that are reviewed by humans before final acceptance. The system optimizes for reviewer trust, auditability, and tenant-safe workflows.

## Current Aims
- Stabilize local-first extraction quality using staged providers (OCR + normalization).
- Preserve full extraction provenance per field and per run.
- Keep review pipeline fast and reliable while handling low-confidence ambiguity safely.

## Key Constraints
- **Tenant isolation is non-negotiable:** all document, extraction, and audit records must remain tenant-safe.
- **Human-in-the-loop guarantee:** ambiguous or low-confidence data must route to review, not silent auto-accept.
- **Append-only auditability:** extraction attempts must be stored as history, not overwritten.
- **Portable provider design:** provider-specific logic stays behind worker abstractions so stages can be swapped.

## Patterns to Follow
- Use staged extraction providers in `Extraction.Worker` with explicit provider/stage metadata.
- Keep confidence thresholds explicit and centralized (`High`, `ReviewRequired`, `Escalate`).
- Persist run-level attempt metadata (`extraction_run_id`, `technique`, confidence inputs/outputs).
- Prefer deterministic parsing/validation before accepting model output.
- Keep docs/ADR updates in the same change set as behavior shifts.

## Anti-Patterns to Avoid
- Do not bypass review gating for low-confidence fields.
- Do not overwrite prior extraction attempts when reprocessing.
- Do not add provider-specific behavior directly in API contracts.
- Do not commit real secrets (`.env.local` stays local; update `.env.example` for new settings).

## Decision Context
- "Done" means: code builds, targeted runtime behavior is validated, and audit/review invariants hold.
- Non-trivial extraction changes should include proof via local intake runs (upload -> worker processing -> review payload + attempts).
- ADRs in `docs/ADRs` are the source of truth for architecture-level extraction decisions.

## Toolstack Notes
- Primary stack: .NET services/worker, Postgres, MinIO, web app under `apps/web`.
- Typical local commands:
  - `docker compose up -d postgres minio api worker`
  - `dotnet build src/Northwoods.slnx`
  - `pnpm check`
- Browser validation can use Playwright CLI skill when UI workflow verification is needed.

---

## Design Context

### Users
Social workers operating in two roles within government agencies or nonprofit human services organizations:
- **Intake workers** upload scanned handwritten forms. They work under time pressure, often processing many intakes per day. They need to trust the system enough to submit a scan and move on.
- **Reviewers** validate extracted data against the original document. They need to focus attention on uncertainty, not re-enter everything. Speed matters, but accuracy matters more.

Both roles work in institutional environments. The interface should feel like a reliable professional tool, not a consumer app or a developer dashboard.

### Brand Personality
**Calm, trustworthy, precise.**

The system handles sensitive personal information about vulnerable populations. The interface should communicate:
- **Reliability** -- this tool does not lose data or make silent mistakes.
- **Clarity** -- the user always knows what state a document is in, what needs attention, and what has been completed.
- **Restraint** -- no decoration for its own sake. Every visual element earns its place by reducing cognitive load or surfacing actionable information.

### Aesthetic Direction
- **Visual tone:** Institutional, accessible, understated. Closer to Gov.uk / USDS design patterns than to SaaS marketing dashboards.
- **Theme:** Light mode. Dark backgrounds signal developer tools or consumer media; light backgrounds signal institutional trustworthiness and accessibility.
- **References:** Gov.uk Design System, U.S. Web Design System (USWDS), healthcare/government case management tools.
- **Anti-references:** Flashy dark dashboards, glassmorphism, heavy gradients, marketing-style hero sections, decorative illustrations.
- **Typography:** Inter remains appropriate. Use weight and size hierarchy, not color variety, to establish structure.
- **Color:** Muted, purposeful palette. Reserve color for meaning:
  - Confidence tiers (green/amber/red) should be the primary use of color.
  - Navigation and structure should be neutral (grays, near-black text on white).
  - Accent color (blue or teal) used sparingly for interactive elements.
- **Spacing:** Generous whitespace. Dense information is fine but cramped layouts are not. Let the content breathe.
- **Borders and surfaces:** Subtle borders and flat surfaces over shadows, blur, and transparency. Cards are fine; glassmorphism is not.

### Design Principles

1. **Confidence is the interface.** The most important visual signal in the system is how confident the extraction is. Every screen should make confidence immediately legible through color, positioning, or progressive disclosure.

2. **Status over chrome.** Users need to know where a document is in its lifecycle (uploaded, extracting, review-ready, finalized, failed) more than they need visual polish. Status indicators should be unambiguous and visible without interaction.

3. **Reduce reviewer burden, not reviewer control.** The UI should pre-sort, highlight, and focus attention on uncertain fields. It should never hide fields, auto-accept without visibility, or remove the reviewer's ability to inspect and override.

4. **Institutional trust over visual novelty.** The system is used in settings where trust is earned through predictability and clarity. Avoid surprising layouts, animated transitions, or unconventional interaction patterns. Boring is good.

5. **Accessible by default.** WCAG AA minimum. Sufficient color contrast on light backgrounds. No reliance on color alone for meaning (pair with icons or text). Support keyboard navigation. Respect reduced-motion preferences.
