---
name: dev-pipeline
description: Full dev pipeline from problem framing through merge. Ensures GitHub issue exists, explores solutions, executes, then ships. Adapted for the Northwoods .NET + React + Postgres stack.
tools: Read, Write, Edit, Grep, Glob, Bash, Agent, WebFetch, WebSearch
mcpServers:
  - rna-mcp
---

# /dev-pipeline

Full development pipeline: **problem-statement -> solution-space -> execute -> ship.**

Takes a feature or bug from framing through merge. Each phase feeds the next via a session file. The pipeline ensures nothing is skipped -- no coding without a problem statement, no merging without the /ship quality gate.

> **Use RNA tools -- not Grep/Read -- for all code navigation.**
>
> - **MCP tools** (`search`, `repo_map`, `outcome_progress`) -- project-level context: guardrails, outcomes, metis, cross-cutting impact analysis.
> - **CLI in your worktree** -- code navigation WITHIN your working directory:
>   ```bash
>   repo-native-alignment search --repo . "what you're looking for" --limit 5
>   repo-native-alignment graph --node "file:symbol:kind" --repo . --mode neighbors
>   ```
>   **The worktree MUST be scanned before querying.** See the MANDATORY scan step in Phase 3.
>
> **Friction logging:** Every Grep/Read used for code navigation after the scan has run is a friction event. Log it to the session file's `## RNA Tool Friction Log` table with severity `skipped`.

> **BUILD GUARDRAIL:** Before building, verify you are in the correct working directory. Use `dotnet build src/Northwoods.slnx` for backend. Use `pnpm --dir apps/web build` for frontend. Use `pnpm check` for both.

## Arguments

`/dev-pipeline <issue-number-or-description>`

- If a GitHub issue number is given (e.g., `1`), read it as the starting context.
- If a description is given, use it to frame the problem in Phase 1.
- If both, the issue is the source of truth; the description is supplementary context.

## Session File

All phases write to `.oh/sessions/<issue-number>-dev.md` (or `<slug>-dev.md` if no issue yet).

Initialize it at pipeline start:

```markdown
# Dev Pipeline -- <title>
**Issue:** #<number> (or "pending")
**PR:** (filled in Phase 3)
**Started:** <timestamp>

## Phase 1: Problem Statement
(filled by Phase 1)

## Phase 2: Solution Space
(filled by Phase 2)

## Phase 3: Execute
(filled by Phase 3)

## Phase 4: Ship
(filled by Phase 4)

## RNA Tool Friction Log
<!-- Append entries as you encounter friction with RNA MCP tools. -->
<!-- Format: | Phase | Tool | What happened | What you did instead | Severity | -->
| Phase | Tool | What happened | Workaround | Severity |
|-------|------|---------------|------------|----------|
```

---

## Phase 1: Problem Statement -> GitHub Issue

**Goal:** Ensure the work has a crisp problem statement captured in a GitHub issue.

### If an issue number was provided:
1. Read the issue: `gh issue view <number>`
2. Assess: does it already have a clear problem statement? (outcome-focused, testable, solution-agnostic)
3. If yes -- extract it into the session file, move to Phase 2.
4. If no -- run the `/problem-statement` process against the issue description to reframe it.
5. Update the issue body with the reframed problem statement: `gh issue edit <number> --body ...`

### If only a description was provided:
1. Run the `/problem-statement` process to frame the problem.
2. Create a GitHub issue with the problem statement as the body.
3. Record the issue number in the session file.

### Phase 1 output:
- A GitHub issue with a clear problem statement and acceptance criteria
- Session file updated with the problem statement

**Gate:** Do not proceed to Phase 2 without acceptance criteria on the issue.

---

## Phase 2: Solution Space -> PR Description

**Goal:** Explore candidate solutions and draft a PR with the chosen approach.

1. Run the `/solution-space` process, using the problem statement from Phase 1 as input.
   - Generate 3-4 candidates at different levels (band-aid -> redesign)
   - Evaluate trade-offs
   - Recommend with reasoning

   **Bias against Local Optimum solutions.** Band-aids are sometimes the right call. Redesigns dissolve problems. But Local Optimum / "just refactor this" type solutions are the danger zone -- they add complexity without changing the model. Check: has this codebase solved a similar problem before? (`search` for prior art.)

2. Create a feature branch:
   ```bash
   git checkout -b <issue-number>-<slug> main
   ```

3. Draft the PR description from the solution space output:
   ```bash
   gh pr create --draft --title "<title>" --body "$(cat <<'EOF'
   ## Summary
   <1-2 sentence summary of chosen approach>

   Closes #<issue-number>

   ## Problem
   <from Phase 1>

   ## Solution
   **Selected:** <recommended option>
   **Level:** <band-aid / local optimum / reframe / redesign>

   <rationale>

   ## Acceptance Criteria
   <copied from issue>

   ## Test Plan
   - [ ] ...
   EOF
   )"
   ```

