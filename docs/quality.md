# Quality, evaluation and Definition of Done

## 1. Principle

One aggregate accuracy is not sufficient. The system must be evaluated at the same boundaries where it can fail:

1. source retrieval;
2. decision (`ANSWER / CLARIFY / HANDOFF...`);
3. answer support/applicability;
4. moderation;
5. routing;
6. state/handoff correctness;
7. quality audit;
8. recovery after failures.

A system that refuses every question can look «safe» and still be useless. Always measure precision/safety together with useful coverage.

## 2. Gold data

Target, if labeling capacity allows:

- 240 core scenarios total: 120 dev + 120 blind test;
- additional moderation set around 120 messages;
- quality-audit set around 40 cases independently reviewed by two people.

If time is shorter, reduce n and report it honestly. Never replace a human blind set with self-grading by the same model.

### Leakage controls

- split duplicate/near-duplicate families before tuning;
- remove service prefixes such as prefilled topic labels from free-text evaluation;
- do not use historical `Решение` as classifier input for request understanding;
- record which fields are available at inference time;
- gold answer is a manual claim/source spec, not the first model output;
- after inspecting blind failures, that set becomes regression data, not a fresh blind benchmark;
- synthetic cases are allowed but labeled as synthetic.

## 3. Retrieval evaluation

Compare on the same snapshot/split/config budget:

| Variant | Purpose |
|---|---|
| B0 dense top-k | simple measurable baseline |
| B1 FTS + dense + RRF | hybrid benefit |
| B2 B1 + reranker + corrected table fragments | ranking/structure benefit |
| B3 B2 + answerability + condition cards + post-check | final safety/decision layer |

Metrics to record when gold evidence is available:

- Recall@k;
- MRR/nDCG where useful;
- exact code/status retrieval success;
- latency p50/p95;
- retrieval failures by category.

Do not claim system superiority if only coverage was reduced through more abstention.

## 4. Decision evaluation

Every gold case has expected decision class plus conditions/reason.

Metrics:

- confusion matrix;
- precision/recall/F1 per decision;
- false `ANSWER` rate on unanswerable cases;
- unnecessary handoff rate on clearly answerable cases;
- clarification usefulness (question changes branch vs redundant question).

The most expensive error is typically an unsafe/wrong confident `ANSWER`, but exact costs are a product policy, not an invented probability.

## 5. Grounded answer evaluation

For answerable cases check separately:

- required source retrieved;
- cited fragment actually supports claim;
- applicability conditions preserved;
- critical exceptions preserved;
- codes/numbers/durations unchanged;
- no invented Portal state/action;
- answer is concise enough for user.

Do not use presence of citation as proof of entailment.

## 6. Moderation evaluation

At minimum cover:

- direct profanity;
- common obfuscation;
- separators/repeated characters;
- substrings inside benign words;
- ordinary anger/criticism without profanity;
- quoted/ambiguous cases according to agreed policy.

Metrics:

- precision;
- recall;
- false-positive rate;
- deterministic rule coverage.

Store failing examples as regression cases.

## 7. Routing evaluation

If official L1/L2 labels are absent, do not publish a supervised accuracy as if it were ground truth.

For policy-based expert/gold labels use:

- macro-F1;
- per-line precision/recall;
- confusion matrix;
- auto-route coverage above chosen threshold;
- low-confidence fallback rate.

Topic classification, if added, is a helper and must not be confused with line routing.

## 8. State and reliability tests

Mandatory regression examples around state:

- `ANSWER` leaves resolution `UNKNOWN` until explicit confirmation;
- positive usefulness feedback does not resolve case;
- same idempotency key + same payload repeats result;
- same key + different payload conflicts;
- stale running turn cannot publish after new user revision;
- second handoff cannot bypass active/accepted one;
- timeout does not convert `PENDING` to accepted;
- moderation close preserves already accepted handoff;
- infrastructure failure is `TECHNICAL_ERROR`, not «knowledge missing»;
- `knowledge` timeout / 5xx / schema-invalid body on any stage → `TECHNICAL_ERROR` with `KnowledgeFailure` category, persisted stage events preserved;
- `knowledge` down + human request → handoff package still built from persisted fields;
- UI restore reads state from API after reload.

Where these tests run (ADR-0001 §3, rule 8):

- state/idempotency invariants — `TenderHack.Domain.Tests` / `Application.Tests` (no DB, no HTTP);
- `evals/decisions` — HTTP against `api` with `knowledge` in stub/fixture mode returning recorded `retrieve`/`answerability`/`verify` payloads; decision logic is never re-implemented in Python for test convenience;
- `evals/retrieval`, `evals/quality` — Python, directly against `knowledge`;
- E2E (§11) — full Compose.

