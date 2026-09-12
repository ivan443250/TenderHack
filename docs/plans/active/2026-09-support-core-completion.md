# Support Core (.NET) — доведение до P0 и готовности к product-enhancement

Status: **ACTIVE**. Создан 2026-09-12 по итогам сверки `docs/` с фактическим кодом `src/support-core` на коммите `42de5a4`.

Это execution plan по `docs/agent-workflow.md §5`. Он не переопределяет `product-spec.md`, `architecture.md` и frozen contracts; каждый пункт ссылается на источник, которому код сейчас не соответствует, или на документированный target, который ещё не реализован.

## Goal

Закрыть расхождения между .NET Support Core и source-of-truth так, чтобы:

1. каждый P0-пункт `product-spec.md §3`, за который отвечает `.NET api`/`api-worker`, работал на реальном (не fixture) Knowledge-пути и был покрыт тестом;
2. поведение соответствовало `web-api-v0`, `support-adapter-v0`, `knowledge-v0` без «тихого» drift;
3. документированные targets из `architecture.md §7` либо были реализованы, либо остались явно помеченными как known debt;
4. появилась серверная основа для первых product-enhancement фич из `product-experience.md §11` (Applicability Card, Smart Recovery), не ломая core.

## Non-goals

- Изменение runtime boundary, добавление сервисов/очередей/БД (требует ADR).
- Логика decision/answerability в Python — она остаётся в .NET.
- Реальный Portal adapter (OD-003 открыт) — только demo adapter и port.
- Web Push / email (OD-004).
- Web UI — только coordinated contract changes, которые фронту нужны от API.
- Certification моделей (`model-stack-v2-real-certification.md`).

## Текущее состояние (факты на `42de5a4`)

Реализовано и покрыто тестами: `Case`/`Turn`/`Handoff` aggregate с явными переходами; warning-first moderation с deterministic rules + context-check; `TurnOrchestrator` (moderate → human-request → understand → retrieve → answerability → draft → verify → decision) с per-stage `case_events`; `IngestHandoffStatus` (poll + HMAC webhook), demo adapter со `staged` скриптом; completion (user/support/moderation), feedback (4 сигнала), notifications (inbox + owner SSE + ack), idempotency keys, `xmin`/unique-revision concurrency, outbox + 4 воркера, quality pushes (`turns`/`feedback`/`completions`), analytics proxy, owner cookie, EF migrations, Compose.

`dotnet test src/support-core/TenderHack.sln -c Release` — 224 passed (Domain 128, Application 61, Contract 14, Api.Tests 21 — требует Docker/Testcontainers) на текущей ветке после Фаз 0–4 (см. Progress ниже; открытыми остаются только явно помеченные known-debt пункты C3/C5-remainder/E2/E3/E4 и остаток D2 — `TenderHack.Worker.Tests`).

## Найденные gaps (с evidence)

### A. Дефекты и contract drift — исправить первыми

