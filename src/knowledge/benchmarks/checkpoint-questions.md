# K2D checkpoint questions

These questions are deliberately kept outside the retrieval gold set. Some were unresolved during the K2D audit and have since been answered by repository policy; resolved items are kept here only to preserve the assumptions under which the historical audit ran. Current product truth is always the source-of-truth docs, not this checkpoint file.

## 1. Can public Portal KB articles be additional normative data?

- WHY IT MATTERS: The six attached manuals do not necessarily cover every recurring portal operation; adding public articles changes corpus scope and provenance requirements.
- CURRENT IMPLEMENTATION ASSUMPTION: Search only the six approved manuals in the `NORMATIVE` snapshot. Public articles are not normative until explicitly approved and versioned.
- BLOCKS DEVELOPMENT: No. The audit can report misses and continue.

## 2. Is historical STP “Решение” only analytics/history or normative?

- WHY IT MATTERS: Treating a historical resolution as an answer source would violate corpus separation and can surface stale or case-specific actions.
- CURRENT REPOSITORY DECISION: `Решение` is historical analytics only. It is never sent to normative retrieval or used directly as answer evidence; any future knowledge-card proposal requires explicit review/publication into a new normative snapshot (`product-spec.md §6`, `product-experience.md §8`).
- BLOCKS DEVELOPMENT: No.

## 3. Which real support lines/queues should be offered to employees?

- WHY IT MATTERS: A coverage gap can be answerable only by the correct human line; the export does not establish a stable real routing/integration contract.
- CURRENT IMPLEMENTATION ASSUMPTION: Do not infer real queue, specialist or SLA from STP fields. Routing policy can recommend L1/L2 according to current Domain rules, while actual dispatch/status facts come only from the approved support adapter.
- BLOCKS DEVELOPMENT: No for retrieval; real Portal routing/integration remains external-dependency work (`docs/open-decisions.md OD-003`).

## 4. Is profanity handling close-first or warning-first (and on repetition)?

- STATUS: **RESOLVED 2026-09-12**.
- CURRENT REPOSITORY DECISION: warning-first — first confirmed violation produces `MODERATION_WARNING`; a repeat closes the chat. Threshold is server-configurable. Source of truth: `docs/product-spec.md §14` and `docs/open-decisions.md OD-001`.
- HISTORICAL NOTE: K2D did not change moderation while this policy was still pending.

## 5. Is a simulated handoff sufficient for the demo, and which status fields are available in a real adapter?

- WHY IT MATTERS: Coverage cannot be called a real human resolution when adapter acknowledgement, specialist or stage is simulated.
- CURRENT REPOSITORY DECISION: P0 has an explicitly labelled demo adapter for reproducible flows; simulated status is never represented as real. Whether/when a production Portal adapter is available remains external dependency `OD-003`.
- BLOCKS DEVELOPMENT: No for the demo flow; yes for production integration claims.

## 6. How should the 778 free-text STP “Тема” values map to the 9-theme / 86-subcategory taxonomy?

- WHY IT MATTERS: The export has no proven foreign key to the supplied taxonomy. A guessed mapping would bias per-theme coverage and could silently turn historical labels into retrieval hints.
- CURRENT IMPLEMENTATION ASSUMPTION: Use exact/containment matches only when unambiguous; retain all other rows in an explicit `UNMAPPED_OR_AMBIGUOUS` bucket. Do not use the field to form normative retrieval queries.
- BLOCKS DEVELOPMENT: No for the historical audit; yes for authoritative per-subtheme reporting and any future supervised routing claim.