## 9. Quality evaluator

Do not output a single employee rating in P0.

Dimensions:

- factual support;
- completeness;
- clarity;
- next step.

Values:

- 0 — explicit problem;
- 1 — partial;
- 2 — sufficient;
- UNKNOWN — insufficient data;
- NOT_APPLICABLE — dimension irrelevant.

A critical unsafe action is a separate `critical_error` flag; it cannot be averaged away by good clarity.

For each evaluator output require:

- quote/evidence from evaluated text;
- source if factual correctness can be checked;
- concise reason;
- limitation;
- concrete improvement if applicable.

Where possible compare evaluator against human rubric and report agreement, not «AI objectivity».

## 10. Repeated issues analytics

A group card must expose:

- group definition / label;
- n;
- representative examples;
- available negative/unresolved signals;
- data limitations;
- hypothesis, clearly marked as hypothesis.

A cluster is not automatically an incident or bug.

## 11. Critical E2E suite

Keep a small suite that can run before demo/release:

1. Typical navigation question with typo → grounded answer + source opens.
2. Similar topic but missing applicable answer → no hallucination; handoff offer.
3. Condition changes branch (e.g. duration threshold) → correct decision.
4. Similar but different error code → no substitution.
5. Direct human request → no forced FAQ loop.
6. Profanity → moderation close.
7. Handoff success → pending then accepted/simulated accepted.
8. Handoff timeout/failure → honest failure and safe retry.
9. Generator unavailable + human request → handoff path still possible where DB/adapter work.
10. Positive usefulness without resolution confirmation → resolution remains unknown.
11. Source recommending external support → no fake Portal/internal dispatch.
12. Offline/external internet disabled → normal demo remains functional.

## 12. Component Definition of Done

### Ingestion

Done only if:

- all provided docs registered;
- source/version/page anchors preserved;
- critical tables visually checked;
- fragment ID can resolve to original source;
- extraction failures are explicit.

### Retrieval

Done only if:

- baseline comparison exists;
- exact code/op typo regressions exist;
- historical resolution corpus cannot leak into normative retrieval;
- config/snapshot version recorded.

### Answerability / generation

Done only if:

- decision regression set passes target gate;
- unsupported codes/numbers/actions are rejected;
- citation/support validation runs before publish;
- failure path produces handoff/technical error, not unsafe answer.

### Routing

Done only if:

- line/channel semantics are explicit;
- reasons stored;
- low-confidence behavior defined;
- no invented ground-truth claim.

### Moderation

Done only if:

- deterministic rules versioned;
- false-positive cases tested;
- close state implemented server-side;
- retrieval/generation not executed after confirmed close.

### Handoff

Done only if:

- summary is inspectable/editable;
- pending/accepted/simulated/failed are distinct;
- idempotency works;
- adapter timeout/failure tested;
- UI never claims real dispatch in demo mode.

### Quality analytics

Done only if:

- source type/data sufficiency captured;
- UNKNOWN/NA supported;
- feedback separated from resolution;
- no personal ranking claim;
- group conclusions disclose limitations.

### Frontend

Done only if:

- happy, clarify, handoff, moderation, technical error and restore states render;
- source opens correctly;
- no dead controls for unimplemented features;
- progress is semantic, not fake percentage;
- state comes from API.

### Ops

Done only if:

- documented local start;
- health checks;
- secrets outside git;
- organizer data/model weights mounted, not committed;
- reproducible seed/demo path;
- external internet can be disabled for normal demo.

## 13. Verification commands

The repository currently contains documentation only after the scaffold reset. **Do not invent passing commands.**

The first scaffold change must add and document actual commands here, expected roughly as:

```bash
# .NET support core (apps/api)
dotnet restore
dotnet format --verify-no-changes
dotnet build -warnaserror
dotnet test

# Python knowledge service (apps/knowledge)
uv sync --frozen
uv run ruff check .
uv run <type-checker> ...
uv run pytest ...

# Contract v0
<documented command to export knowledge OpenAPI and regenerate the C# client; CI fails if generated client is stale>

# Web
pnpm install --frozen-lockfile
pnpm lint
pnpm test --run
pnpm build

# Integration/e2e
<documented docker/eval commands>
```

Exact commands must match real manifests/configs before becoming mandatory.

## 14. Release gate

Before demo/submission:

- fresh environment starts;
- migrations apply;
- models mount/load with recorded revisions;
- core E2E suite repeats without manual DB edits;
- benchmark tables show actual n/config;
- failures/limitations listed;
- README commands verified;
- BPMN and architecture reflect actual implementation;
- skeptic review has no unresolved critical/high finding.