4. Update session file with solution space analysis and PR number.

### Phase 2 output:
- A draft PR with solution exploration in the description
- Session file updated with solution space and PR number

**Gate:** Do not proceed to Phase 3 without a draft PR.

---

## Phase 3: Execute

**Goal:** Implement the chosen solution.

### MANDATORY: RNA scan before touching any code

**You CANNOT start coding until the RNA index is live.** Run this before opening any source file:

```bash
COUNT=$(repo-native-alignment search "" --repo . --limit 1 2>/dev/null | grep -o "[0-9]* symbols" | head -1)
echo "RNA ready: $COUNT indexed"

# If 0 or failed, rebuild:
if [ -z "$COUNT" ] || [ "$COUNT" = "0" ]; then
  repo-native-alignment scan --repo . --full 2>&1 | tail -2
fi
```

### Build and verify

Use the project's standard build/check commands:

```bash
# Backend
dotnet build src/Northwoods.slnx

# Frontend
pnpm --dir apps/web build

# Both
pnpm check

# Docker Compose (if infrastructure changes)
docker compose up --build -d
```

### Implementation

Launch the `/execute` process with the session file as context:

- Pre-flight, build, drift detection, salvage if needed
- Commits are pushed to the PR branch
- Tag commits with `[outcome:X]` if a relevant outcome exists

After execution completes:
1. Update session file with execution notes
2. Push final commits
3. Keep the PR as draft; Ship will mark it ready after review/dissent fixes

### Phase 3 output:
- Implementation committed and pushed to the PR branch
- PR remains draft until Ship marks it ready
- Session file updated with execution status

**Gate:** Do not proceed to Phase 4 if execution produced SALVAGE verdict. Surface the salvage to the user and stop.

---

## Phase 4: Ship

**Goal:** Quality gate and merge.

> **CRITICAL: CodeRabbit will review this PR.** When the PR is marked ready for review (step 3b below), CodeRabbit will post automated findings. These MUST be addressed before merge.

### Ship steps

1. **Review** -- Run `/review` against the diff. Post findings as a PR comment with the **exact header `## /review findings`**.
2. **Dissent** -- Run `/dissent` to challenge assumptions. Post as a PR comment with the **exact header `## /dissent challenge`**.
3. **Fix** -- Address all review + dissent findings.

3b. **Gate check -- REQUIRED before `gh pr ready`:**
   ```bash
   OWNER_REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner)
   PR=<number>
   REVIEW=$(gh api repos/$OWNER_REPO/issues/$PR/comments | jq '[.[] | select(.body | startswith("## /review findings"))] | length')
   DISSENT=$(gh api repos/$OWNER_REPO/issues/$PR/comments | jq '[.[] | select(.body | startswith("## /dissent challenge"))] | length')
   echo "review=$REVIEW dissent=$DISSENT"
   ```
   **Do not call `gh pr ready` until both counts are >= 1. If either is 0, the comment was not posted -- post it now before continuing.**

3c. **Mark ready** -- `gh pr ready <number>`. This triggers CodeRabbit review.
4. **Wait for CodeRabbit** -- Poll PR comments until CodeRabbit posts its review (typically 1-3 minutes).
5. **Address CodeRabbit findings** -- Fix Critical and Major findings. Document Minor findings if not fixing. Push fixes.
6. **Verify builds** -- `pnpm check` and `docker compose up --build -d` if infra changed.
7. **Final comment sweep** -- Re-fetch all PR comments. Verify nothing was posted after step 5 that is unaddressed.
8. **Merge** -- `gh pr merge <number> --squash --delete-branch`

### Phase 4 output:
- PR merged (or stopped with verdict if issues found)
- Session file updated with ship pipeline results

---

## Automation Rules

- **Do not wait** for user prompts between phases. When one phase completes and its gate passes, immediately start the next.
- **Stop and ask** only if:
  - Phase 1 can't determine acceptance criteria (needs user input)
  - Phase 3 produces a SALVAGE verdict
  - Phase 4 CI fails after 2 fix attempts or CodeRabbit raises Critical findings that require design changes
- **Record metis** if any phase surfaces a new learning: write to `.oh/metis/<slug>.md`

## Friction Reporting

When an RNA tool falls short -- wrong results, missing data, too slow, or you fell back to Grep/Read -- append to the session file's `## RNA Tool Friction Log` table.

**At pipeline end**, summarize friction events with recommendations.

## Position in Framework

**This is the full pipeline.** It composes:
- `/problem-statement` (Phase 1)
- `/solution-space` (Phase 2)
- `/execute` (Phase 3)
- `/ship` (Phase 4)

For partial runs, use the individual skills directly. `/dev-pipeline` is for end-to-end delivery of a single issue.
