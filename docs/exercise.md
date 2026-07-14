**Assignment: Handwritten Intake Document Processor** 

**Overview** 

This assignment evaluates your ability to design and implement a modern full-stack web application  using C#, .NET, and React/TypeScript with agentic AI development tools (Cursor AI, Copilot, Claude  Code, Antigravity, etc.). 

You will build an application inspired by organizations like Northwoods, where social workers upload  scanned, handwritten forms to digitize and organize case information. 

The system must support uploading documents, extracting structured data via OCR/AI, storing  confidence scores, enabling human-in-the-loop review, and using RAG with a vector database to  surface similar cases during review. 

Your goal is to deliver a well-structured, working system that follows Clean Architecture principles,  implemented as a set of microservices with clean, well-designed APIs, maintainable code, and clear  design reasoning. 

**Core Features** 

**1. Secure Login** 

• Implement JWT-based modern authentication. 

• Support at least two roles: Intake Worker and Reviewer. 

• Access control: users modify only permitted data. 

**2. Form Templates** 

• Create four different one-page form templates representing distinct types of social services  intakes. 

• Templates should define a set of fields, but you choose the schema, style, and structure. • Allow users to view and download/print blank templates. 

**3. Document Upload & Processing** 

• Intake Workers must upload scanned, hand-filled documents (images or PDFs). • Each upload associates with a chosen template. 

• Implement a background process that performs: 

o OCR and/or AI extraction 

o Field mapping 

o Confidence scoring per field 

• Store: 

o Original file 

o Extracted structured data 

o Confidence scores 

o Processing status 

You may use a real OCR library/service or a mocked component—describe your choice. 

**4. Review & Human-in-the-Loop** 

Reviewers should have a dedicated workflow to validate low-confidence data. • Show extracted fields, their confidence, and the scanned image/PDF. 

• Allow reviewers to correct values and finalize the document. 

• Track actions in an audit log (e.g., extraction complete, field corrected, finalized).  
**5. Search & Case View** 

• Provide basic search capabilities across processed intakes (e.g., by name, date, template  type). 

• Include a simple case view that aggregates all documents related to a person/case. 

**6. RAG + Vector Database (Required)** 

You must integrate a vector database and implement Retrieval-Augmented Generation (RAG) to  support Similar Case Context during document review. 

**Requirements** 

• Construct a dataset of processed cases large enough to produce meaningful semantic search  results (can be synthetic or randomly generated). 

• Generate embeddings for: 

o OCR text 

o Extracted field values 

o Reviewer notes (if any) 

• During review, surface **Similar Cases** using vector search: 

o Retrieve the most semantically similar historical cases. 

o Present these to the reviewer along with short AI-generated context or summaries. o This output should help the reviewer identify patterns (e.g., recurring needs, similar  demographics, similar responses). 

• You have full freedom in how you structure: 

o Embedding strategy 

o Chunking 

o Similarity scoring 

o UI presentation 

o How RAG interacts with the main dataset 

**7. Multi-Tenancy (Required)** 

Implement **basic multi-tenancy** so that multiple agencies, departments, or organizations can use the  system without seeing each other’s data. 

• You choose the tenancy model (e.g., database-per-tenant, schema-per-tenant, or row-level  tenant isolation). 

• Authentication/authorization should propagate tenant context across services and APIs. • All major operations (templates, uploads, processing results, vector search, review actions)  must respect tenant boundaries. 

• RAG and vector embeddings should be isolated per tenant unless you intentionally design a  shared model and justify it. 

Keep the implementation simple but conceptually correct. 

**8. Observability & Resilience** 

• Add structured logging, correlation IDs, and basic metrics (request counts, extraction failures,  review activity). 

• Include API health checks. 

• Background workers should retry transient failures.  
**9. Tests** 

Provide a representative test suite including: 

• Unit tests 

• API integration tests 

• Worker/queue tests 

• Light UI smoke tests 

• (Optional) tests for your RAG pipeline 

Not exhaustive—just enough to demonstrate competence and thoughtful coverage. 

**10. Deliverables** 

• Source code, cleanly organized. 

• Docker Compose to run the API, worker, DB, front-end, and vector DB. 

• API documentation (Swagger/OpenAPI). 

• Architecture / Rationale document including: 

o Diagram 

o Component responsibilities 

o Template + extraction model design 

o RAG design and embedding strategy 

o **Multi-tenancy strategy and implications** 

• Instructions to run the application locally and load sample data. 

• Self-assessment describing what you completed, what’s missing, design trade-offs, and where  AI assisted your work (with example prompts). 

**Evaluation Criteria** 

• Architecture clarity and modularity 

• Code quality and separation of concerns 

• Workflow correctness (upload → extract → review → finalize) 

• Depth of thought in the RAG Similar Case Context implementation 

• Correct and secure multi-tenancy boundaries 

• UX of the review screen 

• Observability and resilience 

• Completeness and quality of documentation 

• Effective use of AI development tools 

**Submission** 

1. Send a zip file with the code to the evaluator.

2. or share a GitHub project  

3. or a Google drive with the zip of the code
