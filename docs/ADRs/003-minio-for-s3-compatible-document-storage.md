# ADR 003: Use MinIO for S3-compatible document storage

- **Status:** Accepted
- **Date:** 2026-03-25

## Context

The assignment requires storing original uploaded documents and making them available during review.

The system therefore needs object storage for:

- scanned image and PDF uploads
- stable retrieval of originals during review
- explicit separation between binary object storage and relational metadata

The storage choice should:
- run locally in Docker Compose
- support a credible production-style API
- stay simple enough for a small exercise
- avoid coupling the project to a specific cloud provider during local development

## Decision

We will use **MinIO** as the local object storage system, accessed through its **S3-compatible API**.

We will store:
- binary file objects in MinIO
- document metadata, tenancy information, object keys, statuses, and relationships in Postgres

We will not store uploaded binaries directly in Postgres except for incidental test fixtures.

## Why this decision

### 1. It cleanly separates object storage from relational state

The platform needs both:
- durable binary storage for original documents
- relational records for templates, processing state, extracted values, audit events, and review status

Using MinIO for objects and Postgres for metadata keeps those responsibilities clear.

### 2. It gives us a credible storage API without cloud lock-in

S3 compatibility is a strong portability point. It lets the project use a widely understood storage contract while remaining easy to run locally.

That is a better reviewer story than inventing a custom file-store abstraction or binding the exercise to a single hosted cloud dependency.

### 3. It works well with the workflow design

The ingestion workflow can:
- write the object to MinIO
- persist the object key and metadata in Postgres
- pass the object reference into extraction activities
- surface the original document in the review UI via a controlled backend route

### 4. It stays at the right complexity level

MinIO is enough to demonstrate credible object-storage design without dragging cloud provisioning, IAM setup, or provider-specific complexity into the first delivery slice.

## Consequences

### Positive

- production-like storage API in local development
- clear separation of object data and relational metadata
- easier future portability to S3-compatible storage
- simple fit for Docker Compose
- better alignment with the document-review use case than database blob storage

### Negative

- one more service to run locally
- storage lifecycle and object-key conventions must be managed deliberately
- access patterns must still preserve tenant safety at the application layer

## Rejected alternatives

### Store documents directly in Postgres

Rejected because it mixes large binary storage with transactional/query-heavy relational concerns and weakens the architectural story.

### Local filesystem only

Rejected as the main design because it is less portable, less cloud-like, and weaker as a reviewer-facing systems decision.

### Provider-specific object store from day one

Rejected because the assignment is local-first and does not require commitment to AWS, Azure, or GCP infrastructure.

## Implementation notes

- Use tenant-aware object key conventions and store `tenant_id` in relational metadata
- Do not expose raw object paths directly from the frontend
- Serve review access through backend-authorized endpoints or time-bounded signed URLs
- Keep file metadata, MIME type, checksum, and processing linkage in Postgres
- Treat MinIO as the binary source of truth and Postgres as the metadata/control plane

## References

- MinIO documentation: https://min.io/docs/minio/linux/index.html
- MinIO S3 compatibility: https://min.io/product/s3-compatibility
