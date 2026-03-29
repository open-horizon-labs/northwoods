# ADR 004: Use shared Postgres tenancy with `tenant_id` and RLS backstop

- **Status:** Accepted
- **Date:** 2026-03-25

## Context

The assignment requires basic multi-tenancy across templates, uploads, processing results, review actions, search, and similar-case retrieval.

For this exercise, tenancy must be:

- explicit enough to explain and defend
- strong enough to prevent cross-tenant leakage
- simple enough to implement and test in a local Docker Compose environment
- consistent across APIs, workflow execution, storage metadata, and retrieval

The platform also needs a tenancy model that works naturally with the other chosen infrastructure:
- Postgres for transactional data and hybrid retrieval
- Temporal for workflow orchestration
- MinIO for original document storage

## Decision

We will use **shared Postgres tables with `tenant_id` on every tenant-scoped record**, enforced by:

- explicit application-level tenant scoping in services and workflows
- **Postgres Row-Level Security (RLS)** as a database backstop
- tenant-aware metadata for object storage and workflow state

We will not rely on application filtering alone.

We will not use schema-per-tenant or database-per-tenant for this exercise.

## Why this decision

### 1. It is the right complexity level for the assignment

The brief asks for basic multi-tenancy that is conceptually correct. Shared tables with `tenant_id` provide the cleanest implementation path that still demonstrates strong isolation discipline.

This lets us show:
- tenant-aware API access
- tenant-safe search and retrieval
- tenant-safe review workflows
- explicit tenancy propagation through the full document lifecycle

without paying the operational cost of per-tenant schemas or databases.

### 2. RLS makes the tenancy boundary more credible

Application-level scoping is necessary, but not sufficient. It is too easy for a missed `WHERE tenant_id = ...` clause to create a data leak.

RLS gives us a database-enforced safety boundary so that tenant visibility is not dependent on every query author remembering the filter.

For this project, that is a strong reviewer-facing signal: tenancy is enforced in both the application layer and the data layer.

### 3. It fits hybrid retrieval better than more fragmented tenancy models

Because similar-case retrieval, exact matching, fuzzy matching, and full-text search all live in Postgres, keeping tenant scoping in the same database simplifies correctness.

The same core tenant boundary can govern:
- case data
- extracted fields
- review notes
- audit entries
- embeddings
- search/reranking candidate sets

### 4. It is easier to test and demonstrate

A good interview solution needs not only the right choice, but a choice that is easy to prove.

With shared tables plus RLS, we can write direct tests showing that:
- tenant A cannot read tenant B rows
- search results do not cross tenant boundaries
- similar-case retrieval does not cross tenant boundaries
- workflow-owned writes remain scoped to the initiating tenant

## Implementation model

### Relational model

- Every tenant-scoped table includes `tenant_id`
- Composite or tenant-aware indexes include `tenant_id` where query patterns require it
- Cross-table relationships preserve tenant consistency

### Application model

- JWTs carry `tenantId` and role claims
- Services resolve tenant context from authenticated requests
- The extraction worker receives tenant context from the document record it picks up from the poll queue; tenant context is passed explicitly through each extraction activity call
- MinIO object metadata and object-key conventions remain tenant-aware

Note: ADR 002 (Temporal) was deferred. The worker polling model used instead propagates tenant context through the `Document.TenantId` field read at poll time.

### Database model

- RLS is enabled on tenant-scoped tables
- Application roles do **not** bypass RLS
- Migrations/admin operations use separate elevated roles
- Session or transaction-scoped tenant context is set explicitly before tenant-scoped queries

## Consequences

### Positive

- strong tenant-safety story without overbuilding
- consistent isolation model across CRUD, review, search, and retrieval
- easier local development and Docker Compose setup
- good fit with Postgres hybrid retrieval design
- simpler to document and test than schema-per-tenant or db-per-tenant models

### Negative

- every tenant-scoped table and query path must be modeled carefully
- app and workflow code still must propagate tenant context correctly
- shared-database blast radius is larger than db-per-tenant if privileged roles are misused

## Rejected alternatives

### Application-level filtering only

Rejected because it is too easy to get wrong, especially once search, retrieval, and background workflows are involved.

### Schema-per-tenant

Rejected for this exercise because it adds migration, provisioning, and operational complexity without improving the core demonstration enough to justify the cost.

### Database-per-tenant

Rejected because it is too heavy for a local-first interview exercise and would distract from the core workflow and review experience.

## Implementation notes

- Add `tenant_id` to all tenant-scoped entities, including search/retrieval materializations
- Use RLS as the enforcement backstop, not as a substitute for explicit tenant-aware application design
- Ensure Temporal activities receive tenant context as an explicit input, not implicit ambient state
- Store tenant metadata alongside MinIO object references
- Write integration tests that attempt cross-tenant access through normal APIs and retrieval paths
- Avoid using table owners or `BYPASSRLS` roles for application traffic

## References

- PostgreSQL Row Security Policies: https://www.postgresql.org/docs/current/ddl-rowsecurity.html
- [ADR 001: Use Postgres hybrid retrieval with `pgvector` and `pg_trgm`](001-postgres-hybrid-retrieval-with-pgvector-and-pg-trgm.md)
- [ADR 002: Use Temporal for document processing workflows](002-temporal-for-document-processing-workflows.md)
- [ADR 003: Use MinIO for S3-compatible document storage](003-minio-for-s3-compatible-document-storage.md)
