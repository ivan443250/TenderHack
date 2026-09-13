# Product enhancements — Context Passport, Resolution Plan, Applicability Card, Smart Recovery, Emerging Issues, Knowledge Gap Radar

Status: **ACTIVE**. Создан 2026-09-13 по итогам сверки `docs/product-experience.md` с фактическим кодом (рабочее дерево поверх `6621260`, включая незакоммиченные E1/E3 из `2026-09-demo-readiness.md`).

Это execution plan по `docs/agent-workflow.md §5` для product-enhancement track. Нормативная семантика каждой фичи — `docs/product-experience.md §3–§9`; этот план не переопределяет её и не меняет core policy из `product-spec.md`. Каждый пункт ниже ссылается на то, что уже есть в коде, и на то, чего не хватает до acceptance из `product-experience.md`.

## Goal

Довести до работающего, измеримого состояния 3–4 фичи из семи (`product-experience.md §11`: «лучше 2–3 полностью работающих product flows, чем семь карточек без данных»), в порядке «дёшево и видимо → максимальная ценность»:

1. **Applicability Card** — backend готов, нужен только UI.
2. **Portal Context Passport** — модель данных готова, нужны demo-context, команда исправления, snapshot-поле и UI.
3. **Smart Recovery** — новая команда + recovery-путь в оркестраторе поверх существующих `CLARIFY`/condition cards.
4. **Knowledge Gap Radar** — исправить группировку в analytics (сейчас handoff'ы группируются по `service_need`, а `KNOWLEDGE_GAP` вешается на технический сбой) + первый экран аналитики в web.

Emerging Issue Detector — поверх п.4 (time window + baseline), Interactive Resolution Plan — только после проверки, что структура процедур извлекается детерминированно (см. Risks). Presentation Controls — отдельный additive endpoint knowledge, вне этого плана.

## Non-goals

- Новая модель/агентный swarm/GraphRAG — все фичи строятся на существующих `TurnContext`, answerability, condition cards, quality analytics.
- Автоматическое изменение сущностей Портала или «самообучение» KB: Knowledge Gap Radar **показывает** пробел и готовит черновик карточки на review, не публикует его.
- Изменение frozen contracts «по намерению» — каждое новое поле/команда сначала additive-правка `web-api-v0`/`knowledge-v0` + тест (`AGENTS.md §4`).
- Заявление «инцидент на Портале»: Emerging Issue Detector выдаёт только «возможная системная проблема, не подтверждено» с `n`, окном и примерами.

## Текущее состояние (факты на рабочее дерево поверх `6621260`)

| # | Фича | Backend | Contract | Web | Готовность |
|---|---|---|---|---|---|
| 1 | Portal Context Passport | `TurnContext.KnownSlots` с provenance `user_explicit / trusted_portal_context / inferred / unknown`, persisted (`turn_context` JSON), merge с приоритетами (`TurnContext.cs:44-70,94`), передаётся в `understand/retrieve` как `Entity`, в handoff package (`user_reported_context` / `verified_portal_context`), в `AI_ANSWER.applicability.entities`. **Ничего не порождает `trusted_portal_context`** (нет demo-context конфигурации/входа от Портала); нет команды исправления пользователем | `entities` только внутри `AI_ANSWER` payload; в `CaseSnapshot` контекста нет | нет панели «Контекст обращения», нет «Изменить» | ~40 % (данные) / 0 % (продуктовая поверхность) |
| 2 | Interactive Resolution Plan | Draft = `draft_markdown + claims`; `answer_blocks` во внутреннем `GroundedDraft` — не шаги; condition cards (20 шт., `required_slots`, `applicability`, `allowed_action`) есть и уже дают `missing_conditions` | нет `steps` ни в `knowledge-v0 DraftResponse`, ни в `AI_ANSWER` | markdown как есть | ~10 % |
| 3 | Answer Applicability Card | `AI_ANSWER.applicability = {entities[type,value,provenance], missing_conditions, risk_flags, evidence_fragment_ids}` (`TurnOrchestrator.cs:498-508`) | `web-api-v0.md §4.3` описан (2026-09-13) | **не рендерится вообще** (`grep applicability src/web` — пусто) | ~50 % (backend 100 / UI 0) |
| 4 | Smart Recovery | нет команды и recovery-пути. Строительные блоки есть: `CLARIFICATION.questions` (B1), `TurnContext.DeclinedSlots`/лимит clarifications, condition cards, `EXPANDED_RETRY` | нет `answer_did_not_fit`/recovery в `web-api-v0`, нет `recovery` в `QualityTurnPush` | после ответа только «Полезно / Не помогло» в feedback-виджете **после завершения** кейса (`ResolutionFeedback.tsx`), не после ответа | ~15 % |
| 5 | Emerging Issue Detector | `IssueGroupRecord {n, representative_case_ids, negative_signal_count, unresolved_count, limitations, hypotheses(LOW)}`, worker пересчитывает; `GET /api/v0/analytics/issue-groups` | есть | **экрана аналитики в web нет** | ~35 % — нет окна времени, baseline/«↑4.6×», примеров-симптомов (только case ids), учёта в routing |
| 6 | Knowledge Gap Radar | Причины handoff различимы: `INSUFFICIENT_EVIDENCE` ≠ `VERIFICATION_FAILED` ≠ `EXPLICIT_HUMAN_REQUEST` ≠ `CLARIFICATION_LIMIT` (reason codes уходят в `QualityTurnPush.reason_codes`). **Но** `_safe_group_key` (`quality/service.py:217`) берёт `service_need` раньше `reason_codes` → все handoff'ы схлопываются в «CONSULTATION»; `hypothesis_type = KNOWLEDGE_GAP` ставится только при `error_category ∋ "KNOW"` (= `KNOWLEDGE_UNAVAILABLE`, технический сбой — ровно то, что Gap Radar должен **отличать** от пробела знаний, `quality.md §10`) | есть issue-groups; нет полей «представительные вопросы», «связанные human resolutions», «candidate card» | нет | ~20 % |
| 7 | Presentation Controls | нет endpoint'а transform в knowledge | нет | нет | 0 % |

Сквозной пробел: в web нет ни одного экрана для функционального заказчика/аналитика — фичи 5–6 без него невидимы даже при готовом backend.

## Найденные gaps (с evidence)

### F1. Applicability Card (UI)

| # | Проблема | Где |
|---|---|---|
| F1.1 | Payload есть, компонента нет: `MessageList.tsx` рендерит `AI_ANSWER` как markdown + `SourceCitation`, поле `applicability` игнорируется | `web/src/features/chat/MessageList.tsx`, `Messages.tsx`, `api/types.ts` (нет типа `Applicability`) |
| F1.2 | `provenance` не переводится в человекочитаемую пометку («вы указали» / «из Портала» / «предположение» — inferred **не** должен выглядеть как verified, `product-experience.md §5`) | `Messages.tsx` |
| F1.3 | «Нужно уточнить» для `missing_conditions` без вопроса — переиспользовать словарь slot→вопрос из B1 (сейчас только на сервере в `TurnOrchestrator.ToClarificationQuestion`); либо сервер кладёт `questions` и в `applicability` | `TurnOrchestrator.cs:498`, `web-api-v0.md §4.3` |

### F2. Portal Context Passport

| # | Проблема | Где |
|---|---|---|
| F2.1 | Нет источника `trusted_portal_context`. Реального Portal SSO/page-context нет (OD-003); нужен **server-configured demo context** (`Support:DemoContext:*` или `Context:Demo:*`), явно помеченный `SIMULATED` в snapshot и в UI | `Api/Program.cs` (options), новый `Application/Context/DemoPortalContextProvider`, `CreateCaseUseCase` (seed slots при создании кейса) |
| F2.2 | `CaseSnapshot` не содержит контекста → UI не может показать «Контекст обращения» до первого ответа и после reload | `Api/Contracts/CaseMapper.ToSnapshot`, `CaseContracts.cs`, `web-api-v0.md §3` |
| F2.3 | Нет команды исправления: пользователь не может сказать «я заказчик» иначе как текстом в чат | новый `PATCH /api/v0/cases/{case_id}/context` → `Case.CorrectContext(slots)` (`user_explicit`, Rank 2 перезаписывает trusted/inferred — `TurnContext.cs:94`), событие `CONTEXT_UPDATED` |
| F2.4 | Нет UI: панель с известными слотами, пометкой источника, «Изменить» (select для `role`, `provider`, `document_type`, `status`) | `web/src/features/chat/ContextPanel.tsx` (новая вкладка/блок над «Источники»), `client.ts`, `types.ts` |
| F2.5 | Handoff package: `verified_portal_context` уже заполняется по provenance, но demo-контекст должен приходить туда как `SIMULATED`, а не как verified факт (`product-experience.md §3`, `support-adapter-v0 §2`) | `Application/Handoff/HandoffPackageBuilder.cs` |

### F3. Smart Recovery

| # | Проблема | Где |
|---|---|---|
| F3.1 | Нет команды «ответ не подходит» после `AI_ANSWER`; feedback-виджет появляется только после завершения кейса | `web-api-v0.md` (новая команда), `CaseEndpoints.cs`, `Messages.tsx` (кнопки под ответом) |
| F3.2 | Нет recovery-пути в оркестраторе: по `answer_did_not_fit` нужно взять `applicability.missing_conditions`/`risk_flags` последнего ответа и condition cards → **один** targeted-вопрос с вариантами (из `applicability.document_status`/`roles` карточки), затем `understand→retrieve→answerability` с обновлёнными слотами; при повторном «не подходит» → `HANDOFF_OFFER` с reason `RECOVERY_EXHAUSTED` | `TurnOrchestrator.cs`, `Domain/Cases/TurnContext.cs` (счётчик recovery), `Domain/Routing/RoutingPolicy.cs` |
| F3.3 | Quality push не различает «ответ дан» и «ответ не подошёл» — сигнал нужен Gap Radar/Emerging Issues (`product-experience.md §6`, `support-core-completion.md` E2) | `QualityTurnPush` additive `recovery: {attempt, reason}`; `knowledge-v0.openapi.yaml`; `quality/service.py` |
| F3.4 | Варианты ответа для targeted-вопроса должны быть **из карточек/фрагментов**, не выдуманы: knowledge должен отдавать допустимые значения слота (`applicability.document_status` карточки) — additive поле `slot_options` в `AnswerabilityResponse` | `answerability/service.py`, `knowledge-v0` |

### F4. Knowledge Gap Radar + первый экран аналитики

| # | Проблема | Где |
|---|---|---|
| F4.1 | Группировка: `service_need` перекрывает `reason_codes`; `KNOWLEDGE_GAP` ставится на `KNOWLEDGE_UNAVAILABLE` (технический сбой). Нужно: отдельная группировка «gap groups» по `reason_code ∈ {INSUFFICIENT_EVIDENCE, VERIFICATION_FAILED, CLARIFICATION_LIMIT}` × тема (нормализованные entities `document_type`/`process` из `QualityTurnPush` — они уже есть в `question_text`/`prior_turns_summary`, но не как структурированные поля) | `quality/service.py:217-300`, `QualityTurnPush` (additive `entities[]`), `TurnOrchestrator.EnqueueQualityTurnPush` |
| F4.2 | Технический сбой (`error_category` непустой) должен быть **отдельной** группой «technical», никогда не gap (`quality.md §10`) | `quality/service.py` |
| F4.3 | Карточка группы: нет представительных **вопросов** (только case ids), нет `related_human_resolutions` (сколько кейсов этой группы дошли до `CLOSED_SUPPORT/RESOLVED` — данные в `quality_completions`), нет «candidate knowledge card» (черновик: тема, представительные вопросы, ссылки на кейсы, статус `DRAFT_FOR_REVIEW`) | `IssueGroupRecord`, `knowledge-v0 IssueGroup` (additive поля), новая таблица `knowledge_gap_candidates` (миграция 0009) |
| F4.4 | В web нет экрана аналитики: `/analytics` с двумя списками (issue groups, knowledge gaps), карточка группы с `n`, окном, limitations, hypothesis «LOW/MEDIUM — не подтверждено» | `web/src/features/analytics/*`, `App.tsx` роут, `Sidebar` ссылка (только для демо-режима, без ролей — `hackathon-requirements.md §3`) |

### F5. Emerging Issue Detector (поверх F4)

| # | Проблема | Где |
|---|---|---|
| F5.1 | Нет окна времени и baseline: `build_issue_groups` считает по всем данным. Нужно `window_hours` (2 ч) и `baseline` (средний `n` за такое же окно в предыдущих 24 ч/7 дн) → `velocity = n_window / max(baseline, 1)`; порог — конфиг, не «магическое 4.6×» | `quality/service.py`, `worker/main.py`, `IssueGroup` additive `window`, `baseline_n`, `velocity` |
| F5.2 | Симптомы: представительные `question_text` (уже в `quality_cases`) вместо case ids | `IssueGroupRecord` |
| F5.3 | Учёт в routing/handoff («похож на активную группу #17, 23 обращения за 2 ч») — orchestrator при `HANDOFF_OFFER` спрашивает knowledge `GET /v0/quality/issue-groups?active=true`, сопоставляет по тем же ключам и кладёт `related_issue_group` в handoff package и `HANDOFF_OFFER` payload; пользователю — только текст «мы видим похожие обращения, передаём специалистам» **без ETA** и только если включено конфигом | `TurnOrchestrator`, `HandoffPackageBuilder`, `support-adapter-v0 §2` (additive), `HandoffCard.tsx` |

### F6. Interactive Resolution Plan (условно)

| # | Проблема | Где |
|---|---|---|
| F6.1 | Нужна проверка гипотезы: извлекаются ли из фрагментов процедур (`kind = paragraph/list`, секции «6.5.1 …») нумерованные шаги детерминированно. Замер на 10 процедурах корпуса; если < 8/10 — фича откладывается, вместо неё «пошагово» через Presentation Controls | `retrieval/service.py` / новый `generation/steps.py`, `benchmarks/` |
| F6.2 | Если гипотеза подтверждена: knowledge `DraftResponse.steps[{order, text, fragment_id, page, precondition?}]` (additive, каждый шаг — verbatim из фрагмента, проходит `verify_v1`); .NET прокидывает в `AI_ANSWER.steps`; UI — чек-лист с «Перед продолжением» из `missing_conditions` | `generation/service.py`, `knowledge-v0`, `TurnOrchestrator`, `web-api-v0 §4.3`, `Messages.tsx` |

## Acceptance criteria

Из `product-experience.md`, дополнено проверяемыми условиями:

- **F1**: после `ANSWER` под ответом карточка «Почему этот ответ применим»: ✓-строки по `entities` с пометкой источника (`user_explicit` → «вы указали», `trusted_portal_context` → «из Портала» / «демо-контекст» при SIMULATED, `inferred` → «предположение», без ✓), блок «Нужно уточнить» по `missing_conditions` с вопросами, «Открыть источник» по `evidence_fragment_ids`; нет процентов/confidence; vitest на все три provenance.
- **F2**: новый кейс в демо-режиме стартует с слотами из конфига, помеченными `SIMULATED`; `CaseSnapshot.context.slots[]` есть до первого сообщения и после reload; `PATCH …/context` с `role=customer` → следующий `understand` получает исправленный слот (`Application.Tests`), в timeline `CONTEXT_UPDATED`; trusted-слот не спрашивается в `CLARIFY` (`missing_conditions` не содержит известный слот — уже так, тест); handoff package показывает `verified_portal_context` с `integration_mode: SIMULATED`; отсутствие контекста не ломает core (тест без конфига).
- **F3**: после `AI_ANSWER` три кнопки «Помогло / Не подходит к моей ситуации / Нужен специалист»; «Не подходит» → ровно один вопрос с вариантами из карточки/evidence; ответ → новый ход с обновлённым слотом и новым решением (`ANSWER`/`CLARIFY`/`HANDOFF_OFFER`); второе «не подходит» → `HANDOFF_OFFER{reason: RECOVERY_EXHAUSTED}`; `QualityTurnPush.recovery` заполнен; никакого «переписывания» ответа без нового evidence.
- **F4**: `GET /api/v0/analytics/knowledge-gaps` возвращает группы только по `INSUFFICIENT_EVIDENCE|VERIFICATION_FAILED|CLARIFICATION_LIMIT`, технические сбои — в отдельной группе `technical`; у группы `n`, `handoff_count`, `related_human_resolutions`, `representative_questions` (≤3), `limitations`, `candidate_card` (черновик со статусом `DRAFT_FOR_REVIEW`, без публикации в KB); экран `/analytics` рендерит это с API, без локальных вычислений; DB-gated тест на реальном снапшоте.
- **F5**: `issue-groups` содержат `window`, `n_window`, `baseline_n`, `velocity`, `representative_questions`; hypothesis `confidence ∈ LOW|MEDIUM`, текст «не подтверждено как инцидент»; при `velocity ≥ порог` группа помечена `emerging: true`; экран показывает это отдельным блоком; routing-учёт (F5.3) — только если владелец включил `Support:EmergingIssues:Enabled`.
- **F6**: решение принимается по замеру F6.1 и записывается в Decisions made; при реализации — каждый шаг цитирует фрагмент и проходит `verify_v1`.

## Relevant docs/contracts

`docs/product-experience.md §3–§8, §10–§13`, `docs/product-spec.md §7, §10–§12, §15`, `docs/architecture.md §5.2, §8, §13–§14`, `docs/contracts/web-api-v0.md §3, §4.3, §8`, `docs/contracts/knowledge-v0.{md,openapi.yaml}` (answerability, quality), `docs/contracts/support-adapter-v0.md §2`, `docs/quality.md §9–§10, §12`, `docs/plans/active/2026-09-support-core-completion.md` (E1–E4), `docs/plans/active/2026-09-demo-readiness.md`.

## Risks / unknowns

- Reg: `trusted_portal_context` из demo-конфига легко превратить в «verified Portal state» — инвариант `AGENTS.md §3` («inferred/simulated нигде не выглядят как verified»). Маркер `SIMULATED` обязателен в snapshot, карточке, handoff.
- Smart Recovery меняет user-visible decisions — без decision-fixture (Api.Tests с fake knowledge) легко получить бесконечный цикл «не подходит → вопрос → не подходит». Лимит recovery = 1, затем handoff.
- Gap Radar на хакатонных данных даст группы `n = 2–5`; показывать `n` и limitations честно, не «74 обращения».
- Resolution Plan: извлечение шагов из PDF-таблиц/списков может быть шумным — это единственная фича, где есть риск «AI-theater»; отсюда F6.1 как gate.
- Все фичи трогают `TurnOrchestrator` — не параллелить F2/F3/F5.3.

## Workstreams

- **W1 Applicability Card UI (F1)** — web; 0.5 дня; без изменений контракта (кроме опционального `questions` в `applicability`).
- **W2 Context Passport (F2)** — .NET + web + `web-api-v0`; 1–1.5 дня.
- **W3 Smart Recovery (F3)** — .NET + knowledge (`slot_options`) + web + оба контракта; 1.5–2 дня; после W2 (использует команду исправления слота).
- **W4 Knowledge Gap Radar + analytics screen (F4)** — knowledge + .NET proxy + web; 1.5 дня; независим от W1–W3.
- **W5 Emerging Issue Detector (F5)** — knowledge + web; 0.5–1 день поверх W4; F5.3 — отдельно, после W3.
- **W6 Resolution Plan gate (F6.1)** — knowledge; 0.5 дня замера, дальше по результату.

Параллелить можно: W1 ‖ W4; W2 → W3; W4 → W5. Порядок для демо: W1 → W2 → W4 → W3 → W5 → (W6).

## Implementation steps

### Фаза 1 — Applicability Card UI (F1)

1. `web/src/api/types.ts`: `Applicability { entities: {type, value, provenance}[]; missing_conditions: string[]; risk_flags: string[]; evidence_fragment_ids: string[] }`, поле `applicability?` в `AiAnswerPayload`.
2. `web/src/features/chat/ApplicabilityCard.tsx`: заголовок «Почему этот ответ применим»; строки `entities` с иконкой по provenance и подписью источника (`SIMULATED` → «демо-контекст»); блок «Нужно уточнить» из `missing_conditions` (вопросы — из `applicability.questions`, если сервер даст, иначе словарь-fallback в web, помеченный как временный); «Открыть источник» → `SourceCitation` по первому `evidence_fragment_id`. Ни confidence, ни процентов.
3. `TurnOrchestrator.cs:502`: добавить `questions = MissingConditions.Select(ToClarificationQuestion)` в `applicability` (additive, `web-api-v0.md §4.3`).
4. `MessageList.tsx`: рендер карточки под `AiAnswer`, сворачиваемая. Vitest: три provenance, пустой `entities`, `missing_conditions` с вопросами.

### Фаза 2 — Portal Context Passport (F2)

5. Контракт `web-api-v0.md §3`: `CaseSnapshot.context = { slots: [{type, value, provenance, simulated: bool}], updated_at }` (additive); новая команда `PATCH /api/v0/cases/{case_id}/context { slots: [{type, value}] }` → `200 CaseSnapshot`, событие `CONTEXT_UPDATED { slots, source: "user" }`; `409 CASE_CLOSED` для завершённого кейса.
6. `Api/Program.cs` + `appsettings`: секция `Context:Demo` (`Enabled`, `Slots: [{type: role, value: supplier}, {type: process, value: contract_execution}]`), env `CONTEXT_DEMO_ENABLED`, `CONTEXT_DEMO_SLOTS` (JSON) в `compose.yaml`/`.env.example`. `Application/Context/IPortalContextProvider` + `DemoPortalContextProvider` (возвращает слоты с `TrustedPortalContext` + флаг simulated). `CreateCaseUseCase`: при создании кейса `case.SeedContext(provider.GetAsync(ownerId))`.
7. Domain: `ContextSlot` + `Simulated: bool` (миграция `turn_context` JSON — обратная совместимость: отсутствующее поле = false); `Case.SeedContext(slots)`, `Case.CorrectContext(slots, now)` (provenance `UserExplicit`, `WithObservedQuestion`-merge, событие). Domain.Tests: seed не перезаписывает user_explicit; correction перезаписывает trusted; inferred никогда не перезаписывает.
8. Application: `CorrectContextUseCase` (owner check, `CASE_CLOSED`), публикация `CONTEXT_UPDATED`; `TurnOrchestrator` без изменений (слоты уже идут в `understand/retrieve`); `HandoffPackageBuilder`: `verified_portal_context` только из `TrustedPortalContext && !Simulated`, simulated-слоты — в отдельное поле `simulated_context` (additive в `support-adapter-v0 §2`), чтобы адаптер не получил демо-факт как verified. Тесты.
9. Api: `CaseMapper.ToSnapshot` → `context`; `MapPatch(…/context)`; Api.Tests: snapshot после создания содержит demo-слоты с `simulated: true`; PATCH меняет и пишет событие; чужой owner 404.
10. Web: `ContextPanel.tsx` — блок «Контекст обращения» над «Источники»: список слотов с подписью источника, бейдж «демо» для simulated, «Изменить» → форма из `<select>` по словарю значений (`role`: поставщик/заказчик; `provider`; `document_type`; `status`) → `api.correctContext`; после ответа — refetch snapshot (уже по SSE). Vitest.

### Фаза 3 — Smart Recovery (F3)

11. Контракты: `web-api-v0` — команда `POST /api/v0/cases/{case_id}/recovery { turn_id, reason: "ANSWER_DID_NOT_FIT" }` → `SendMessageResponse`-подобный ответ (новый ход), события `RECOVERY_QUESTION { question, options[], slot }`; `knowledge-v0` — `AnswerabilityResponse.slot_options: {slot: [values]}` (additive, из condition cards `applicability.*`), `QualityTurnPush.recovery {attempt, reason}` (additive).
12. Knowledge `answerability/service.py`: собирать `slot_options` из карточек, участвовавших в оценке (`document_status`, `roles`, `provider`); тест на фикстурных карточках.
13. Domain: `TurnContext.RecoveryAttempts`, `Case.StartRecovery(turnId, now)` (валидация: последний ход = `ANSWER`, лимит 1); reason code `RECOVERY_EXHAUSTED` в `RoutingPolicy` (L1, `Consultation`).
14. Application: `RecoveryUseCase` → оркестратор: взять `applicability` последнего `AI_ANSWER` (persisted в событии), выбрать слот: первый из `missing_conditions`, иначе слот с `risk_flags` (`WRONG_PROVIDER` → provider, `ROLE_AMBIGUITY` → role, `WRONG_STATUS` → status); опубликовать `RECOVERY_QUESTION` с `options` из `slot_options`; ответ пользователя (кнопка → `PATCH /context` + новое сообщение «повторите с учётом…» или отдельная команда `POST /recovery/answer`) → обычный ход. Второй `recovery` → `HANDOFF_OFFER{RECOVERY_EXHAUSTED}`. Quality push с `recovery`. Api.Tests с fake knowledge: fit → recovery → ANSWER; fit → recovery → recovery → HANDOFF.
15. Web: под `AiAnswer` три кнопки; «Не подходит» → `api.startRecovery`; `RECOVERY_QUESTION` рендерится как вопрос с чипами-вариантами (клик → `answerRecovery`); «Нужен специалист» → `handoff/prepare`. Vitest.

### Фаза 4 — Knowledge Gap Radar + экран аналитики (F4)

16. `QualityTurnPush` additive `entities: [{type, value, provenance}]` (`knowledge-v0`), `TurnOrchestrator.EnqueueQualityTurnPush` заполняет из `KnownSlots`; миграция `0009_quality_entities_and_gaps` (`quality_cases.entities json`, таблица `knowledge_gap_candidates`).
17. `quality/service.py`: `build_knowledge_gaps(turns, completions)` — фильтр `reason_codes ∩ {INSUFFICIENT_EVIDENCE, VERIFICATION_FAILED, CLARIFICATION_LIMIT}` и `error_category` пуст; ключ = `(reason, document_type|process из entities, иначе нормализованный первый exact_code/лемма темы)`; поля `n`, `handoff_count`, `related_human_resolutions` (кейсы группы с `completion_reason = SUPPORT` и `resolution_status = RESOLVED`), `representative_questions` (≤3 самых коротких `question_text`), `limitations`, `candidate_card {title, representative_questions, case_ids, status: DRAFT_FOR_REVIEW}`. Технические сбои → отдельная группа `technical:<error_category>` в issue-groups, никогда в gaps. Исправить `hypothesis_type`: `KNOWLEDGE_GAP` только для gap-групп. Тесты на in-memory store + DB-gated.
18. Роут `GET /v0/quality/knowledge-gaps` (+ `POST /v0/quality/knowledge-gaps/{id}/candidate-card` — сохранить черновик; без публикации в KB), worker пересчитывает gaps вместе с группами. `.NET`: `GET /api/v0/analytics/knowledge-gaps`, `POST …/candidate-card` (owner session; демо-режим без ролей — отметить в `web-api-v0 §11`). `nswag run`.
19. Web: `features/analytics/AnalyticsScreen.tsx` (`/analytics`, ссылка в sidebar «Аналитика (демо)»): вкладки «Повторяющиеся проблемы» и «Пробелы базы знаний»; карточка группы: `n`, окно, `unresolved`, `representative_questions`, limitations, hypothesis с confidence и текстом «не подтверждено»; кнопка «Подготовить черновик knowledge card» → `candidate-card` → статус `DRAFT_FOR_REVIEW`. Vitest с fake api.
20. Данные для демо: прогнать 8–10 кейсов по двум темам без ответа (например «МЧД после смены руководителя», «РДИК») через API → группы с `n ≥ 3`.

### Фаза 5 — Emerging Issue Detector (F5, после Фазы 4)

21. `build_issue_groups(window_hours=2, baseline_days=7)`: `n_window`, `baseline_n` (среднее по окнам той же длины за baseline), `velocity`, `emerging = velocity ≥ EMERGING_VELOCITY_THRESHOLD (env, default 3.0) and n_window ≥ EMERGING_MIN_N (default 5)`; `representative_questions`; hypothesis `confidence: MEDIUM` при `emerging`, иначе `LOW`; `limitations` += «baseline по N окнам». Additive поля в `IssueGroup`. Тесты с синтетическими timestamp'ами (помечены synthetic).
22. Web: блок «Возможные системные проблемы» на экране аналитики — только `emerging` группы, `↑{velocity}×`, `n_window` за окно, симптомы; текст «Не подтверждено как инцидент».
23. F5.3 (опционально, после Фазы 3): `Support:EmergingIssues:Enabled`; оркестратор при `HANDOFF_OFFER` запрашивает активные emerging-группы, сопоставляет по ключу (тот же `document_type/process` + reason), кладёт `related_issue_group {id, n_window, window}` в `HANDOFF_OFFER` payload и handoff package; `HandoffCard`: «Мы видим похожие обращения у других пользователей. Передаём информацию специалистам» — без ETA.

### Фаза 6 — Resolution Plan gate (F6)

24. Замер F6.1: скрипт `benchmarks/steps-extraction-probe.py` на 10 процедурных секциях корпуса (нумерованные списки в `kb_fragments`, `kind ∈ {list, paragraph}`): доля секций, где извлекается упорядоченный список шагов с page anchors ≥ 2 шагов; результат в `benchmarks/k7-resolution-steps.json`. Решение «делать/не делать» — в Decisions made.
25. При «делать»: `generation/steps.py` (детерминированно, verbatim), `DraftResponse.steps` additive, `verify_v1` на каждый шаг, `AI_ANSWER.steps`, UI-чеклист + «Перед продолжением» из `missing_conditions`.

## Verification

```bash
dotnet build src/support-core/TenderHack.sln -c Release && dotnet test src/support-core/TenderHack.sln -c Release
docker run --rm -v "<repo>:/repo:ro" -w /repo/src/knowledge python:3.12-slim sh -c "pip install -q '.[test]' && python -m pytest -p no:cacheprovider"
pnpm --dir src/web typecheck && pnpm --dir src/web test && pnpm --dir src/web build
docker compose --profile stub up -d --build && powershell -File .\scripts\bootstrap-knowledge.ps1
# smoke по фиче: F1 — «как создать оферту» → карточка применимости; F2 — новый кейс → «Контекст обращения (демо)» → «Изменить» → CLARIFY больше не спрашивает роль;
# F3 — ANSWER → «Не подходит» → вопрос с вариантами → новый ход; F4 — 3 handoff-кейса одной темы → /analytics показывает gap с n=3 и черновик карточки
```

## Decisions made

- Порядок: Applicability Card → Context Passport → Gap Radar/аналитика → Smart Recovery → Emerging Issues → (Resolution Plan по gate). Отличается от `product-experience.md §11` (там Presentation Controls вторыми): Presentation Controls требуют нового transform-endpoint в knowledge и дают меньше на демо, чем Context Passport, у которого 40 % уже есть.
- Demo-контекст — server-config, помечен `SIMULATED` везде (snapshot, карточка, handoff `simulated_context`), никогда не `verified_portal_context`.
- Smart Recovery: лимит одна попытка, варианты только из condition cards/evidence, второе «не подходит» → handoff.
- Gap Radar никогда не включает технические сбои и не публикует карточки в KB; только `DRAFT_FOR_REVIEW`.

## Decisions to make (нужен ответ владельца)

- Какие demo-слоты стартовать по умолчанию (`role=supplier`, `process=contract_execution`?) и показывать ли их до первого сообщения на главной (composer уже «знает» контекст) или только в чате.
- Показывать ли пользователю сообщение «мы видим похожие обращения» (F5.3) — бизнес-решение; по умолчанию только для специалиста в handoff package.
- Делать ли Resolution Plan вообще до защиты (F6.1 → gate).
- Экран аналитики без ролей: доступен любому owner session в демо — приемлемо для защиты? (`hackathon-requirements.md §3` — accounts/roles нет).

## Progress

- [x] Фаза 1 — Applicability Card UI (F1)
  - [x] 1. types (`web/src/api/types.ts`: `Applicability`, `ApplicabilityEntity`, `ContextSlotProvenance`)
  - [x] 2. `ApplicabilityCard.tsx` — collapsible, ✓ only for `user_explicit`/`trusted_portal_context`, "Открыть источник" on first `evidence_fragment_ids`, renders nothing when there is nothing to say
  - [x] 3. `applicability.questions` (additive, `TurnOrchestrator.cs`, reuses B1's `ToClarificationQuestion`) + `web-api-v0.md §4.3` updated
  - [x] 4. рендер в `MessageList` + vitest (4 cases) + 2 new `TurnOrchestratorTests` cases (empty case, mapped-question case). Manually verified live: on the real corpus, `understanding` does not currently extract slot entities from free text, so `applicability.entities`/`missing_conditions` come back empty on ordinary `ANSWER`s and the card correctly renders nothing rather than an empty box — this is existing `understand` behavior, not a defect in this change. The full-entities/questions/source-open path is covered by the vitest+xunit tests above; a real end-to-end demo of a populated card needs a query that actually produces `missing_conditions` (e.g. a `CLARIFY`-turned-`ANSWER`) or F2's context passport feeding `entities` from something other than free-text NLU.
- [ ] Фаза 2 — Context Passport (F2)
  - [ ] 5. контракт: `CaseSnapshot.context`, `PATCH …/context`, `CONTEXT_UPDATED`
  - [ ] 6. `Context:Demo` options + `DemoPortalContextProvider` + seed в `CreateCaseUseCase`
  - [ ] 7. Domain: `Simulated`, `SeedContext`, `CorrectContext` + тесты
  - [ ] 8. `CorrectContextUseCase`, `HandoffPackageBuilder.simulated_context` + тесты
  - [ ] 9. `CaseMapper.context`, `MapPatch`, Api.Tests
  - [ ] 10. Web: блок «Контекст обращения», «Изменить», vitest
- [ ] Фаза 3 — Smart Recovery (F3)
  - [ ] 11. контракты (`recovery`, `RECOVERY_QUESTION`, `slot_options`, `QualityTurnPush.recovery`)
  - [ ] 12. knowledge `slot_options`
  - [ ] 13. Domain `RecoveryAttempts`, `RECOVERY_EXHAUSTED`
  - [ ] 14. `RecoveryUseCase` + оркестратор + Api.Tests
  - [ ] 15. Web: три кнопки, `RECOVERY_QUESTION`, vitest
- [ ] Фаза 4 — Knowledge Gap Radar + `/analytics` (F4)
  - [ ] 16. `QualityTurnPush.entities`, миграция 0009
  - [ ] 17. `build_knowledge_gaps`, technical-группа, `hypothesis_type` фикс + тесты
  - [ ] 18. роуты knowledge + .NET proxy + `nswag run`
  - [ ] 19. `AnalyticsScreen.tsx` + vitest
  - [ ] 20. демо-данные (n ≥ 3)
- [ ] Фаза 5 — Emerging Issue Detector (F5)
  - [ ] 21. окно/baseline/velocity/emerging + тесты
  - [ ] 22. блок на экране аналитики
  - [ ] 23. F5.3 routing-учёт (опционально, после Фазы 3)
- [ ] Фаза 6 — Resolution Plan gate (F6)
  - [ ] 24. замер извлечения шагов → `k7-resolution-steps.json` → решение
  - [ ] 25. реализация (только при положительном gate)

## Open issues

- `TurnContext.KnownSlots` не имеет словаря допустимых типов слотов — `understand` отдаёт `document_abbreviation`, карточки требуют `document_type`/`role`/`provider`/`status`; для F2/F3 нужна таблица нормализации типов (одно место — `Application/Context/SlotVocabulary.cs`), иначе «Изменить» и `missing_conditions` будут говорить на разных языках.
- Presentation Controls (`product-experience.md §9`) — не в этом плане; требует `POST /v0/transform` в knowledge с повторным verify.
- `GET /api/v0/analytics/*` без ролей — при появлении реальных пользователей нужен gate (OD-003/roles), сейчас демо.