| # | Проблема | Где | Источник истины |
|---|---|---|---|
| A1 | `HandoffSubmissionPublisher` написан, но **нигде не вызывается** и не зарегистрирован в DI. После ack адаптера нет `HANDOFF_STATUS`/`HANDOFF_UPDATED`; браузер видит `PENDING` до первого poll (15 с + 10 с), при `FAILED` — навсегда | `src/support-core/src/TenderHack.Worker/HandoffSubmitWorker.cs`, `Worker/Program.cs`, `Application/Orchestration/HandoffSubmissionPublisher.cs` | `web-api-v0.md §4.2, §8`, `architecture.md §5.9` |
| A2 | `Case.CompleteBySupport`: `CANCELLED` → `UNRESOLVED` (контракт: resolution не меняется); терминальный факт на `CLOSED_USER` перезаписывает resolution безусловно (контракт: только из `UNKNOWN`). Тест покрывает только `UNKNOWN` | `Domain/Cases/Case.cs:171-190`, `Domain.Tests/CaseTests.cs:238` | `support-adapter-v0.md §6.3`, `quality.md §8` |
| A3 | `knowledge` при недоступной БД отвечает `200 INSUFFICIENT + risk_flags:["KNOWLEDGE_UNAVAILABLE"]`; .NET превращает это в `HANDOFF_OFFER` «подтверждённого ответа нет» | `Application/Orchestration/TurnOrchestrator.cs` (`DecideFromAnswerabilityAsync`), `src/knowledge/.../api/routes.py:231-239` | `architecture.md §16`, `product-spec.md §9` («infrastructure failure ≠ нет ответа») |
| A4 | `DirectHumanRequestDetector` ловит подстроку `оператор` → «оператор ЭДО» (базовый термин домена) даёт ложный `HANDOFF_OFFER` без retrieval | `Domain/Routing/DirectHumanRequestDetector.cs` | `product-spec.md §21` (regression: direct human request vs обычный вопрос) |
| A5 | Compose передаёт `Knowledge__BaseUrl`, а опция называется `BaseAddress` — override молча игнорируется, работает только потому, что default совпадает | `compose.yaml:28,50`, `Infrastructure/KnowledgeClient/KnowledgeServiceOptions.cs` | `quality.md §12 Ops` |
| A6 | `HandoffRequest` к адаптеру содержит только summary/queue/reason_codes/flag. Нет `channel`, `user_reported_context`, `verified_portal_context`, `already_tried`, `unknown_fields`, `sources_checked`, `handoff_reason`, `relevant_message_ids`, `integration_mode`. `prepare` не возвращает editable package и ничего не сохраняет — summary приходит только из браузера на `confirm` | `Application/Ports/IHandoffAdapter.cs`, `UseCases/PrepareHandoffUseCase.cs`, `Api/Endpoints/HandoffEndpoints.cs` | `support-adapter-v0.md §2`, `product-spec.md §17`, `web-api-v0.md §8` |
| A7 | `AI_ANSWER.sources` = `string[]` (только fragment_id); контракт требует `{fragment_id, title, page, label}`. Web сейчас типизирован под `string[]` — менять coordinated | `TurnOrchestrator.PublishAnswerAsync`, `src/web/src/api/types.ts` | `web-api-v0.md §4.3` |
| A8 | Ответ не сохраняет `snapshot_id`, `model_version`, `retrieval_config_version` (есть в памяти, теряются) — воспроизведение ответа невозможно | `TurnOrchestrator.PublishAnswerAsync`, `EnqueueQualityTurnPush` | `architecture.md §8 «Knowledge versioning»` |
| A9 | `MODERATION_WARNING` event несёт только `moderation_warning_count`: нет user-facing message, `rule_id`/`rule_version`, порога | `TurnOrchestrator.HandleModerationViolationAsync` | `product-spec.md §14` (текст предупреждения, хранить rule_id/version), `quality.md §12 Moderation` |
| A10 | `GET /api/v0/cases` всегда отдаёт `unread_notifications: 0` | `Api/Contracts/CaseMapper.ToListItem` | `web-api-v0.md §3` |
| A11 | `GET /api/v0/sources/{id}` не требует owner session | `Api/Endpoints/SourceEndpoints.cs` | `web-api-v0.md §11` |
| A12 | `HANDOFF_STATUS` из `IngestHandoffStatus` без `external_case_id`/`stale`/`updated_at` (контракт: payload = handoff view + `changed`) | `UseCases/IngestHandoffStatusUseCase.cs` | `web-api-v0.md §4.2` |

### B. Незакрытые P0-функции оркестрации

