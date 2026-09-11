# Agent development workflow

This document defines how coding agents should work in this repository. It applies to Codex-style agents and any other harness with repository/file/tool access.

The core idea follows current OpenAI agent-first guidance:

- repository knowledge is versioned and discoverable;
- `AGENTS.md` is a map, not a monolithic handbook;
- the root agent behaves like a manager;
- independent work may be delegated to subagents in parallel;
- reviews are iterative and often agent-to-agent;
- testing is proportional to the risk of the change;
- humans steer intent and acceptance criteria; agents execute, verify and surface uncertainty.

## 1. Root-agent responsibility

The root agent owns:

- task interpretation;
- final plan;
- allocation of subagent work;
- integration;
- conflict resolution;
- self-review;
- skeptic review response;
- final verification;
- documentation sync;
- final user-facing summary.

Subagents accelerate work. They do not remove accountability from the root agent.

## 2. Default loop

For nontrivial changes:

```text
1. DISCOVER
2. DEFINE ACCEPTANCE
3. PLAN
4. DELEGATE independent work
5. IMPLEMENT
6. SELF-CHECK
7. SKEPTIC REVIEW
8. REPAIR
9. VERIFY
10. DOCUMENT
11. FINAL CHECK
```

Do not mechanically run all 11 stages for a typo-only change. Scale process to risk.

## 3. DISCOVER

Before editing:

1. Read `AGENTS.md`.
2. Read `docs/index.md`.
3. Open only the relevant source-of-truth docs.
4. Inspect the actual files/code path involved.
5. Inspect current git status/diff.
6. Identify existing tests/evals closest to the behavior.
7. Write down facts vs assumptions.

Questions to answer:

- What is the user-visible behavior?
- What domain state changes?
- What source of truth controls this behavior?
- What can fail?
- What data boundary is crossed?
- Is this P0/P1/non-goal?

## 4. Acceptance criteria before code

A task is not ready for implementation until acceptance is concrete enough to falsify the solution.

Good criteria:

- `ANSWER` is not emitted for an unanswerable gold case.
- handoff remains `PENDING` until adapter acknowledgement.
- exact error code with leading zero is not normalized into another code.
- source drawer opens the same fragment ID recorded in decision log.

Weak criteria:

- «Improve RAG quality».
- «Make architecture cleaner».
- «Use agents».

For bugfixes, the failing example is part of acceptance whenever practical.

## 5. Execution plans

Create a plan for work that:

- spans multiple modules;
- changes a contract/state/data model;
- has several independent workstreams;
- contains an experiment/benchmark;
- may take more than one coding session.

Suggested plan template:

```markdown
# <task>

## Goal

## Non-goals

## Acceptance criteria

## Relevant docs/contracts

## Risks / unknowns

## Workstreams
- A — owner/root/subagent
- B — ...

## Implementation steps

## Verification

## Decisions made

## Progress

## Open issues
```

Keep the plan live. Do not write it once and ignore it.

## 6. Subagent strategy

Use subagents when tasks are independent enough that parallel execution saves time or an independent viewpoint improves quality.

### 6.1. Manager pattern

Preferred pattern:

```text
                 Root / Manager
               /       |        \
        Scout/Research  Implement  Reviewer
               \       |        /
                 Root integrates
```

The root agent remains the only integrator and owner of final state.

Avoid decentralized handoffs where no agent retains global context.

### 6.2. Parallelize these tasks

Good parallel work:

- inspect different independent modules;
- benchmark two retrieval approaches;
- verify data schema separately from API implementation;
- frontend and backend work against a frozen contract;
- review security/state correctness while another agent runs tests;
- prepare skeptic review after implementation;
- inspect docs for drift.

### 6.3. Do not parallelize these blindly

- two agents editing the same file;
- two agents redesigning the same state model;
- migration and domain model independently without frozen schema;
- multiple agents changing shared API types concurrently;
- open-ended «improve everything» tasks.

### 6.4. Subagent task contract

Every delegated task should include:

```text
GOAL
SCOPE / allowed files or read-only
RELEVANT DOCS
INPUTS / known facts
EXPECTED OUTPUT
DO NOT DO
VERIFICATION
```

Bad delegation:

> «Посмотри backend и улучши».

Good delegation:

> «Read-only review of handoff state transitions in X/Y. Check against docs/product-spec.md §16–17. Return only findings with severity, file/line evidence and reproduction idea. Do not edit files.»

### 6.5. Default subagent roles

These are modes, not permanent personas.

#### Scout

Goal: gather evidence fast.

- read-only;
- traces actual code/data paths;
- reports facts and unknowns;
- does not propose broad refactors unless asked.

#### Specialist Implementer

Goal: implement a bounded independent workstream.

- explicit allowed scope;
- must return changed files + tests + assumptions;
- should not change shared contracts without root coordination.

#### Verifier

Goal: independently validate behavior.

- runs/inspects targeted tests, logs, fixtures, benchmarks;
- tries to reproduce failure;
- does not assume implementation intent is correct.

#### Skeptic

Goal: attack the solution after root self-review.

