# Hackathon requirements — stable constraints

This document preserves the useful formal requirement layer from the previous repository notes **without preserving their obsolete architecture choices**.

If organizers/experts provide a newer explicit requirement, that newer requirement wins and this file must be updated.

## 1. Case goal

Build an intelligent system for automating support requests for the Supplier Portal (`zakupki.mos.ru`).

Required task families:

1. Study organizer-provided data.
2. Structure the informal knowledge base from manuals/instructions/other supplied sources.
3. Automatically consult users on typical/frequent questions.
4. If confirmed information is absent, inform the user and offer support/operator escalation instead of inventing an answer.
5. Detect profanity and automatically end the current violating interaction with a clear notification, according to the agreed policy.
6. Identify/rout requests for different support lines.
7. Provide a methodology for evaluating available technical-specialist answers / quality of communication using positive/negative feedback and produce meaningful conclusions for systemic-problem analysis.
8. Provide an intuitive web UI.
9. Test on real cases.
10. Design for scale.
11. Demonstrate a working project.
12. Present the project and BPMN process scheme.

## 2. User-facing capabilities explicitly expected

The product should support, where data/requirements permit:

- natural-language Russian questions, including typos;
- relevant answer from structured knowledge with traceable source;
- explicit no-answer/insufficient-information path;
- offer to transfer a complex request to support;
- moderation close for confirmed profanity;
- visible addressee/route and processing state where applicable;
- feedback after response/resolution;
- meaningful conclusions for repeated/systemic issues in a separate protected/read-only view.

Do not infer capabilities that were not requested, such as modifying Portal contracts/documents or real operator chat infrastructure.

## 3. Infrastructure constraints

Stable constraints from reviewed materials:

- Linux deployment.
- Web interface.
- Intelligent contour runs on team-controlled servers.
- No external search APIs for the solution's intelligent pipeline.
- No external LLM APIs (OpenAI/Google/Anthropic/etc.) in the normal runtime.
- No low-code/no-code as the core implementation.
- Open models are permitted; model size should be rational for available resources and measured rather than selected for prestige.
- Reproducibility and scalability must be explainable.

Organizer-provided/raw data must not be published or committed to git unless explicitly permitted.

## 4. Scoring map from reviewed requirement materials

The current reviewed specification records the following block weights for the stage:

| Block | Weight | What our implementation must prove |
|---|---:|---|
| Working prototype / functionality | 40 | end-to-end scenario without hidden manual substitution |
| Knowledge structuring and answers | 20 | real instructions, conditions, typos, grounded answer and correct insufficient-info behavior |
| Routing and moderation | 10 | explainable support-line handling and profanity policy |
| Specialist-quality methodology | 20 | separated quality/outcome/feedback analysis with meaningful systemic conclusions |
| Web UI | 10 | clear answer, route/status/next step; usable on real demo cases |

Weights are planning inputs, not a promise that any feature automatically earns points.

### Resource implication

Do not sacrifice the working vertical flow for an isolated model improvement or visual effect. Functionality has the largest block.

## 5. Defense / review expectations

Reviewed materials specify:

- first presentation stage: about 5 minutes, with BPMN and live/demo activity in parallel;
- top solutions proceed to repository/code review / extended defense;
- project claims must be backed by actual implementation or explicitly marked as planned/simulated.

Therefore:

- demo must be rehearsed and understandable without verbose commentary;
- source code must be legible to an external reviewer;
- metrics must include dataset size/config/limitations;
- simulated handoff/human replies must be explicitly labelled;
- architecture/BPMN must match current code, not a concept deck.

## 6. Requirement/source priority

Use this order when requirements conflict:

1. Latest explicit organizer requirement / expert clarification.
2. Supplied task presentation/audio clarification materials.
3. Real provided data and manuals.
4. `docs/product-spec.md` team policy for ambiguous areas.
5. Architecture/stack implementation choices.

A previous scaffold or older repository note never overrides a newer formal requirement.

## 7. Known scope clarification from the final review

Current final review explicitly does **not** require for P0:

- a full manager/operator cabinet;
- real back-and-forth chat with a production operator;
- deep L2 internal specialization;
- implemented L3 engineering workflow;
- multilingual support;
- mandatory screenshot recognition.

If a mentor explicitly changes one of these, update this file, the product spec and execution plan together.

## 8. BPMN expectation

BPMN is a business-process artifact, not a backend component diagram.

At minimum model:

### User lane/pool

- submit request;
- answer clarification;
- confirm transfer / feedback.

### AI support system

- moderation;
- understand/context;
- search/applicability check;
- answer/clarify/handoff decision;
- prepare/submit handoff.

### Human support

- receive prepared case;
- resolve/respond/close where simulated/future flow is shown.

### Quality/analytics

- evaluate available answer/result/feedback;
- group repeated issues;
- surface hypothesis/insight.

Use gateways for profanity, answerability, clarification, handoff confirmation and feedback/systemic-analysis conditions. Analytics must not block the primary response path.

## 9. Team clarifications applied to the formal items (2026-09-12)

These are **team decisions** that interpret the formal items above; they are not organizer facts. If an organizer/expert clarification contradicts one, the organizer wins and this section plus `product-spec.md` are updated together.

The scored chat functionality is fixed as seven user-visible behaviours:

| # | Behaviour | Formal item(s) | Team interpretation |
|---|---|---|---|
| 1 | Ask in chat, get an answer | §1.3, §2 | core turn |
| 2 | Source button in the answer when a fragment was used | §2 «traceable source» | `fragment_id` → `GET /api/v0/sources/{id}` |
| 3 | Explicit «no confirmed answer» + «Обратиться к оператору поддержки» button | §1.4, §2 | `HANDOFF_OFFER` |
| 4 | Profanity: one warning in chat, chat closed on repeat | §1.5 «according to the agreed policy» | warning-first, `product-spec.md §14` |
| 5 | Notification when the case is completed | §2 «processing state» | in-app inbox + owner SSE + browser notification; no push/email (ADR-0002) |
| 6 | After handoff: widget with status, specialist and stage | §2 «visible addressee/route and processing state» | status always; specialist/stage **only when the adapter reports them**, demo-labelled when simulated |
| 7 | After completion: archive + feedback (specialist, information quality, solved, comment) + systemic conclusions | §1.7, §2 | four independent signals; no personal ranking (§7) |

§7 remains in force: no operator cabinet, no real operator chat. Item 6 therefore depends on the adapter contract (`contracts/support-adapter-v0.md`), not on an operator UI.

## 10. What this document intentionally excludes

It does not specify:

- programming language;
- microservices;
- model name;
- vector database;
- exact UI component library;
- agent framework.

Those are implementation decisions recorded separately in `docs/stack.md` and `docs/architecture.md` and may change after benchmark/hardware evidence without altering formal requirements.