| # | Проблема | Источник истины |
|---|---|---|
| B1 | **Нет контекста между ходами.** `PriorTurnSummary` всегда `null`; ответ пользователя на `CLARIFY` («поставщик») уходит в `understand/retrieve` как самостоятельный вопрос. Не реализованы: «не спрашивать уже известное», «не более двух последовательных clarifications», «не спрашивать slot после “не знаю”», после исчерпания — handoff | `product-spec.md §7, §11`, `architecture.md §5.2` |
| B2 | Decision policy не согласована с реальным словарём `risk_flags` Knowledge. `RoutingPolicy` знает только `TECHNICAL_DIAGNOSIS_REQUIRED` (Knowledge его не эмитит); любой risk flag при `SUFFICIENT` → `ANSWER_AND_HANDOFF`; `HUMAN_SUPPORT_REQUIRED` приходит как `INSUFFICIENT` → обычный `HANDOFF_OFFER`, т.е. `ANSWER_AND_HANDOFF` на реальном пути практически недостижим. Реальные флаги: `HUMAN_SUPPORT_REQUIRED`, `ROLE_AMBIGUITY`, `CONFLICTING_EVIDENCE`, `WRONG_ROLE/STATUS/PROVIDER`, `FORBIDDEN_GENERALIZATION`, `HIGH_RISK_MISSING_CONDITION`, `NEGATED_QUERY`, `LOW_SPECIFICITY`, `POST_INSTRUCTION_FAILURE`, `MISSING_SOURCE`, `INVALID_EVIDENCE_REFERENCE`, `KNOWLEDGE_UNAVAILABLE` | `product-spec.md §10, §15`, `architecture.md §10` («mapping … в C# … покрыт `evals/decisions`») |
| B3 | Проваленный verify → сразу handoff с reason `INSUFFICIENT_EVIDENCE`. Спека: один ограниченный rewrite/extractive fallback, затем handoff; причина «verification failed» должна отличаться от «нет знаний» (нужно для Knowledge Gap Radar) | `product-spec.md §12`, `product-experience.md §8` |
| B4 | Нет «максимум одно дополнительное расширение поиска» | `product-spec.md §8 п.9` |
| B5 | Один глобальный `Knowledge:Timeout = 10s` на все стадии. С реальным llama.cpp `draft` на 4B-модели легко превышает 10 с → массовый `TECHNICAL_ERROR`. Нужны per-stage timeouts (config) | `architecture.md §10` («per-stage timeouts are api config») |
| B6 | `TurnStatus.Running` никогда не выставляется (QUEUED → COMPLETED) | `architecture.md §6` |
| B7 | `POST /messages` синхронный: держит HTTP до конца pipeline, отвечает `COMPLETED/FAILED + decision`, контракт описывает `202 {status: QUEUED}` + события по SSE. Решение — см. «Decisions to make» | `web-api-v0.md §5` |

### C. Reliability — documented targets из `architecture.md §7`

| # | Проблема |
|---|---|
| C1 | Retries `api → knowledge` для идемпотентных стадий (understand/retrieve/answerability/verify) не реализованы; `draft` — «не более одного повтора, если draft не персистирован» |
| C2 | Stale-turn guard работает только in-memory: `Turn` без concurrency token, `StartTurn` не трогает строку `cases` (xmin не меняется) → superseded ход из другого запроса/вкладки может перезаписать `SUPERSEDED` на `COMPLETED` в БД |
| C3 | Атомарность: `CaseEventStore` пишет события отдельным `DbContext`; в `Complete/Feedback/IngestHandoffStatus` события коммитятся **до** `SaveChangesAsync` состояния и notifications. Ошибка второго коммита оставляет `CASE_COMPLETED` без завершённого кейса. Для оркестратора per-stage durability — намеренна; для use-case'ов нужна одна транзакция |
| C4 | Notification uniqueness `(owner_id, case_id, type, source_event_id)` — нет колонки/индекса |
| C5 | Outbox: нет lease/heartbeat (второй `api-worker` удвоит доставку); `HandoffSubmitWorker` ставит `FAILED` при первом transport exception без bounded retry (`support-adapter-v0.md §7`); poll — фиксированный интервал вместо backoff `Initial → Max` (`§6.1`) |
| C6 | `Idempotency-Key`/`client_message_id` игнорируются на `POST /messages`; `POST /complete` без idempotency |

### D. Observability и тесты

| # | Проблема |
|---|---|
| D1 | Нет structured trace log в .NET: `trace_id` генерируется, но не логируется вместе с case/turn/stage/timings/decision/error_category; `X-Trace-Id` не возвращается клиенту (`architecture.md §18`) |
| D2 | Нет тестов воркеров (submit success/timeout/failure, status-sync poll/stale/poll-failure), webhook (contract test 13: unsigned/bad signature/stale timestamp → 401, endpoint absent when disabled), API integration tests через `WebApplicationFactory` (owner mismatch → 404, `CASE_CLOSED`, idempotency replay/conflict, validation codes, SSE envelope) |
| D3 | `evals/decisions` (HTTP против `api` с Knowledge в fixture mode) — только README; decision fixtures для `ANSWER/CLARIFY/HANDOFF_OFFER/ANSWER_AND_HANDOFF/TECHNICAL_ERROR` отсутствуют (`quality.md §8`) |
| D4 | Moderation regression set (`quality.md §6`) не существует как fixture; offsets match — в normalized тексте (документированный follow-up) |

### E. Product-enhancement (P0+), серверная часть — только после стабилизации A–B

| # | Фича | Что нужно от .NET | Contract change |
|---|---|---|---|
| E1 | Applicability Card | Публиковать в `AI_ANSWER` payload `applicability { entities[{type,value,provenance}], missing_conditions[], risk_flags[], evidence_fragment_ids[] }` из уже полученного answerability | additive `web-api-v0 §4.3` |
| E2 | Smart Recovery | Команда «Не совпадает с моей ситуацией» → recovery turn с prior evidence/conditions (использует B1), один targeted clarification / одно expand (B4), лимиты, reason «answer did not fit» в quality push; «Помогло» → in-turn сигнал без `RESOLVED`; «Нужен специалист» → существующий handoff | новый route/command в `web-api-v0`, additive поле в `QualityTurnPush` |
| E3 | Presentation Controls | Нужен новый endpoint Knowledge (`transform` с тем же `snapshot_id`/evidence + повторный verify) — в `knowledge-v0` его нет | additive `knowledge-v0` + `web-api-v0` |
| E4 | Portal Context Passport | Слоты с provenance на `Case` (миграция), `trusted_portal_context` только из server-config demo-контекста, команда исправления пользователем, передача в `understand/retrieve` как `Entity` с provenance, перенос в handoff package (A6) | `web-api-v0` + `support-adapter-v0 §2` (`verified_portal_context`) |

## Acceptance criteria

Фаза считается закрытой, когда для каждого пункта есть тест/фикстура, и `dotnet build/test -c Release` зелёные.

- A1: после `Submit=Success|Failure|Timeout` в timeline появляется `HANDOFF_STATUS` с `changed:["status"]` и notification `HANDOFF_UPDATED` **до** первого poll; тест воркера.
- A2: `CANCELLED` на `ACTIVE` → `CLOSED_SUPPORT`, resolution `UNKNOWN`; `RESOLVED` на `CLOSED_USER` c `UNRESOLVED` → resolution остаётся `UNRESOLVED`; Domain tests.
- A3: `risk_flags` содержит `KNOWLEDGE_UNAVAILABLE` → `TECHNICAL_ERROR{category: UNAVAILABLE}`, `NO_CONFIRMED_ANSWER` не публикуется; orchestrator test. Параллельно issue в Knowledge: возвращать `503 MODEL_UNAVAILABLE`.
- A4: «какого оператора ЭДО выбрать» → не human request; «соедините с оператором» → human request; theory-тест с ≥ 6 негативными примерами домена.
- A6: `prepare` возвращает и сохраняет package (summary draft, user_reported_context, already_tried, unknown_fields, sources_checked, handoff_reason, relevant_message_ids); `confirm` принимает только редактируемые поля; `HandoffRequest` заполнен по `§2`; package строится при недоступном Knowledge (contract test 6).
- A7/A8: `AI_ANSWER.payload` содержит `sources[{fragment_id, document_id, page, anchor, title?, label}]`, `snapshot_id`, `model_version`, `retrieval_config_version`; web-тип обновлён тем же change.
- B1: диалог `вопрос → CLARIFY(role) → «поставщик»` даёт второй `understand` с `prior_turn_summary`, содержащим исходный вопрос и missing condition; третий подряд `CLARIFY` невозможен — вместо него `HANDOFF_OFFER` с reason `CLARIFICATION_LIMIT`; ответ «не знаю» не ведёт к повторному вопросу о том же slot.
- B2: таблица `risk_flag → (ServiceNeed, SupportLine, reason_code)` в `RoutingPolicy`; `HUMAN_SUPPORT_REQUIRED` + непустой evidence → `ANSWER_AND_HANDOFF` (draft+verify пройдены), иначе `HANDOFF_OFFER`; неизвестный флаг → консервативный L2 с `UNKNOWN_RISK_FLAG:<name>`; fixture-набор `evals/decisions` минимум по одному кейсу на decision.
- B5: `Knowledge:Timeouts:{Understand,Moderation,Retrieve,Answerability,Draft,Verify,Source}`; timeout draft ≥ 90 с по умолчанию; тест классификации `TIMEOUT` на per-stage значении.
- C3: `CompleteCase`/`SubmitFeedback`/`IngestHandoffStatus` — события, state, notifications, outbox в одной транзакции; тест: сбой на commit → нет `CASE_COMPLETED` в `case_events`.
- D2/D3: воркеры и webhook покрыты; `evals/decisions` запускается против `api` + Knowledge fixture mode и включён в `quality.md §13`.

## Relevant docs/contracts

`docs/product-spec.md §7–§17`, `docs/architecture.md §5–§8, §10, §12, §15–§18`, `docs/contracts/web-api-v0.md`, `docs/contracts/support-adapter-v0.md`, `docs/contracts/knowledge-v0.md` + `.openapi.yaml`, `docs/product-experience.md §3–§6, §11`, `docs/quality.md §6, §8, §11–§13`, `docs/adr/0002`.

## Risks / unknowns

- B1/B2 меняют user-visible decisions: без decision fixtures легко ухудшить precision/coverage. Fixtures — до правок policy.
- A7 и E1–E4 — coordinated changes с Web: нельзя менять payload раньше типов в `src/web/src/api/types.ts`.
- B7 (sync/async) — архитектурное решение пользователя; async тянет in-process runner + supersede/cancel.
- C2/C3 — миграции и транзакции; проверять на живом PostgreSQL, не только на fakes.
- Слово «оператор» в A4 — политика распознавания должна быть подтверждена доменными примерами из organizer data, не «на глаз».
- Реальные латентности llama.cpp неизвестны до certification — значения B5 задать с запасом и сделать конфигурируемыми.

## Workstreams

- **W1 Defects & contract drift (A1–A12)** — root; Domain/Application/Api/Worker; coordinated с Web для A7.
- **W2 Orchestration P0 (B1–B6)** — root + subagent «decision fixtures»; только `Application/Orchestration`, `Domain/Routing`, `Infrastructure/KnowledgeClient`.
- **W3 Reliability (C1–C6)** — subagent; `Infrastructure/Persistence`, `Worker`; миграции.
- **W4 Observability & tests (D1–D4)** — subagent; новые test-проекты `TenderHack.Api.Tests`, `TenderHack.Worker.Tests`, `evals/decisions`.
- **W5 Product enhancement server side (E1–E4)** — root, после W1–W2; каждая фича начинается с contract change + skeptic review.

Нельзя параллелить: правки `Decision`/`RoutingPolicy`, payload timeline-событий, `IHandoffAdapter`/`HandoffRequest`, миграции `cases/turns/handoffs`.

## Implementation steps

### Фаза 0 — quick fixes (≈ 2–3 ч)

1. A1: вызвать `HandoffSubmissionPublisher.PublishAsync` в `HandoffSubmitWorker` после `AcknowledgeHandoff/FailHandoff`, зарегистрировать в `Worker/Program.cs`; тест воркера с fake adapter (success/failure/timeout).
2. A2: `CompleteBySupport(terminal)` — `Cancelled` не меняет resolution; при `ConversationStatus != Active` менять resolution только из `Unknown`; 3 Domain-теста.
3. A3: в `DecideFromAnswerabilityAsync` — если `RiskFlags` содержит `KNOWLEDGE_UNAVAILABLE` → `throw new KnowledgeFailureException(Unavailable, …)`; тест. Завести issue на Python-сторону (503).
4. A4: `DirectHumanRequestDetector` → regex-фразы с word boundaries («оператор(ом|а)? (поддержки|портала)», «живой оператор», «с человеком», «позовите/соедините/переключите»), исключение «оператор эдо/электронного документооборота»; расширить theory-тесты.
5. A5: переименовать опцию в `BaseUrl` **или** env в compose — одно из двух; добавить `Knowledge__Timeouts__*` в compose/`.env.example`.
6. A9: payload `MODERATION_WARNING`/`CONVERSATION_CLOSED` += `message`, `rule_id`, `rule_version`, `close_after_warnings`; текст по `product-spec.md §14`.
7. A10: `ListCasesUseCase` считает unread по `INotificationReader` (один запрос по owner, группировка по case).
8. A11: `RequireOwner` в `SourceEndpoints`.
9. A12: `IngestHandoffStatus` публикует полный handoff view (`external_case_id`, `stale`, `updated_at`) + `changed`.

### Фаза 1 — контекст хода и timeouts (≈ 6–8 ч)

10. B5: `KnowledgeServiceOptions.Timeouts` per stage; в `HttpKnowledgeService` — per-call `CancellationTokenSource` с linked token вместо `HttpClient.Timeout`; тесты классификации.
11. B6: **отложено с обоснованием.** В текущей синхронной архитектуре ход обрабатывается in-process сразу после коммита `Queued`-строки — нет отдельного воркера, который "забирает" ход из очереди. Персист отдельного `Running`-состояния потребовал бы второго `SaveChangesAsync` за ход исключительно ради значения, которое ничем не отличимо от `Queued` для `stale-turn cleanup` (оба уже покрыты `ListStaleActiveTurnCasesAsync`) и нарушил бы задокументированный/протестированный инвариант «один внутренний commit, финальный — на вызывающей стороне» (`TurnOrchestratorTests.TurnIsPersistedBeforeTheFirstStageRuns`). `Running` обретает реальный смысл только вместе с async-моделью из B7 (пункт 26) — реализовать вместе с ней, не раньше.
12. B1: `TurnContext` на `Case` (persisted): последний вопрос, `missing_conditions` последнего `CLARIFY`, слоты `Entity` с provenance, `consecutive_clarifications`, `declined_slots`. Оркестратор: строит `prior_turn_summary`, объединяет entities (user_explicit из нового хода имеет приоритет), после `CLARIFY` ≥ 2 подряд → `HANDOFF_OFFER` reason `CLARIFICATION_LIMIT`; «не знаю» → slot в `declined_slots`, повторный `CONDITION_DEPENDENT` по нему → handoff. Миграция. Тесты по сценариям `product-spec.md §11`.
13. A8: `AI_ANSWER` += `snapshot_id`, `model_version`, `retrieval_config_version`; `QualityTurnPush` уже несёт `snapshot_id` — добавить optional `model_version`/`retrieval_config_version` (additive в `knowledge-v0`).
14. A7: `sources` как объекты (`document_id/page/anchor` из `RetrieveResult`, `title` — additive optional поле `Candidate.title` в `knowledge-v0` с fallback на `document_id`); обновить `src/web/src/api/types.ts` + `SourceCitation.tsx` тем же change.

### Фаза 2 — decision policy и handoff package (≈ 6–8 ч)

15. D3 сначала: `evals/decisions` — фикстуры Knowledge (fixture mode) + ожидаемый `Decision`/`reason_codes` для: sufficient/no flags; sufficient+`NEGATED_QUERY`; insufficient+`HUMAN_SUPPORT_REQUIRED`+evidence; insufficient без evidence; condition_dependent; verify failed; `KNOWLEDGE_UNAVAILABLE`; explicit human request; profanity ×2. Runner — `dotnet test` категория `Decisions` через `WebApplicationFactory` + stub `IKnowledgeService` или HTTP к `knowledge` в fixture mode (по `quality.md §8`).
16. B2: `RoutingPolicy.Evaluate(RiskFlagSet)` с явной таблицей; `ANSWER_AND_HANDOFF` при `HUMAN_SUPPORT_REQUIRED` и непустом `evidence_fragment_ids` (draft+verify обязательны); `TECHNICAL_DIAGNOSIS_REQUIRED` оставить как зарезервированный; reason code для каждого флага.
17. B3: reason `VERIFICATION_FAILED` (отдельно от `INSUFFICIENT_EVIDENCE`); один повторный `draft` с `constraints.tone="extractive"` (поле уже optional в `DraftConstraints`) → verify; затем handoff. `NO_CONFIRMED_ANSWER.payload.reason`.
18. B4: если `INSUFFICIENT` и `MissingConditions`/entities дали новый exact code или slot из B1 — один повторный `retrieve` с расширенными entities; флаг `expanded_once` в quality push.
19. A6: `HandoffPackage` на `Handoff` (owned entity/JSON column): `summary`, `user_reported_context`, `already_tried`, `unknown_fields`, `sources_checked` (evidence ids последнего хода), `handoff_reason` (reason codes), `relevant_message_ids` (event ids `USER_MESSAGE`), `channel`, `integration_mode`; `prepare` собирает и сохраняет из persisted state (без Knowledge); `confirm/retry` принимают только editable поля; `HandoffRequest` по `§2`; `HandoffSubmitPayload` = handoff id (данные читать из БД). Миграция; contract tests 1–6.

### Фаза 3 — reliability (≈ 5–7 ч, можно параллельно с фазой 2)

20. C3: `ITurnEventStream` получает вторую реализацию на ambient `DbContext` (или флаг `durableImmediately`) для use-case'ов; оркестратор оставляет per-stage коммиты. Тест на откат.
21. C2: `xmin` на `turns` (или `Version` колонка) + перечитывание статуса перед `TryPublishDecision` в `SendMessageUseCase`; integration test на PostgreSQL (Testcontainers) — второй send во время первого не позволяет первому опубликовать.
22. C1: `Polly`/ручной retry в `HttpKnowledgeService` — 2 повтора с backoff для understand/retrieve/answerability/verify/moderation; `draft` — 1 повтор только при `TIMEOUT/UNAVAILABLE` до персистенции; тесты.
23. C5: bounded retry submit (`Support:Submit:MaxAttempts`, backoff) до `FAILED`; per-handoff `next_poll_at` с удвоением `Initial → Max` (колонка на `handoffs`); `SELECT … FOR UPDATE SKIP LOCKED` + `locked_until` для outbox (lease).
24. C4: `source_event_id` в `notifications` + unique index; `INotificationSink.Enqueue` принимает event id.
25. C6: `Idempotency-Key` на `POST /messages` (scope `case.message`, replay → тот же `turn_id/revision`) и `POST /complete`.
26. B7 — по решению (см. ниже).

### Фаза 4 — observability, тесты, product-enhancement (после фаз 0–2)

27. D1: `ILogger` scopes с `trace_id/case_id/turn_id`; лог одной строки на стадию (`stage`, `ms`, `snapshot_id`) и на решение (`decision`, `reason_codes`, `error_category`); `X-Trace-Id` в ответе `POST /messages`.
28. D2: `TenderHack.Api.Tests` (`WebApplicationFactory`, in-memory или Testcontainers PostgreSQL): owner authz, `CASE_CLOSED`, idempotency, validation codes, SSE envelope; `TenderHack.Worker.Tests`; webhook contract test 13. Обновить `quality.md §13`.
29. D4: moderation regression fixture (`evals/moderation/*.jsonl`) + theory-тест; external word list через `Moderation:RuleSetPath` (optional).
30. E1 Applicability Card: additive `applicability` в `AI_ANSWER` + `web-api-v0 §4.3`; данные уже есть после фазы 1.
31. E2 Smart Recovery: contract change (`POST /cases/{id}/recovery {kind: NOT_MATCHING|HELPED|NEED_HUMAN}` или `messages.kind`), оркестратор recovery-path поверх B1/B4/B2, `answer_did_not_fit` в `QualityTurnPush` (additive).
32. E4 Context Passport (серверная основа): слоты с provenance из B1 + `Support:DemoPortalContext` (server config, помечен `SIMULATED`), команда исправления, перенос в package (A6).
33. E3 Presentation Controls — только после additive endpoint в `knowledge-v0`; в этом плане не реализуется.

## Verification

```bash
dotnet build src/support-core/TenderHack.sln -c Release
dotnet test src/support-core/TenderHack.sln -c Release
# после фазы 2:
dotnet test src/support-core/TenderHack.sln -c Release --filter Category=Decisions
# после фазы 3 (Testcontainers, нужен Docker):
dotnet test src/support-core/tests/TenderHack.Api.Tests -c Release
docker compose config --quiet
docker compose build api api-worker
```

Ручной E2E по `quality.md §11` п.5–10, 13–15 против Compose с Knowledge в fixture mode; отдельно — п.1–4 на реальном Knowledge после certification.

Не заявлять пункт закрытым без прогона на текущем коммите.

## Decisions made

- 2026-09-12: порядок фаз — сначала дефекты/contract drift (A), затем контекст хода и decision policy (B), reliability (C) параллельно фазе 2, product-enhancement — только после A–B.
- `ANSWER_AND_HANDOFF` определяется в .NET по `HUMAN_SUPPORT_REQUIRED` + evidence; Knowledge не меняет свою классификацию `INSUFFICIENT` (граница не пересматривается).
- `KNOWLEDGE_UNAVAILABLE` как risk flag трактуется .NET как инфраструктурный сбой независимо от того, исправит ли Knowledge код ответа.

## Decisions to make (нужен ответ владельца)

1. ~~B7~~ — решено, см. Progress п.26.
2. **A4 — политика фразы «оператор».** Нужны 10–20 реальных примеров из organizer data с меткой «просит человека / не просит», чтобы regex не «угадывать». (Текущая word-boundary реализация — лучшее, что можно сделать без этих данных.)
3. **Приоритет E1–E4** относительно оставшихся C3/C5-остатка перед feature freeze: что важнее для демо.

## Progress

Чек-лист по каждому шагу из «Implementation steps» — отмечать по факту прогона verification на текущем коммите, не по намерению.

- [ ] Фаза 0 — quick fixes
  - [x] 1. A1 — `HandoffSubmissionPublisher` вызывается из `HandoffSubmitWorker` + зарегистрирован в DI + тест воркера
  - [x] 2. A2 — `CompleteBySupport`: `Cancelled` не трогает resolution; resolution на неактивном кейсе меняется только из `Unknown`; 3 Domain-теста
  - [x] 3. A3 — `KNOWLEDGE_UNAVAILABLE` risk flag → `KnowledgeFailureException(Unavailable)`; тест; issue заведён на Python-сторону
  - [x] 4. A4 — `DirectHumanRequestDetector` переписан на word-boundary фразы с исключением «оператор ЭДО»; расширенные theory-тесты
  - [x] 5. A5 — `Knowledge:BaseUrl`/compose приведены в соответствие; `Knowledge__Timeouts__*` добавлены в compose/`.env.example`
  - [x] 6. A9 — `MODERATION_WARNING`/`CONVERSATION_CLOSED` payload содержит `message`, `rule_id`, `rule_version`, `close_after_warnings`
  - [x] 7. A10 — `ListCasesUseCase` считает `unread_notifications` реально
  - [x] 8. A11 — `GET /api/v0/sources/{id}` требует owner session
  - [x] 9. A12 — `IngestHandoffStatus` публикует полный handoff view + `changed`
- [ ] Фаза 1 — контекст хода и timeouts
  - [x] 10. B5 — per-stage `Knowledge:Timeouts:*`, per-call CTS в `HttpKnowledgeService`, тесты классификации
  - [ ] 11. B6 — отложено до B7 (async model); обоснование в Implementation steps §11
  - [x] 12. B1 — persisted `TurnContext` (prior summary, slots с provenance, `consecutive_clarifications`, `declined_slots`), лимит clarifications, миграция, тесты по `product-spec.md §11`
  - [x] 13. A8 — `AI_ANSWER` += `snapshot_id`/`model_version`/`retrieval_config_version`; `QualityTurnPush` += optional `model_version`/`retrieval_config_version` (additive `knowledge-v0`)
  - [x] 14. A7 — `sources` как объекты (`document_id/page/anchor/title?/label`); `knowledge-v0 Candidate.title` additive; `src/web` типы и `SourceCitation.tsx` обновлены тем же change
- [ ] Фаза 2 — decision policy и handoff package
  - [x] 15. D3 — реализовано как `tests/TenderHack.Api.Tests` (WebApplicationFactory + Testcontainers Postgres + fixture `IKnowledgeService`), не как отдельный `evals/` workspace — 9 decision-фикстур (sufficient/no-flags, sufficient+NEGATED_QUERY, insufficient+HUMAN_SUPPORT_REQUIRED, insufficient без evidence, condition_dependent, verify failed, KNOWLEDGE_UNAVAILABLE, explicit human request, profanity ×2) + 5 lifecycle-тестов (owner authz, idempotency replay, CASE_CLOSED, validation, handoff prepare/confirm). Все 14 проходят против реального Postgres. Требует Docker
  - [x] 16. B2 — `RoutingPolicy` с полной таблицей risk-flag → (ServiceNeed, SupportLine, reason_code); `ANSWER_AND_HANDOFF` достижим на реальном пути
  - [x] 17. B3 — `VERIFICATION_FAILED` reason отдельно от `INSUFFICIENT_EVIDENCE`; один extractive rewrite перед handoff
  - [x] 18. B4 — одно дополнительное расширение retrieval с `expanded_once` в quality push
  - [x] 19. A6 — сделано (summary/context/already_tried/unknown_fields/sources_checked/handoff_reason/relevant_message_ids/channel; integration_mode сознательно не включён — известен только адаптеру, не кейсу); `prepare` строит и сохраняет пакет без Knowledge; `HandoffRequest` по support-adapter-v0 §2; миграция AddHandoffPackage. Не сделано: contract tests 1-13 из support-adapter-v0.md §10 (DemoHandoffAdapter/submit/status сценарии не покрыты отдельными тестами — см. Open issues).
- [ ] Фаза 3 — reliability
  - [ ] 20. C3 — **осознанно отложено, не поверхностно.** Требует реальной FK-связи `NotificationEntity → CaseEventEntity` (чтобы EF в одном `SaveChangesAsync` разрешил сгенерированный `event_id` как `source_event_id` до INSERT нотификации), новой реализации `ITurnEventStream` поверх ambient `DbContext`, и — ключевая проблема — `ITurnEventStream.PublishAsync` сегодня синхронно возвращает реальный committed `event_id` вызывающему коду (нужен для C4's `source_event_id`), что несовместимо с «отложить commit до конца use-case» без второго `SaveChangesAsync` (тогда транзакция снова не одна). Правильная реализация требует переделать этот контракт, а не косметическую правку. Учитывая, что этот риск проявляется только при обрыве процесса ровно между immediate-commit события и финальным commit'ом use-case (редкое окно), решено не делать это наспех поверх уже провalidated C1/C2/C4 кода в этой сессии
  - [x] 21. C2 — concurrency token на `turns`, перечитывание статуса перед публикацией; integration-тест на PostgreSQL
  - [x] 22. C1 — retry для идемпотентных стадий (understand/retrieve/answerability/verify/moderation), ограниченный retry `draft`
  - [~] 23. C5 — частично. Сделано: `Support:Submit:MaxAttempts` (default 3), `HandoffSubmitWorker` больше не помечает `FAILED` на первой транспортной ошибке — использует `RecordAttemptFailureAsync` пока `AttemptCount+1 < MaxAttempts`. Не сделано (осталось как было задокументировано в коде до этой сессии как follow-up): per-handoff backoff `Initial → Max` для status-poll (сейчас фиксированный интервал — `SupportOptions.StatusPollOptions` уже содержит комментарий об этом) и outbox lease (`FOR UPDATE SKIP LOCKED`, актуально только при >1 реплике `api-worker`). Нет отдельного `TenderHack.Worker.Tests` — bounded retry не покрыт тестом в этой сессии
  - [x] 24. C4 — `source_event_id` + unique index на `notifications`
  - [x] 25. C6 — `Idempotency-Key` на `POST /messages` и `POST /complete`
  - [x] 26. B7 — **решено: вариант 1 (sync-с-таймаутами), уже реализовано через B5.** `POST /messages` остаётся синхронным; per-stage timeouts (B5) не дают ходу зависнуть бесконечно, а `HttpKnowledgeService`'s retry (C1) уже покрывает временные сбои. Async `TurnRunner` (вариант 2) не реализуется в этом плане — при реальном 30-60с генеративном ходе на боевой модели стоит пересмотреть, но это отдельное архитектурное решение вне текущего скоупа
- [ ] Фаза 4 — observability, тесты, product-enhancement
  - [x] 27. D1 — structured trace logging (`trace_id/case_id/turn_id`, per-stage и per-decision строки), `X-Trace-Id` в ответе
  - [~] 28. D2 — частично. `TenderHack.Api.Tests` сделан (15 lifecycle-тестов, см. п.15) + webhook contract test 13 (`WebhookEndpointTests`/`WebhookEndpointDisabledTests`: unsigned/bad signature/stale timestamp → 401 на реальном HMAC-пути через отдельный `WebhookTestFixture` с `Support:Webhook:Enabled=true`; endpoint отсутствует при выключенном webhook — 404/405 в зависимости от того, перехватывает ли `MapFallbackToFile` тот же путь для GET). Не сделано: отдельный `TenderHack.Worker.Tests` (bounded-retry-submit из C5 и status-sync poll/stale/poll-failure воркеров остаются непокрытыми автотестом); `quality.md §13` не обновлён под новый Api.Tests-набор
  - [x] 29. D4 — moderation regression fixture + theory-тест
  - [x] 30. E1 — Applicability Card: `applicability` в `AI_ANSWER` (additive `web-api-v0 §4.3`)
  - [ ] 31. E2 — Smart Recovery: новый route/команда, recovery-path в оркестраторе, `answer_did_not_fit` в quality push
  - [ ] 32. E4 — Context Passport (серверная основа): слоты с provenance, demo-контекст помечен `SIMULATED`, команда исправления, перенос в package
  - [ ] 33. E3 — Presentation Controls — не реализуется в этом плане (ждёт additive endpoint в `knowledge-v0`)

## Open issues

- Knowledge: `POST /v0/answerability` при отсутствии репозитория должен отвечать `503 MODEL_UNAVAILABLE`, а не `200 + KNOWLEDGE_UNAVAILABLE` (владелец — workstream B/F).
- Knowledge: `Candidate.title` (additive optional) для источников в ответе без N вызовов `GET /v0/sources`.
- Web: `src/web/README.md` содержит неразрешённые conflict markers (`<<<<<<< HEAD`) — не .NET, но блокирует чтение README.
- `docs/architecture.md §7 «Implementation status»` и `docs/quality.md §13` обновить по факту закрытия каждой фазы тем же change.