- read-only by default;
- assumes the patch may be wrong;
- searches for spec drift, unsafe assumptions and edge cases;
- findings, not rewrite.

## 7. Self-check loop

Before asking another agent to review, the implementer/root performs a local self-check.

### 7.1. Diff review

Read the whole diff, not only edited snippets.

Ask:

- Did I implement more than requested?
- Did I change semantics accidentally while refactoring?
- Did I add a dependency/service without documentation?
- Does error behavior still match the product spec?
- Is the code easy for a future agent to inspect?

### 7.2. Product invariants

Check relevant invariants:

- source provenance preserved;
- user-reported != verified Portal state;
- historical solutions not normative;
- no answer from similarity alone;
- `ANSWER != RESOLVED`;
- `prepared != accepted handoff`;
- infrastructure error != no knowledge;
- no chain-of-thought exposure.

### 7.3. Boundary review

For each input/output boundary:

- schema parsed?
- unknown fields handled?
- invalid shape fails predictably?
- secrets/private data excluded from logs?
- retries/idempotency clear for state changes?

## 8. Skeptic review protocol

### 8.1. When mandatory

Skeptic review is required for changes that touch any of:

- state machine;
- retrieval/answerability;
- prompt/generation verification;
- handoff/idempotency;
- data model/migration;
- moderation;
- security/privacy;
- new dependency/service/model;
- evaluation methodology;
- architecture docs.

It is optional for obvious docs typo/formatting changes.

### 8.2. Skeptic prompt template

Use an instruction equivalent to:

```text
You are an adversarial reviewer. Assume the proposed change is subtly wrong.
Read the task, acceptance criteria, relevant source-of-truth docs and the full diff.
Do not praise the patch and do not rewrite it.
Find concrete failure modes, unsupported assumptions, spec drift, missing edge cases,
state/idempotency/race bugs, security/data leaks, misleading success states,
tests that merely mirror implementation, and unnecessary complexity.
For every finding return:
- severity: critical/high/medium/low
- evidence: file/line or exact behavior
- why it matters
- minimal reproduction/verification
- suggested direction, not full implementation
If no meaningful findings remain, say so explicitly and list what you checked.
```

### 8.3. Root response

For every critical/high/medium finding:

- fix it; or
- record why it is not applicable with evidence.

Then rerun targeted verification.

Repeat skeptic review once if material code changed. Do not loop forever: after two normal rounds continue only if critical/high findings remain.

## 9. Verification strategy

OpenAI model guidance recommends calibrating verification effort rather than running every test repeatedly. Apply that here.

### 9.1. Verification pyramid

1. **Static/local:** type/lint/schema validation.
2. **Unit:** domain rules, parser helpers, deterministic moderation.
3. **Component:** repository/retrieval/adapters with real dependencies where useful.
4. **Eval:** gold set for retrieval/decisions/moderation/quality.
5. **E2E:** a small set of critical user journeys.
6. **Manual/demo:** only where visual/hardware behavior cannot be cheaply automated.

### 9.2. Order

After change:

- run narrow relevant checks first;
- if they pass and change is local, stop broadening unless risk justifies it;
- run broader suites for shared boundaries, migrations, state machines, core retrieval/security;
- do not rerun identical tests repeatedly without a new change/failure.

### 9.3. Meaningful tests

A test is valuable when it can fail for a plausible regression and asserts externally meaningful behavior.

Avoid tests whose assertion is essentially «mock returned X so function returned X» unless verifying an important adapter contract.

## 10. Experiment discipline

For retrieval/model/routing experiments:

- freeze dataset split/snapshot;
- record config/version;
- compare against baseline;
- report n;
- do not tune on blind set;
- separate quality gain from coverage reduction;
- save failures, not only aggregate metrics.

A model change without benchmark evidence is a hypothesis, not an upgrade.

## 11. Documentation discipline

If an agent learns an important project-specific rule from debugging or mentor clarification, that knowledge must be written into repository docs where future agents can find it.

Do not stuff discoveries into `AGENTS.md` unless they are global workflow/invariant rules.

Examples:

- new domain exception → `product-spec.md` + regression case;
- new state invariant → `architecture.md`;
- new model/runtime decision → `stack.md`;
- new eval rule → `quality.md`;
- new execution dependency → active plan.

## 12. Agent communication

Subagent messages are operational artifacts and should be legible:

- use exact file/module names;
- distinguish observed fact from inference;
- include failed attempts;
- do not report «done» without verification;
- report blockers immediately to root;
- do not hide uncertainty behind confidence language.

## 13. Completion report

Before marking a task complete, root agent should be able to state:

- what changed;
- why it matches acceptance;
- what tests/evals actually ran;
- what did not run and why;
- remaining known risks;
- docs updated;
- git status/diff state.

For a user-requested commit/push, verify target branch and HEAD again immediately before updating the ref.

## 14. Product-agent distinction

Important terminology:

- **coding subagents** in this document = development acceleration/review tools;
- **product orchestrator** = runtime logic of the TenderHack application.

The use of coding subagents does **not** justify implementing the product as a multi-agent swarm. Runtime architecture remains the explicit state machine described in `docs/architecture.md`.
