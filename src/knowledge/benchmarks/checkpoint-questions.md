# K2D checkpoint questions

These questions are deliberately kept outside the retrieval gold set. Until
organizers answer them, the audit uses the conservative assumptions below.

## 1. Can public Portal KB articles be additional normative data?

- WHY IT MATTERS: The six attached manuals do not necessarily cover every
  recurring portal operation; adding public articles changes corpus scope and
  provenance requirements.
- CURRENT IMPLEMENTATION ASSUMPTION: Search only the six approved manuals in
  the `NORMATIVE` snapshot. Public articles are not normative until explicitly
  approved and versioned.
- BLOCKS DEVELOPMENT: No. The audit can report misses and continue.

## 2. Is historical STP “Решение” only analytics/history or normative?

- WHY IT MATTERS: Treating a historical resolution as an answer source would
  violate corpus separation and can surface stale or case-specific actions.
- CURRENT IMPLEMENTATION ASSUMPTION: `Решение` is historical analytics only. It
  is used, when present, only as a coarse `HUMAN_ACTION_LIKELY` signal and is
  never sent to retrieval or used as answer evidence.
- BLOCKS DEVELOPMENT: No.

## 3. Which real support lines/queues should be offered to employees?

- WHY IT MATTERS: A coverage gap can be answerable only by the correct human
  line; the export does not establish a stable routing contract.
- CURRENT IMPLEMENTATION ASSUMPTION: Do not infer line, queue, specialist, or
  SLA from STP fields. Handoff remains a .NET-owned package with an explicit
  route only when supplied by an approved adapter contract.
- BLOCKS DEVELOPMENT: No for retrieval coverage; yes for real routing.

## 4. Is profanity handling close-first or warning-first (and on repetition)?

- WHY IT MATTERS: A moderation policy changes lifecycle outcomes, but the
  historical export cannot prove the intended product rule.
- CURRENT IMPLEMENTATION ASSUMPTION: Do not change the repository policy in
  K2D. Keep the current source-of-truth behavior and leave warning thresholds
  to the pending moderation decision.
- BLOCKS DEVELOPMENT: No for this audit.

## 5. Is a simulated handoff sufficient for the demo, and which status fields
   are available in a real adapter?

- WHY IT MATTERS: Coverage cannot be called a human resolution when the
  adapter acknowledgement, specialist, or stage is not real.
- CURRENT IMPLEMENTATION ASSUMPTION: Mark simulated/future integration
  explicitly; never invent specialist, SLA, or stage values.
- BLOCKS DEVELOPMENT: No for retrieval; yes for production handoff claims.

## 6. How should the 778 free-text STP “Тема” values map to the 9-theme / 86-
   subcategory taxonomy?

- WHY IT MATTERS: The export has no proven foreign key to the supplied
  taxonomy. A guessed mapping would bias per-theme coverage and could silently
  turn historical labels into retrieval hints.
- CURRENT IMPLEMENTATION ASSUMPTION: Use exact/containment matches only when
  unambiguous; retain all other rows in an explicit `UNMAPPED_OR_AMBIGUOUS`
  bucket. Do not use the field to form retrieval queries.
- BLOCKS DEVELOPMENT: No for this audit; yes for authoritative per-subtheme
  reporting and future routing analytics.
