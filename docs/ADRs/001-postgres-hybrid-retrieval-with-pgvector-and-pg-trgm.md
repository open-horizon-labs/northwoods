# ADR 001: Use Postgres hybrid retrieval with `pgvector` and `pg_trgm`

- **Status:** Accepted
- **Date:** 2026-03-25

## Context

The assignment requires Similar Case Context during review, backed by a vector database / RAG capability, while also enforcing strict tenant isolation.

For this system, similar-case retrieval needs to support multiple kinds of relevance:

- exact or normalized identity signals such as case ID, DOB, and phone
- fuzzy structured matching for names and addresses with OCR noise or misspellings
- lexical matching over OCR text, extracted field values, and reviewer notes
- semantic matching over narrative case content

The retrieval path also needs to stay simple enough to explain, test, and operate in a local Docker Compose environment.

## Decision

We will use **Postgres as the primary retrieval store** and implement hybrid similar-case retrieval with:

- **native Postgres full-text search** for lexical retrieval
- **`pgvector`** for semantic similarity search over embeddings
- **`pg_trgm`** for fuzzy matching on high-value structured text such as names and addresses
- **Reciprocal Rank Fusion (RRF)** or equivalent rank-based fusion in application/query logic to combine lexical and semantic candidates
- **row-level tenant scoping** in every retrieval path, backed by `tenant_id` on all tenant-scoped data and database enforcement via RLS

We will not introduce a separate vector database unless later evidence shows Postgres is insufficient for scale, isolation, or retrieval quality.

## Why this decision

### 1. It fits the actual retrieval problem

This domain is not purely semantic.

Reviewers need results that may be similar because they:
- describe similar needs or narratives,
- refer to the same person or household,
- contain overlapping names, addresses, or phone numbers,
- or share the same template/program context.

A hybrid strategy is therefore a better fit than vector-only search.

### 2. Postgres covers the needed signals well

- **Exact/normalized matching:** standard SQL and indexed columns
- **Lexical matching:** native full-text search (`tsvector`, `tsquery`, `ts_rank`)
- **Fuzzy matching:** `pg_trgm` similarity and trigram indexes
- **Semantic matching:** `pgvector`
- **Fusion/reranking:** straightforward in SQL or application code

This gives us a credible retrieval stack without splitting operational responsibility across multiple datastores.

### 3. Tenant safety is easier to defend in one store

Keeping case data, extracted fields, notes, and embeddings in Postgres makes it easier to enforce and test tenant boundaries consistently.

That supports the exercise's strongest trust requirement:
- no cross-tenant leakage in search,
- no cross-tenant leakage in similar-case retrieval,
- and explicit, testable access control at the data layer.

### 4. It is the right complexity level for this exercise

A separate vector database is possible, but it adds infrastructure, query-path complexity, and another tenancy boundary to explain.

For this assignment, the stronger judgment signal is:
- a simpler system,
- clear retrieval reasoning,
- and a trustworthy reviewer workflow.

## Retrieval design

### Stage 1: Structured filters and exact matching

Apply tenant filter first, then use exact or normalized filters/boosts where appropriate:

- `tenant_id`
- case/person identifiers
- normalized phone
- DOB
- template type / program type

### Stage 2: Hybrid retrieval

Retrieve candidates using both:

- **FTS** over OCR text, extracted text, and reviewer notes
- **vector similarity** over case embeddings via `pgvector`

Fuse candidates using **RRF** so lexical and semantic ranks contribute without brittle score normalization.

### Stage 3: Fuzzy structured boosts

Use `pg_trgm` on selected structured text such as:

- person names
- household member names
- address lines
- city / locality fragments

These signals should boost or supplement hybrid results, not replace tenant-safe core retrieval.

### Stage 4: Optional reranking

If needed later, rerank the fused candidate set in application logic.

This is optional and not required for the first working slice.

## Consequences

### Positive

- simpler architecture and local setup
- easier joins between cases, fields, notes, and embeddings
- strong tenant-isolation story
- hybrid retrieval remains explainable to reviewers
- fewer moving parts in Docker Compose

### Negative

- retrieval workload shares infrastructure with transactional data
- advanced ANN tuning is less specialized than a dedicated vector system
- fusion/reranking logic must be implemented deliberately rather than delegated to a separate product

## Rejected alternatives

### Separate vector database from day one

Rejected for now because it adds operational and conceptual complexity without clear assignment-level benefit.

### Vector-only retrieval

Rejected because the domain has strong exact, fuzzy, and lexical relevance signals that semantic search alone will miss.

### FTS-only retrieval

Rejected because lexical matching alone will miss semantically similar cases expressed with different wording.

## Implementation notes

- Add `tenant_id` to all tenant-scoped retrieval tables
- Use RLS as a database backstop, in addition to application-level tenant scoping
- Store embeddings in Postgres with `pgvector`
- Use generated/search-maintained `tsvector` columns for OCR text and notes
- Add trigram indexes for selected name and address columns
- Start with hybrid retrieval plus structured boosts; only add a heavier reranker if quality demands it

## References

- PostgreSQL Full Text Search: https://www.postgresql.org/docs/current/textsearch.html
- PostgreSQL Row Security Policies: https://www.postgresql.org/docs/current/ddl-rowsecurity.html
- pgvector: https://github.com/pgvector/pgvector
- pg_trgm: https://www.postgresql.org/docs/current/pgtrgm.html
