---
id: architecture-must-follow-capabilities
severity: soft
statement: "Architecture must follow durable capabilities and trust boundaries, not rubric theater or thin technical-layer services."
outcome: cto-judgement-legible-in-system
---

This guards the shape of the system.

Why it matters:
- The exercise asks for Clean Architecture and microservices, but the real judgment test is whether the decomposition makes the workflow clearer and safer.
- Splitting by technical layer or creating too many tiny services would increase coordination cost while reducing the clarity of ownership.
- This aligns with my personal aim to enable alignment and resilience in how people work: good boundaries create calmer execution, clearer responsibilities, and systems that can evolve without thrash.

Implications for delivery:
- Service boundaries should map to business capabilities such as identity/tenancy, intake processing, review/case management, and retrieval intelligence.
- Cross-service contracts should be explicit and few.
- Each boundary should earn its existence by protecting a trust boundary, scaling concern, or cognitive boundary.
- If a boundary exists only to look like microservices, remove it.

This is how the design demonstrates strategic vision instead of architecture cosplay.