# ADR 002: Use Temporal for document processing workflows

- **Status:** Accepted — deferred; not yet implemented
- **Date:** 2026-03-25
- **Update:** 2026-03-27 — Temporal was deferred in favor of a simpler .NET `BackgroundService` worker polling loop. The rationale below still stands as the target design if orchestration is upgraded. The current implementation uses the polling model described in the "Rejected alternatives" section of this ADR, with retry behavior handled in-process.

## Context

The assignment requires asynchronous document processing with retries, visible processing status, and a credible upload → extract → review workflow.

The workflow is not a single fire-and-forget background task. Even in the first slice, the system needs durable coordination across steps such as:

- persisting the uploaded document record
- storing the original file
- running OCR / extraction
- writing extracted field values and confidence scores
- creating review-ready state
- generating embeddings for similar-case retrieval
- recording audit events and terminal status

This work must preserve tenant context, tolerate transient failures, and stay observable enough that a reviewer can see this is not a fragile demo.

## Decision

We will use **Temporal** as the workflow orchestration mechanism for asynchronous document processing.

We will model the ingestion path as a small number of durable workflows and activities, starting with a single top-level document ingestion workflow.

We will not use:

- an ad hoc jobs table as the primary orchestration model
- a separate message-bus-heavy event mesh from day one
- Celery or another non-.NET-first worker stack

## Why this decision

### 1. The workflow is durable orchestration, not just background execution

A jobs table is acceptable for simple one-step async work. This problem already has enough state transitions and retry semantics that hand-rolling workflow durability would create accidental complexity quickly.

Temporal gives us:
- durable workflow state
- retries and backoff
- visibility into workflow progress
- explicit step boundaries
- better handling of chained asynchronous operations

### 2. It strengthens the operational credibility of the exercise

A reviewer should be able to see that processing is not a black box.

Temporal helps make these behaviors legible:
- document accepted
- extraction running
- extraction failed and retried
- extraction completed
- review-ready state created

That supports the assignment's observability and resilience criteria.

### 3. It is a better fit than Celery for this stack

The target system is .NET + React. Introducing Celery would add a second language/runtime center of gravity without a compensating advantage for this exercise.

Temporal has a .NET SDK and fits the current stack direction more cleanly.

### 4. It lets us keep service count low while preserving workflow clarity

Temporal gives us workflow discipline without forcing a larger distributed-systems footprint.

That matches the chosen architecture principle: capability-aligned services with only as much infrastructure as the workflow actually needs.

## Initial workflow design

### DocumentIngestionWorkflow

The first workflow should cover:

1. validate tenant and caller context
2. persist document metadata and initial processing status
3. store the original object in S3-compatible storage
4. run OCR / extraction activity
5. persist extracted fields and confidence scores
6. create review-ready state
7. generate/store embeddings for retrieval
8. record completion and audit events

The workflow must preserve tenant context from start to finish.

## Consequences

### Positive

- durable async processing with explicit lifecycle
- retry behavior without hand-built orchestration code
- better operational visibility for document processing
- clear place to preserve and inspect tenant-aware workflow state
- easier extension to additional steps such as embedding refresh or reprocessing

### Negative

- additional infrastructure to run locally
- some upfront workflow modeling overhead
- team must stay disciplined and avoid turning Temporal into workflow sprawl

## Rejected alternatives

### Database jobs table

Rejected as the primary orchestration mechanism because it would likely become an under-specified workflow engine once retries, chaining, and auditability accumulate.

### Message broker + custom consumer choreography

Rejected for the first slice because it increases distributed coordination cost before the value is proven.

### Celery

Rejected because it adds a Python-centered worker stack to a .NET-first solution without clear assignment-level payoff.

## Implementation notes

- Start with one top-level ingestion workflow
- Keep activities small and capability-aligned
- Preserve `tenant_id` and document identity throughout the workflow context
- Expose processing status back to the Intake / Review surfaces
- Use Temporal's retry and visibility features before building custom equivalents
- Avoid mixing Temporal with a second orchestration pattern unless a clear need emerges

## References

- Temporal documentation: https://docs.temporal.io/
- Temporal .NET SDK: https://docs.temporal.io/develop/dotnet/
