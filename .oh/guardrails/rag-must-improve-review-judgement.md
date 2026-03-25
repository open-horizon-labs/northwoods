---
id: rag-must-improve-review-judgement
severity: hard
statement: "RAG must improve reviewer judgment inside the review workflow, not exist as a disconnected technical demo."
outcome: trustworthy-exception-based-intake-review
---

This protects against ornamental AI.

Why it matters:
- The assignment explicitly evaluates the depth of thought in the Similar Case Context implementation.
- A vector database and embeddings are not the outcome; better reviewer decisions are.
- This aligns with my personal aims around effectiveness and leverage: AI should change what people can do, not just satisfy a checklist.

Implications for delivery:
- Similar cases should appear where the reviewer is already working.
- Retrieved context should be clearly tied to the current case through summaries or comparison cues.
- The sample corpus must be rich enough that semantic retrieval returns plausible patterns rather than random noise.
- If retrieval quality is weak, narrow the scope and improve the data/model path before adding more surface area.

This guardrail keeps the system focused on capability value rather than AI theater.