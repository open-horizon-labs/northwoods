# AI Development Tooling Used

This project was developed with an AI-assisted workflow. The goal was not to outsource judgment, but to increase speed, context retention, and design quality while keeping architecture and trade-offs explicit.

## Tooling Summary

- **oh-mcp**
  - Used to access my personal strategy graph and working context.
  - Helped connect this exercise to my own aims, decision criteria, and reflective artifacts so the submission stayed aligned with the kind of technical leadership I want to demonstrate.

- **Repo-Native Alignment (RNA)**
  - Used both as an MCP server and as a skill/tooling layer.
  - Makes repository context queryable through semantic search and graph-style code/document relationships.
  - Useful for keeping code, docs, outcomes, and decisions connected instead of relying only on manual grep or memory.

- **Open Horizons skills**
  - Workflow skills used to structure thinking and execution.
  - In practice, these support phases like aiming, framing the problem space, execution discipline, recording outcomes/guardrails, and review.
  - They act as lightweight operating procedures for high-judgment work.

- **Playwright CLI skill (`playwright-cli`)**
  - Used to automate browser interactions for intake/review flow validation against the running local stack.
  - Helped verify end-to-end behavior (upload → extraction → review) with reproducible scripted runs instead of ad-hoc manual clicks.


- **CodeRabbit**
  - Automated PR review service integrated into the GitHub workflow.
  - Provides severity-classified findings (Critical, Major, Minor) on every PR marked ready for review.
  - Used as part of the dev-pipeline ship step to catch issues before merge.

- **Dev-pipeline agents (`.claude/agents/`)**
  - `dev-pipeline` runs the full issue lifecycle: problem-statement, solution-space, execute, ship.
  - `dev-pipeline-oversight` wraps dev-pipeline with a post-merge comment audit that verifies all external review findings (CodeRabbit, human, review skill) were addressed.
  - Ensures no PR merges with unresolved Critical or Major findings.
- **Oh My Pi fork (`.oh-omp/`)**
  - Local fork used as the coding harness.
  - Handles context assembly so the model can work with repository state, skill guidance, artifacts, and tool outputs in a more grounded way.

- **ChatGPT Pro + Claude Code Max subscriptions**
  - Due to rate-limit throttling on either platform alone, both ChatGPT Pro and Claude Code Max (x20) subscriptions were used throughout the project.
  - ChatGPT-5.4 family (`gpt-5.4`, `gpt-5.4-mini`, `gpt-5.4-nano`) for coding, architectural reasoning, and extraction normalization.
  - Codex-5.3 Spark model for lightweight inline code generation tasks.
  - Claude (Sonnet/Opus) via Claude Code for extended coding sessions, multi-file refactors, and execution-heavy phases.
  - Switching between platforms depending on which had available capacity kept throughput high during intensive build sessions.

## How these tools were used

These tools were used to:
- shape the problem before implementation,
- keep architecture and workflow decisions explicit,
- manage repository and decision context,
- speed up iteration on code and documentation,
- and make trade-offs more legible to a reviewer.

## What remained human-owned

I retained responsibility for:
- system framing,
- architectural choices,
- scope decisions,
- trade-off calls,
- and the final judgment about what was credible enough to include.

In other words, AI increased leverage, but it did not replace accountability.