# Demo readiness — полный прогон системы без RunPod и подготовка к защите

Status: **ACTIVE**. Создан 2026-09-13 по итогам runtime-проверки `master` на коммите `6621260` (корпус в git, bootstrap-скрипт) с учётом `3c89791`/`da1cbc3` (enum wire format, профанити-приставки, quality-роуты, knowledge-worker, alembic entrypoint).

Это execution plan по `docs/agent-workflow.md §5`. Он не меняет `product-spec.md`, `architecture.md` и frozen contracts; заглушка генератора — тестовая способность по `AGENTS.md §8` («stub/fixture mode — тестовая способность, если real implementation уже существует»), а не product-fallback.

## Goal

1. Вся цепочка `сообщение → moderation → understand → retrieve → answerability → draft → verify → ANSWER с источниками → completion → feedback → quality analytics` проходит end-to-end на этой машине **без RunPod**, на реальном корпусе (6 PDF, 3899 фрагментов, `snap_0e979dfef376faff41f7c76416fda457`).
2. Критический E2E-набор `quality.md §11` прогнан руками и зафиксирован с результатом по каждому пункту.
3. Видимые на демо дефекты UI/копирайта закрыты.
4. Переключение на RunPod — только `.env`, без правок кода; чек-лист переключения записан.
5. Cold start на втором ноутбуке воспроизводим одной документированной последовательностью.

## Non-goals

- Сертификация Model Stack v2 (`model-stack-v2-real-certification.md`) — заглушка ничего не сертифицирует.
- Изменение decision policy/answerability gate «чтобы ANSWER случался чаще» без evidence (см. Open issues — сначала разбор risk flags).
- Новые сервисы/очереди/БД (нужен ADR); `generator-stub` — compose-профиль, не runtime boundary.
- Web Push/email (OD-004), реальный support adapter (OD-003).
- Handoff package preview / live-обновление списка кейсов / Worker.Tests — polish, вынесены в «Фаза 4 (если останется время)».

## Текущее состояние (факты на `6621260`)

Проверено runtime на этой машине (Docker Desktop, 7.6 GB, без GPU):

- `dotnet test` 230/230; `pytest` 127 passed / 8 skipped (DB-gated); `pnpm typecheck/test/build` зелёные; `docker compose config` OK.
- Корпус загружен `scripts/bootstrap-knowledge.ps1`: 6 документов, 830 стр., 3899 фрагментов, 20 condition cards, snapshot совпадает с бенчмарками, повторный bootstrap идемпотентен.
- Работают: `HANDOFF_OFFER` → prepare/confirm → `api-worker` submit → `SIMULATED_ACCEPTED` → staged-этапы → `RESOLVED` → `CLOSED_SUPPORT` + notifications (SSE в UI без reload); `CLARIFY` («Как изменить МЧД?» → `missing_conditions: [role, provider]`); warning-first модерация; completion/feedback/idempotency/owner-scoping; quality pushes персистятся, `knowledge-worker` строит evaluations.
- **Не работает ветка `ANSWER`**: «как создать оферту» → `SUFFICIENT` → `POST /v0/draft` → `503 MODEL_UNAVAILABLE` → `TECHNICAL_ERROR/UNAVAILABLE`, потому что `KNOWLEDGE_GENERATOR_BASE_URL` указывает на несуществующий `generator-inference`.
- Из 18 сценариев `benchmarks/final-e2e-support-demo.json` `SUFFICIENT` получают только 2 («как создать оферту»; «Как изменить данные банковской карты?» — по gold должен быть `HANDOFF_OFFER`, т.е. false-positive gate). Остальные answerable-вопросы про УПД дают `INSUFFICIENT` с `WRONG_PROVIDER`/`ROLE_AMBIGUITY`/`HIGH_RISK_MISSING_CONDITION`.

## Найденные gaps (с evidence)

### A. Блокер ветки ANSWER

| # | Проблема | Где | Источник истины |
|---|---|---|---|
| A1 | Нет генератора: `draft` → `MODEL_UNAVAILABLE`. RunPod будет позже | `src/knowledge/.../inference/generator.py`, `compose.yaml` профиль `generator` | `stack.md`, `knowledge-v0.md §draft` |
| A2 | `KNOWLEDGE_GENERATOR_TIMEOUT_SECONDS` по умолчанию 30 с (`inference/config.py:85`), нет в `.env.example`; `.NET` ждёт draft 90 с. Медленный RunPod/CPU даст ложный `MODEL_UNAVAILABLE` на 30-й секунде | `inference/config.py`, `.env.example` | `architecture.md §10` (per-stage timeouts согласованы) |

### B. Видимые на демо дефекты

| # | Проблема | Где | Источник истины |
|---|---|---|---|
| B1 | `CLARIFY` рендерит сырые идентификаторы слотов: «Нужны уточнения: • role • provider». В `CLARIFICATION` payload только `missing_conditions` | `TurnOrchestrator.cs:270`, `web/src/features/chat/Messages.tsx:36` | `product-spec.md §11` (уточнение — понятный вопрос, меняющий ветку), `web-api-v0.md §4` |
| B2 | На прямой запрос человека бот пишет «В базе знаний не нашлось подтверждённого ответа», хотя `NO_CONFIRMED_ANSWER.reason = EXPLICIT_HUMAN_REQUEST` | `Messages.tsx:49`, `MessageList.tsx:66` | `product-spec.md §21` (direct human request → no forced FAQ loop) |
| B3 | Текст предупреждения модерации захардкожен в UI; сервер шлёт `message`, `close_after_warnings` | `Messages.tsx:58` | `quality.md §12 Moderation` («warning text and count come from the server event») |
| B4 | Остаток PascalCase: `HANDOFF_OFFER.service_need/recommended_line`, `TECHNICAL_ERROR.category`, метки в `QualityTurnPush` (`decision`, `error_category`, `HandoffStatus`, `RecommendedLine`, `ServiceNeed`) → в аналитике `HandoffOffer`, `ModerationWarning` | `TurnOrchestrator.cs:132,134,448,453-455,525-526` | `web-api-v0.md §4`, `knowledge-v0.md` (Decision vocabulary) |
| B5 | `MODERATION_CLOSE` публикует только `CONVERSATION_CLOSED`: нет `CASE_COMPLETED`, notification и quality completion push | `TurnOrchestrator.HandleModerationViolationAsync` | `architecture.md §5.8` («Completion emits … `CASE_COMPLETED` … `FEEDBACK_REQUESTED` (not for `CLOSED_MODERATION`), and a notification»), `quality.md §12` |
| B6 | Степпер после `TECHNICAL_ERROR` остаётся на «Этап 1 из 7 — Запрос принят» | `RequestStatusStepper.tsx` | `quality.md §12 Frontend` («technical error … states render») |
| B7 | Регексы модерации: пропуск «выебываться» (лимит `{1,6}`), «отъебись» (`ъ`); ложное срабатывание «ебитда» | `Domain/Moderation/ProfanityRuleSet.cs` | `quality.md §6` |

### C. Воспроизводимость / документация

| # | Проблема | Где |
|---|---|---|
| C1 | `scripts/bootstrap-knowledge.ps1` падал с exit 1 после успешного ингеста (PS 5.1: `2>&1` + `$ErrorActionPreference=Stop` → stderr-строка compose становится terminating error). Исправлено локально, **не закоммичено** | `scripts/bootstrap-knowledge.ps1:38-47` |
| C2 | README/`quality.md §13` не говорят: после `git pull` нужен `docker compose build` (в этой сессии стек 2 часа работал на образах старше HEAD с уже починенным багом) | `README.md`, `docs/quality.md §13` |
| C3 | `.env` отстаёт от `.env.example` (`KNOWLEDGE_TIMEOUT`, `KNOWLEDGE_DRAFT_TIMEOUT`, `KNOWLEDGE_VERIFY_TIMEOUT`, `KNOWLEDGE_INFERENCE_BEARER_TOKEN`) | `.env` (локально) |
| C4 | Нет тестов на новое из `da1cbc3`: wiring quality-роутов, `pending_evaluation_identities`, цикл воркера, entrypoint | `src/knowledge/tests` |

### D. Качество ответов (не блокер демо, но риск)

| # | Проблема |
|---|---|
| D1 | «Как изменить данные банковской карты?» → `SUFFICIENT` без risk flags при gold `HANDOFF_OFFER` — риск уверенного неверного `ANSWER` (самая дорогая ошибка по `quality.md §4`) |
| D2 | Обычные вопросы про УПД → `INSUFFICIENT` с `WRONG_PROVIDER`/`ROLE_AMBIGUITY`; возможно, condition cards требуют слот, который пользователь не назвал, и правильный исход — `CLARIFY`, а не handoff |

### E. Задания владельца (2026-09-13): удаление чата, чипы главного экрана, «Материалы» в v0

Все три — product-facing изменения; E1 и E3 расширяют frozen contract `web-api-v0` (E3 — ещё и `knowledge-v0`), поэтому по `AGENTS.md §4` сначала additive-правка контракта + тест, потом обе стороны одним coordinated change.

| # | Задание | Что сейчас | Где |
|---|---|---|---|
| E1 | Пользователь может убрать ненужный чат из списка, «чтобы не мешался» | Удаления нет: `GET /cases` отдаёт все кейсы владельца, разложенные по `ConversationStatus`; в `Case` нет поля скрытия; в `Sidebar` нет контрола | `Domain/Cases/Case.cs`, `Application/UseCases/ListCasesUseCase.cs`, `Api/Endpoints/CaseEndpoints.cs`, `Infrastructure/Persistence/{CaseEntity,CaseRepository,Migrations}`, `web/src/features/navigation/Sidebar.tsx`, `web/src/app/AppShell.tsx`, `web/src/api/{client,types}.ts`, `docs/contracts/web-api-v0.md` |
| E2 | Чип «Больше популярных вопросов» на главной **отправляет свой текст как вопрос** (`startCase(SUGGESTIONS[3])`); то же с «Еще один вопрос к поддержке» — это action-чипы, не вопросы | `HomeScreen.tsx:9-15,41-55`: все пять чипов вызывают `startCase(label)`; `Composer` не умеет принимать текст извне | `web/src/features/home/HomeScreen.tsx`, `web/src/features/chat/Composer.tsx`, новый `web/src/features/home/popularQuestions.ts` |
| E3 | Вкладка «Материалы» должна работать в v0 | `ContextPanel.tsx:63` — плейсхолдер «появится, когда backend будет отдавать список (вне контракта v0)». Ни в `knowledge-v0`, ни в `web-api-v0` нет endpoint'а со списком документов снапшота; данные есть в `kb_snapshot_document_versions → kb_document_versions → kb_documents` и в `kb_fragments.section` | `knowledge/api/routes.py`, `knowledge/persistence/repository.py`, `docs/contracts/knowledge-v0.{md,openapi.yaml}`, `Infrastructure/KnowledgeClient/{nswag.json,Generated,HttpKnowledgeService.cs}`, `Application/Knowledge/IKnowledgeService`, `Api/Endpoints/` (новый `MaterialsEndpoints.cs`), `docs/contracts/web-api-v0.md`, `web/src/features/chat/ContextPanel.tsx`, `web/src/api/{client,types}.ts` |

## Acceptance criteria

- A1: `docker compose --profile stub up -d` + «как создать оферту» → `decision: ANSWER`, `AI_ANSWER.sources` непустой, `AI_ANSWER.model_version` содержит `stub-extractive-v0`, «Открыть источник» в UI показывает фрагмент; `docker compose stop generator-stub` + тот же вопрос → `TECHNICAL_ERROR/UNAVAILABLE` (не «нет ответа»); + запрос человека → handoff работает (E2E #9).
- A2: `KNOWLEDGE_GENERATOR_TIMEOUT_SECONDS` в `.env.example` и `compose.yaml` ≥ `KNOWLEDGE_DRAFT_TIMEOUT`; тест `InferenceSettings.from_env` читает значение.
- B1: `CLARIFICATION.payload.questions: string[]` (additive, `web-api-v0.md §4` обновлён тем же change); для `role`/`provider`/`status`/`document_type` есть формулировки; UI показывает вопросы, при пустом `questions` — fallback на текущий рендер; тест orchestrator + vitest.
- B2/B3: `NoConfirmedAnswerNotice` ветвится по `reason`; `ModerationWarningNotice` показывает `payload.message`; vitest на оба.
- B4: во всех payload/quality push только `ToWire()`; contract-тест на `HANDOFF_OFFER` и `QualityTurnPush.decision == "HANDOFF_OFFER"`.
- B5: `MODERATION_CLOSE` → ровно один `CASE_COMPLETED{completion_reason: MODERATION}`, notification `CASE_COMPLETED`, quality completion push, **без** `FEEDBACK_REQUESTED`; Application-тест.
- B6: `TurnStatus=FAILED` → степпер в состоянии «Ошибка», не «Этап 1».
- B7: theory-тест: «выебываться», «отъебись» → match; «ебитда», «победа», «потребление», «выхухоль» → no match.
- C1–C3: cold start по инструкции `data/organizer/README.md` на чистом volume проходит без ручных шагов; README и `quality.md §13` содержат `build` после pull и профиль `stub`.
- E2E: чек-лист §11 (пункты 1–10, 13–15) заполнен результатами на конкретном коммите; ни одного `TECHNICAL_ERROR` на путях, не связанных с остановленным генератором.
- E1: `DELETE /api/v0/cases/{case_id}` → `204`; кейс исчезает из `GET /cases?status=active|archived` и из подсчёта `unread_notifications`; `GET /cases/{id}` по прямой ссылке продолжает работать (ничего не уничтожается); повторный `DELETE` → `204` (идемпотентно); чужой owner → `404`; кейс с живым handoff (`PENDING`/`ACCEPTED`/`SIMULATED_ACCEPTED` без `terminal`) → `409 HANDOFF_IN_PROGRESS`; `ACTIVE`-кейс при скрытии завершается как `CLOSED_USER`/`UNKNOWN` с **ровно одним** `CASE_COMPLETED{completion_reason: USER}` и quality completion push, но **без** `FEEDBACK_REQUESTED` и без notification; в UI у чата есть контрол «Удалить» с подтверждением, после удаления открытый чат уходит на `/`, список обновляется без reload; Domain/Application/Api.Tests/vitest.
- E2: клик по «Больше популярных вопросов» **не** вызывает `POST /cases` и `POST /messages`, а раскрывает дополнительные вопросы; клик по «Еще один вопрос к поддержке» ставит фокус в composer и ничего не отправляет; клик по чипу-вопросу по-прежнему создаёт кейс и отправляет ровно текст чипа; vitest `HomeScreen.test.tsx` на все три случая.
- E3: `GET /api/v0/materials` отдаёт 6 документов текущего снапшота с `title/declared_version/declared_date/page_count/fragment_count` и `snapshot_id = snap_0e979d…`; `GET /api/v0/materials/{document_id}/sections` — оглавление (section, `page_start`/`page_end`, `first_fragment_id`) в порядке страниц; без owner session → `401`; knowledge недоступен → `503 KNOWLEDGE_UNAVAILABLE`; вкладка «Материалы» показывает список документов → разделы → открытие раздела через существующий `GET /sources/{fragment_id}`; плейсхолдер удалён; `nswag run` воспроизводит `KnowledgeApiClient.g.cs` без ручных правок; pytest (TestClient + in-memory repository), Contract.Tests (wire), Api.Tests (fake), vitest.

## Relevant docs/contracts

`docs/quality.md §4, §6, §11–§13`, `docs/product-spec.md §11, §12, §14, §21`, `docs/architecture.md §5.8, §10, §16`, `docs/contracts/web-api-v0.md §4, §7`, `docs/contracts/knowledge-v0.md` (draft/verify), `docs/stack.md`, `AGENTS.md §3, §8`, `data/organizer/README.md`.

## Risks / unknowns

- Экстрактивная заглушка даёт грубые «ответы» (дословные предложения фрагментов). Для проверки **качества** генерации она бесполезна — только для state/UI/contract E2E. На защите это должно быть сказано явно; `model_version` не даёт выдать stub за модель.
- `verify_v1` может отклонять даже дословные claims, если фрагмент содержит кнопки/URL/числа, которые попадут в `text` claim, но не в `evidence_quote` — заглушка должна класть в claim ровно тот текст, который цитирует.
- D1/D2 — изменения answerability меняют user-visible decisions; без decision-fixture до правок — не трогать. Разбор risk flags — отдельная задача с evidence.
- CPU-вариант (llama.cpp + Qwen3.8-4B Q6_K, 3.3 ГБ) на 7.6 ГБ Docker-памяти: ~1–2 мин на ответ, нужны таймауты 3–4 мин; годится для одного контрольного прогона качества, не для демо.
- Реальные латентности RunPod неизвестны — таймауты (A2) задать с запасом.

## Workstreams

- **W1 Generator stub + inference config (A1, A2)** — Python/infra; `infra/inference/stub/`, `compose.yaml`, `.env.example`, `src/knowledge/tests`.
- **W2 Demo-visible fixes (B1–B7)** — .NET + web; coordinated change для B1 (`web-api-v0.md`).
- **W3 Reproducibility & docs (C1–C4)** — cross-cutting; README, `quality.md`, scripts, тесты knowledge.
- **W4 Full E2E run + demo content (§11, D1/D2 evidence)** — после W1; заполняет чек-лист и `benchmarks/final-e2e-support-demo.json` (новая запись, `generator: stub`).
- **W5 Presentation** — после W4.
- **W6 Owner requests (E1–E3)** — три независимых друг от друга change'а: E1 (.NET + web + `web-api-v0`), E2 (только web), E3 (knowledge + `knowledge-v0` + .NET + `web-api-v0` + web). E3 — самый длинный, начинать с контракта.

W1 и W2 независимы (разные модули); W4 зависит от W1; B1 нельзя параллелить с другими правками `TurnOrchestrator` payload'ов (B4, B5) — делать в одном PR или последовательно. E1 трогает `Case`/миграции — не параллелить с другими миграциями `cases`; E3 регенерирует `KnowledgeApiClient.g.cs` — не параллелить с другими правками `knowledge-v0.openapi.yaml`.

## Implementation steps

### Фаза 1 — заглушка генератора (≈ 2–3 ч)

1. `infra/inference/stub/app.py` (FastAPI, без зависимостей кроме fastapi/uvicorn):
   - `POST /v1/chat/completions`: взять последнее `user`-сообщение, найти `INPUT DATA:` и распарсить JSON `{query, evidence[{fragment_id,page,section,text}], constraints}` (формат — `inference/prompts.py:build_draft_prompt`);
   - выбрать top-K (K=3) evidence в порядке подачи; для каждого — claim `{claim_id: "c<i>", text: <первые 1–2 предложения фрагмента дословно>, fragment_ids: [<id>], evidence_quote: <тот же текст>}`; `draft_markdown` — маркированный список тех же предложений с «(стр. N)»; никаких ключей из `_FORBIDDEN_KEYS` (`generation/models.py`);
   - ответ `{"id":"stub","model":"stub-extractive-v0","choices":[{"index":0,"message":{"role":"assistant","content":"<json>"},"finish_reason":"stop"}],"usage":{...}}`; при `response_format` — контент остаётся чистым JSON без ограждений; `max_tokens` не трогает выбор (всё равно ≤ 800);
   - `GET /health` → `{"status":"ok","model":"stub-extractive-v0"}`; если `INPUT DATA` не найден → `400` (заглушка не должна «отвечать» на произвольный prompt).
2. `infra/inference/stub/Dockerfile` (python:3.12-slim, pinned digest как у knowledge), `infra/inference/stub/README.md`: «test double, не runtime policy, см. ADR-0003».
3. `compose.yaml`: сервис `generator-stub` (`profiles: ["stub"]`, `expose: 8080`, healthcheck на `/health`). `.env.example`: закомментированный блок «Stub generator (E2E без GPU)»: `KNOWLEDGE_GENERATOR_BASE_URL=http://generator-stub:8080`, `KNOWLEDGE_GENERATOR_MODEL=stub-extractive-v0`; добавить `KNOWLEDGE_GENERATOR_TIMEOUT_SECONDS=120` и прокинуть в `compose.yaml` для `knowledge` (A2).
4. Тест `src/knowledge/tests/test_generator_stub.py`: импортировать `app.py` заглушки как модуль (путь через `Path`), собрать prompt `build_draft_prompt` на фикстурных `KnowledgeFragment`, прогнать ответ через `parse_model_output` → `verify_claims` — все claims `supported`. Тест на `400` без `INPUT DATA`.
5. Smoke: `docker compose --profile stub up -d --build generator-stub knowledge` → «как создать оферту» через `POST /api/v0/cases/{id}/messages` → `ANSWER`; зафиксировать `X-Trace-Id`, `model_version`, `sources` в Progress.

### Фаза 2 — видимые дефекты (≈ 1 день, параллельно с фазой 1)

6. B1: в `TurnOrchestrator` словарь `slot → вопрос` (`role` → «Вы работаете на Портале как поставщик или как заказчик?», `provider` → «Через какого оператора ЭДО вы работаете?», `status` → «Какой сейчас статус документа?», `document_type` → «О каком документе речь — УПД, акт, счёт?»; неизвестный слот → «Уточните, пожалуйста: <slot>»); `CLARIFICATION.payload.questions`; `web-api-v0.md §4` additive; `ClarificationNotice` рендерит вопросы; тесты.
7. B2/B3: `NoConfirmedAnswerNotice({reason})`, `ModerationWarningNotice({message})`; vitest.
8. B4: заменить оставшиеся `.ToString()` на `ToWire()` в `TurnOrchestrator.cs:132,134,448,453-455,525-526`; contract-тест; после деплоя убедиться, что `quality_cases.decision_label` новых строк = `HANDOFF_OFFER`.
9. B5: `HandleModerationViolationAsync` при `ModerationClose` → `CaseCompletionPublisher.PublishAsync(..., requestFeedback: false)` (или эквивалент) в той же транзакции хода; Application-тест «ровно один `CASE_COMPLETED`, нет `FEEDBACK_REQUESTED`».
10. B6: `deriveRequestStage` → при `active_turn.status === "FAILED"` состояние `error` («Техническая ошибка», без пульсации).
11. B7: `[её]б[а-яё]{1,8}`, допустить `ъ` после приставки (`(?:на|по|за|от|вы|о|до|пере)?ъ?`), исключение `ебитда`; theory-тест.

### Фаза 3 — воспроизводимость (≈ 2 ч)

12. C1: закоммитить фикс `Invoke-ComposeOutput` (уже в рабочем дереве).
13. C2: README «Запуск» → `docker compose --profile stub up -d --build` → `bootstrap-knowledge.ps1` → открыть `:8080`; `quality.md §13` → добавить `bootstrap-knowledge.ps1` и профиль `stub` как baseline E2E-окружение; отметить: «после `git pull` — `docker compose build`».
14. C3: синхронизировать локальный `.env` с `.env.example` (не коммитится).
15. C4: `test_quality_routes.py` (TestClient + `InMemoryQualityStore` через monkeypatch `get_quality_repository`): `POST turns/feedback/completions` → `GET evaluations/issue-groups` возвращают данные; `test_worker_loop.py`: `_run_once` вызывает `evaluate_turn` для pending и `rebuild_issue_groups` только при наличии работы.

### Фаза 4 — полный прогон и контент демо (≈ 3–4 ч, после фазы 1)

16. Cold start на чистом volume (`docker compose down -v` → `--profile stub up -d --build` → bootstrap) — засечь время, записать в Progress.
17. E2E `quality.md §11` руками в UI `:5173`, результаты в таблицу Progress (пункт / ожидание / факт / commit). Для #8 — `Support:Demo:Submit=Failure` через env; для #9 — `docker compose stop generator-stub`; для #12 — отключить сеть хоста.
18. Прогнать 18 сценариев `final-e2e-support-demo.json` + вопросы `benchmarks/organizer-questions.md` через API скриптом; записать новую запись бенчмарка с `generator: "stub-extractive-v0"`, `git_head_at_measurement`, decision по каждому. Отобрать демо-набор: 3–4 `ANSWER`, 1 `CLARIFY` (МЧД), 1 handoff, 1 «похоже, но ответа нет», 1 модерация.
19. D1/D2: по каждому «неожиданному» исходу записать `risk_flags` + evidence fragment ids в Open issues; **не менять** gate в этом плане.
20. Для аналитики на демо: создать ≥ 3 однотипных handoff-кейса, убедиться, что `GET /api/v0/analytics/issue-groups` возвращает группу с `n`, примерами и limitations.

### Фаза 5 — презентация (≈ 0.5 дня)

21. Слайд архитектуры/инвариантов: `.NET` решает, Python — evidence; генератор = env-переключатель (`model_version` в `AI_ANSWER` как доказательство).
22. Таблица бенчмарка из п.18 с реальным n и честным статусом stub vs модель.
23. Чек-лист переключения на RunPod (в README «Inference»): `KNOWLEDGE_GENERATOR_BASE_URL`, `KNOWLEDGE_GENERATOR_MODEL=empero-ai/Qwen3.8-4B-Distill`, `KNOWLEDGE_INFERENCE_BEARER_TOKEN`, `KNOWLEDGE_GENERATOR_TIMEOUT_SECONDS=120`, `KNOWLEDGE_DRAFT_TIMEOUT=00:03:00` → `docker compose up -d knowledge` → smoke «как создать оферту» → проверить `model_version` без `stub`.
24. Скрипт демо (порядок вопросов из п.18, что говорить на `CLARIFY`/handoff/модерации, где показать «Открыть источник» и аналитику).

### Фаза 6 — задания владельца (E1–E3; E2 ≈ 2 ч, E1 ≈ 0.5 дня, E3 ≈ 1 день)

**E2 — чипы главного экрана (только web, делать первым — самое дешёвое и видимое)**

25. `web/src/features/home/popularQuestions.ts`: два массива — `QUESTION_CHIPS` (три текущих вопроса: «УПД завис в «Отправке»», «Изменить данные контракта», «Ошибка электронного исполнения») и `MORE_QUESTIONS` (6–8 реальных формулировок из `benchmarks/final-e2e-support-demo.json`/`organizer-questions.md`: «Как подписать УПД на портале поставщиков?», «Как изменить МЧД на портале?», «Как загрузить YML?», «как создать оферту», «Как удалить МЧД?», «статус УПД подписан поставщиком»). Комментарий: статический список для P0; источник на будущее — `GET /api/v0/analytics/issue-groups`, не в этом плане.
26. `HomeScreen.tsx`: `SUGGESTIONS` разделить на вопросы и действия. Чипы-вопросы → `startCase(text)` как сейчас. «Больше популярных вопросов» → `setExpanded(true)`: под заголовком/вокруг рендерится ряд `SuggestionChip` из `MORE_QUESTIONS` (клик → `startCase`), сам чип превращается в «Скрыть». «Еще один вопрос к поддержке» → `composerRef.current?.focus()`; ничего не отправляет.
27. `Composer.tsx`: `forwardRef` с `useImperativeHandle({ focus, setText })` (или проп `inputRef`) — нужен для п.26 и пригодится для E3/handoff-префилла. Без изменения существующего поведения `onSubmit`.
28. `web/src/features/home/HomeScreen.test.tsx` (vitest + fake `ApiClient`): (a) клик «Больше популярных вопросов» → `createCase` не вызван, в DOM появились чипы из `MORE_QUESTIONS`; (b) клик «Еще один вопрос к поддержке» → `createCase` не вызван, `document.activeElement` — input composer'а; (c) клик «УПД завис в «Отправке»» → `sendMessage(caseId, "УПД завис в «Отправке»", …)`.

**E1 — удаление (скрытие) чата**

29. Контракт `docs/contracts/web-api-v0.md`: в таблицу команд §2 — `Hide case | DELETE /api/v0/cases/{case_id} | soft-hide: убирает кейс из списков владельца; данные и история не удаляются`; новый подраздел «§9.3 Hide case»: семантика (см. Decisions made), ответы `204 / 404 NOT_FOUND / 409 HANDOFF_IN_PROGRESS`, идемпотентность, «hidden cases are excluded from `GET /cases` and from `unread_notifications`; `GET /cases/{id}` remains readable». `CaseSnapshot` не меняется (additive поле не нужно).
30. Domain `Cases/Case.cs`: `DateTimeOffset? HiddenAt`; метод `Hide(DateTimeOffset now)`: если `HiddenAt != null` → no-op; если `Handoff` в живом состоянии (`Pending`, `Accepted`, `SimulatedAccepted` при `Terminal == null`) → `throw new HandoffInProgressException(Id)` (новый Domain exception); если `ConversationStatus == Active` → `CompleteByUser(solved: null, now)` (переиспользовать существующий переход, чтобы инвариант «ровно один `CASE_COMPLETED`» остался в одном месте); затем `HiddenAt = now`. `Domain.Tests/CaseTests.cs`: 5 кейсов (active → closed+hidden; closed → hidden; live handoff → exception; terminal handoff → hidden; повтор → no-op).
31. Application: `UseCases/HideCaseUseCase.cs` — owner check как в `GetCaseSnapshotUseCase` (`CaseNotFoundException`), `case.Hide(now)`, если статус изменился с `Active` → `CaseCompletionPublisher.PublishAsync(@case, resolutionBefore, now, ct, notify: false, requestFeedback: false)` — расширить `CaseCompletionPublisher` двумя optional-флагами (по умолчанию `true`, существующие вызовы не меняются); quality completion push уходит как обычно (аналитика должна видеть завершение). `Application.Tests`: ровно один `CASE_COMPLETED`, нет `FEEDBACK_REQUESTED`, нет notification, есть outbox completion push; `HandoffInProgressException` пробрасывается.
32. `ListCasesUseCase`: `.Where(c => c.HiddenAt is null)` до разбиения по статусу. `INotificationReader`-подсчёт в `GET /cases` не трогать — скрытые кейсы просто не попадают в список (их непрочитанные не показываются нигде; это осознанно).
33. Infrastructure: `Persistence/CaseEntity` + mapping `hidden_at timestamptz null`; EF migration `AddCaseHiddenAt`; `CaseRepository.ListByOwnerAsync` без изменений (фильтр в use case; объём на хакатоне мал). Регистрация `HideCaseUseCase` в `Api/Program.cs`.
34. Api: `CaseEndpoints.cs` — `app.MapDelete("/api/v0/cases/{caseId}", …)` → `Results.NoContent()`; маппинг `HandoffInProgressException` → `409 { code: "HANDOFF_IN_PROGRESS", title: "Дождитесь завершения обращения у специалиста." }` в общем exception handler рядом с `CASE_CLOSED`. `Api.Tests`: `DELETE` → 204 и кейс исчез из `GET /cases`; чужая cookie → 404; live demo handoff → 409; `DELETE` дважды → 204/204; `GET /cases/{id}` после скрытия → 200.
35. Web: `api/client.ts` `hideCase(caseId): Promise<void>` (`DELETE`); `types.ts` — код ошибки `HANDOFF_IN_PROGRESS`. `Sidebar.tsx`: у каждого элемента списка иконка-кнопка «Удалить» (нужен `TrashIcon` в `design-system/icons.tsx`), появляется на hover/focus, `aria-label="Удалить чат"`; клик → inline-подтверждение «Удалить чат? Да / Нет» (без `window.confirm`); «Да» → `onHideCase(caseId)`. `AppShell.tsx`: `handleHideCase` → `api.hideCase` → рефетч обоих списков → если `caseId === activeCaseId` → `navigate("/")`; `409` → текст под элементом «Сначала дождитесь завершения обращения у специалиста». Vitest `Sidebar.test.tsx`: клик → подтверждение → вызов `onHideCase`; отмена → не вызван.

**E3 — «Материалы» в v0 (contract-first, три стороны)**

36. Контракт `docs/contracts/knowledge-v0.openapi.yaml` + `knowledge-v0.md`: `GET /v0/materials` → `MaterialsResponse { snapshot_id, materials: [{ document_id, title, declared_version?, declared_date?, page_count, fragment_count }] }`; `GET /v0/materials/{document_id}/sections` → `MaterialSectionsResponse { snapshot_id, document_id, sections: [{ section, page_start, page_end, first_fragment_id }] }`; ошибки `404 UNKNOWN_FRAGMENT`-семейства для неизвестного `document_id` (новый код `UNKNOWN_DOCUMENT`), `500`. Оба — read-only, без Decision-полей. Обновить `tests/test_contract_paths.py` (новые paths обязаны быть в yaml).
37. Knowledge `persistence/repository.py`: `list_snapshot_materials(snapshot_id)` (join `kb_snapshot_document_versions → kb_document_versions → kb_documents`, `count(kb_snapshot_fragments)` per version), `list_document_sections(snapshot_id, document_id)` (`kb_fragments` этого version в snapshot: `group by section` → `min(page_start)`, `max(page_end)`, `first fragment_id` по `(page_start, fragment_id)`; `section IS NULL` → «Без раздела» последним). Заголовок — через существующий `_safe_title(original_filename)`.
38. Knowledge `api/routes.py`: два роута по образцу `/v0/sources/{fragment_id}` (репозиторий из `get_knowledge_repository`, `UnknownSnapshotError` → `503`-семантика как в `sources`, неизвестный документ → `404 UNKNOWN_DOCUMENT`); ответ статичен для snapshot — кэш `lru_cache` по `snapshot_id` не нужен на хакатоне, но `Cache-Control: private, max-age=300` на .NET-стороне поставить. `tests/test_materials_endpoints.py`: TestClient + `InMemoryKnowledgeRepository` (ingestion fixture из `test_ingestion_foundation.py`): 2 документа → список; секции упорядочены по страницам; неизвестный id → 404; DB-gated вариант на `K2_TEST_DATABASE_URL` для реального снапшота (6 документов, 3899 фрагментов).
39. .NET: `nswag run src/TenderHack.Infrastructure/KnowledgeClient/nswag.json` → `KnowledgeApiClient.g.cs` (только регенерация, руками не править); `Application/Knowledge/Contracts.cs` — records `MaterialSummary`, `MaterialSection`; `IKnowledgeService.ListMaterialsAsync / ListMaterialSectionsAsync`; `HttpKnowledgeService` — маппинг через `CallAsync` с `Timeouts.Source`; `Api/Endpoints/MaterialsEndpoints.cs`: `GET /api/v0/materials`, `GET /api/v0/materials/{document_id}/sections` — owner session обязателен (как `SourceEndpoints` после A11), `KnowledgeFailureException` → `503 KNOWLEDGE_UNAVAILABLE`, `Cache-Control: private, max-age=300`. Обновить fake `IKnowledgeService` в `Api.Tests` и `Contract.Tests` (wire-тест на `MaterialsResponse` десериализацию). `docs/contracts/web-api-v0.md`: новый «§14 Materials» + строки в таблице §2; §11 Security — materials в списке owner-scoped ресурсов.
40. Web: `types.ts` (`Material`, `MaterialSection`, `MaterialsResponse`), `client.ts` (`listMaterials()`, `listMaterialSections(documentId)`); `ContextPanel.tsx` вкладка «Материалы»: при первом открытии — `listMaterials` (loading/error/empty состояния; при `503` — «Библиотека временно недоступна», не плейсхолдер); список документов (`FileTextIcon`, title, `v{declared_version}`, `{page_count} стр.`); клик → раскрыть разделы (`listMaterialSections`, `ChevronRightIcon`); клик по разделу → существующая модалка источника через `api.getSource(first_fragment_id)` (переиспользовать компонент из `SourceCitation.tsx`, вынеся модалку в `SourceModal`). Удалить текст плейсхолдера и комментарий «out of scope for P0» в `ContextPanel.tsx:11-13`. Vitest `ContextPanel.test.tsx`: fake api → список → разделы → `getSource` вызван с `first_fragment_id`; `503` → сообщение о недоступности.
41. Docs sync: `docs/architecture.md §13–14` (web reads materials only via api), `docs/quality.md §12 Frontend` («Materials tab renders from API, no placeholder»), README web «Known gaps» — убрать абзац про «Materials tab has no backing endpoint».

## Verification

```bash
# автоматика (текущий коммит)
dotnet build src/support-core/TenderHack.sln -c Release
dotnet test src/support-core/TenderHack.sln -c Release
docker run --rm -v "<repo>:/repo:ro" -w /repo/src/knowledge python:3.12-slim sh -c "pip install -q '.[test]' && python -m pytest -p no:cacheprovider"
pnpm --dir src/web typecheck && pnpm --dir src/web test && pnpm --dir src/web build
docker compose config --quiet

# runtime
docker compose --profile stub up -d --build
powershell -ExecutionPolicy Bypass -File .\scripts\bootstrap-knowledge.ps1
# → E2E чек-лист §11 в UI; smoke ANSWER/CLARIFY/HANDOFF через POST /api/v0/cases/{id}/messages
```

Локально нет Python/uv — pytest гоняется в Docker (команда выше); `uv`-вариант из `quality.md §13` остаётся для машин с uv.

## Decisions made

- Заглушка — отдельный OpenAI-совместимый HTTP-сервис в compose-профиле `stub`, а не режим внутри `knowledge`: код knowledge не получает «stub-ветку», переключение симметрично RunPod (только env), stub физически невозможно включить без явного профиля.
- Заглушка экстрактивная и дословная: только так `verify_v1` честно проходит, и E2E проверяет реальный verify, а не выключенный.
- Answerability gate в этом плане не меняется (D1/D2 — только сбор evidence).
- E1 «удаление» чата = **soft-hide**, не физическое удаление: `case_events`, `turns`, `feedback`, idempotency-ключи и quality-корпус в knowledge остаются (аналитический контур и «история читается» — инварианты `AGENTS.md §3`); скрытый кейс просто не попадает в списки. Активный кейс при скрытии завершается через существующий `CompleteByUser(solved: null)`, чтобы не появился второй путь завершения и второй `CASE_COMPLETED`. Кейс с живым handoff скрыть нельзя (`409`): у специалиста реальная заявка, и статусы/уведомления по ней должны дойти до пользователя.
- E2: чипы-вопросы по-прежнему отправляют вопрос сразу (стандартный chat-UX); только action-чипы («Больше популярных вопросов», «Еще один вопрос к поддержке») перестают отправлять текст. Список «популярных» в P0 статический — источник из analytics не в этом плане.
- E3: «Материалы» = список документов текущего нормативного снапшота + оглавление по `section` фрагментов; открытие раздела переиспользует `GET /sources/{fragment_id}`. Отдача самих PDF (скачивание) — не в этом плане: PDF не смонтированы в `knowledge` в runtime (только при bootstrap), и это отдельный вопрос объёма/трафика (26 МБ на документ).

## Decisions to make (нужен ответ владельца)

- Показывать ли на защите ответы заглушки, если RunPod не успеет, или ограничить демо ветками `CLARIFY`/handoff/модерация/аналитика и показать `ANSWER` только на записи/скриншотах.
- B1: формулировки уточняющих вопросов — согласовать с доменными терминами организаторов (роль/оператор ЭДО).
- E1: нужна ли кнопка «Восстановить» (список скрытых) в P0, или достаточно, что прямая ссылка на кейс продолжает работать. По умолчанию — не делать.
- E1: скрывать ли `ACTIVE`-кейс с незавершённым ходом (`active_turn.status = QUEUED`) — по умолчанию разрешить (ход всё равно завершится/устареет через `StaleTurnCleanupWorker`).
- E3: нужна ли на демо отдача PDF целиком («Скачать инструкцию») — если да, это + mount `data/organizer` в `knowledge` + `GET /api/v0/materials/{id}/file` (stream, owner session) — оценка ещё ≈ 3 ч.

## Progress

- [x] Фаза 1 — заглушка генератора
  - [x] 1. `infra/inference/stub/app.py`
  - [x] 2. Dockerfile + README заглушки
  - [x] 3. `compose.yaml` профиль `stub`, `.env.example`, `KNOWLEDGE_GENERATOR_TIMEOUT_SECONDS`
  - [x] 4. `test_generator_stub.py` (3 tests, passing in Docker `python:3.12-slim`)
  - [x] 5. Smoke `ANSWER` с источниками — 2026-09-13, working tree поверх `6621260`, `--profile stub`: «как создать оферту» → `ANSWER` (understand 64 / retrieve 338 / draft 495 / verify 172 мс), 6 источников, `model_version=draft_v1:stub-extractive-v0@…`, «Открыть источник» → v87, стр. 129; stub остановлен → `TECHNICAL_ERROR/UNAVAILABLE`, запрос человека → `HANDOFF_OFFER` → `PENDING` (E2E #9); `quality_cases.decision_label = ANSWER`
- [x] Фаза 2 — видимые дефекты
  - [x] 6. B1 questions в `CLARIFICATION` + UI
  - [x] 7. B2/B3 копирайт по payload
  - [x] 8. B4 остаток `ToWire()`
  - [x] 9. B5 `CASE_COMPLETED` при moderation close
  - [x] 10. B6 степпер FAILED
  - [x] 11. B7 регексы
- [x] Фаза 3 — воспроизводимость (C1–C3; C4 explicitly deferred, see Open issues)
  - [x] 12. C1 коммит фикса bootstrap-скрипта
  - [x] 13. C2 README/`quality.md`
  - [ ] 14. C3 `.env` — local `.env` sync is a per-machine action, not a repo change
  - [ ] 15. C4 тесты quality-роутов/воркера — deferred, not in this pass's confirmed scope
- [ ] Фаза 4 — полный прогон
  - [ ] 16. Cold start на чистом volume (время: —)
  - [ ] 17. E2E §11 чек-лист (таблица ниже)
  - [ ] 18. Бенчмарк 18 сценариев + organizer questions, демо-набор
  - [ ] 19. Evidence по D1/D2 в Open issues
  - [ ] 20. Issue group для демо
- [ ] Фаза 5 — презентация
  - [ ] 21–24
- [x] Фаза 6 — задания владельца (2026-09-13)
  - [x] E2 — чипы главного экрана
    - [x] 25. `popularQuestions.ts`
    - [x] 26. `HomeScreen.tsx`: вопросы vs действия, раскрытие «Больше популярных вопросов», фокус для «Еще один вопрос»
    - [x] 27. `Composer` ref (`focus`/`setText`)
    - [x] 28. `HomeScreen.test.tsx` — manually verified live (UI): expand/collapse, focus, question chip send all work.
  - [x] E1 — удаление (скрытие) чата
    - [x] 29. `web-api-v0.md`: `DELETE /api/v0/cases/{case_id}`, §9.4 (§9.3 was already taken by the feedback widget)
    - [x] 30. Domain `Case.HiddenAt` / `Hide()` / `HandoffInProgressException` + тесты (5 cases)
    - [x] 31. `HideCaseUseCase` + `CaseCompletionPublisher(notify, requestFeedback)` + тесты (4 cases)
    - [x] 32. `ListCasesUseCase` фильтр
    - [x] 33. EF migration `AddCaseHiddenAt`, DI
    - [x] 34. `MapDelete` + `409 HANDOFF_IN_PROGRESS` + Api.Tests (4 cases)
    - [x] 35. Web: `hideCase`, `TrashIcon`, Sidebar-контрол с подтверждением, AppShell рефетч/redirect, vitest (3 cases) — manually verified live
  - [x] E3 — «Материалы» в v0
    - [x] 36. `knowledge-v0` контракт: `/v0/materials`, `/v0/materials/{document_id}/sections` + `test_contract_paths` (passes unchanged, frozen⊆generated)
    - [x] 37. Repository: `list_snapshot_materials`, `list_document_sections` (Postgres + `InMemoryKnowledgeRepository`)
    - [x] 38. Роуты + `test_materials_endpoints.py` (3 cases)
    - [x] 39. .NET: `nswag run` (had to add `jsonLibraryVersion: "10.0"` to `nswag.json` — the installed nswag 14.7.1 defaulted to 8.0 and silently dropped `JsonStringEnumMemberName`, breaking an existing wire test; fixed, see Open issues note below), `IKnowledgeService`, `HttpKnowledgeService`, `MaterialsEndpoints`, fakes, Contract.Tests (2 wire tests) + Api.Tests (3 cases), `web-api-v0.md §15` (§14 was already «Support status webhook»)
    - [x] 40. Web: types/client, `ContextPanel` «Материалы» (список → разделы → `SourceModal`, extracted from `ChatScreen.tsx` for reuse), vitest (3 cases) — manually verified live end-to-end (list → sections → source modal)
    - [x] 41. Docs sync (`quality.md §12` Frontend DoD line added; README/architecture.md had no stale "materials out of scope" text to remove — only `ContextPanel.tsx`'s own comment did, already replaced)

Real behavior note (not a plan deviation, a discovered fact): `HttpKnowledgeService.CategoryFor(...)` does not distinguish `UNKNOWN_DOCUMENT`/`UNKNOWN_FRAGMENT` (404 from `knowledge`) from a real outage — both surface as `503 KNOWLEDGE_UNAVAILABLE` today. Documented honestly in `web-api-v0.md §15` and added to Open issues rather than silently implying a clean 404 the code doesn't actually produce.

### E2E §11 — результаты (заполнять по факту, с коммитом)

| # | Сценарий | Ожидание | Факт | Commit |
|---|---|---|---|---|
| 1 | Вопрос с опечаткой → ANSWER + источник | grounded ответ, «Открыть источник» | 2026-09-13: PASS (UI, `--profile stub`) — «как создать оферту» → ANSWER, 6 источников, `model_version=draft_v1:stub-extractive-v0@...`, «Открыть источник» показывает фрагмент. Опечатка отдельно не проверялась | working tree |
| 2 | Похожая тема без ответа | «подтверждённого ответа нет» + оператор | — | — |
| 3 | Условие меняет ветку (МЧД) | CLARIFY → ответ | 2026-09-13: «Как изменить МЧД?» дало `HANDOFF_OFFER` (`INSUFFICIENT_EVIDENCE`/`LOW_SPECIFICITY`), не `CLARIFY` — это известный D2 (answerability gate), не трогали | working tree |
| 4 | Другой код ошибки | нет подмены | — | — |
| 5 | Прямой запрос человека | сразу handoff, без FAQ-петли | 2026-09-13: PASS (UI) — «соедините с живым человеком» → `NO_CONFIRMED_ANSWER{reason:EXPLICIT_HUMAN_REQUEST}` рендерится как «Соединяю вас со специалистом поддержки», не «не нашлось ответа» (B2) | working tree |
| 6 | Мат ×2 | warning → close, история читается | 2026-09-13: PASS на `da1cbc3` (API); повторно PASS в UI — warning показывает серверный `message` (B3), close → ровно один `CASE_COMPLETED{MODERATION}`, без `FEEDBACK_REQUESTED`, UI фидбек-форму больше не показывает (B5 + web-side gating fix) | working tree |
| 7 | Handoff staged | pending → accepted → этапы → Решено без reload | 2026-09-13: PASS (UI, SSE) | `da1cbc3` |
| 8 | Handoff failure | честная ошибка + retry | — | — |
| 9 | Генератор недоступен + человек | handoff работает | 2026-09-13: частично — `docker compose stop generator-stub` + обычный вопрос → `TECHNICAL_ERROR` (не «нет ответа»), степпер показывает «Ошибка» вместо зависания на этапе 1 (B6). Явный запрос человека при остановленном генераторе отдельно не проверялся (не зависит от generator по коду — handoff не вызывает draft) | working tree |
| 10 | Positive feedback без подтверждения | resolution UNKNOWN | 2026-09-13: PASS (API) | `50f0e04` |
| 11 | Источник советует внешнюю поддержку | нет фейкового dispatch | — | — |
| 12 | Offline | демо работает | — | — |
| 13 | Completion by support на другом кейсе | toast + badge | — (notifications stream в web не подключён — см. Open issues) | — |
| 14 | Завершить обращение → feedback → Архив | read-only, 4 сигнала | 2026-09-13: PASS (API); UI список не обновляется без reload | `50f0e04` |
| 15 | Feedback после demo-handoff | строка специалиста «демо», второй раз — 409 | 2026-09-13: PASS (API+UI) | `da1cbc3` |

## Open issues

- ~~Infra: `compose.yaml`'s `knowledge`/`knowledge-worker` never forwarded `KNOWLEDGE_GENERATOR_MODEL` to the container (only `_BASE_URL`/`_TIMEOUT_SECONDS`/bearer token) — `AI_ANSWER.model_version` kept the code-default model id regardless of `.env`, which silently broke A1's "model_version contains stub-extractive-v0" acceptance criterion. Fixed alongside W1 (both services now forward it, `:-` empty fallback matching the existing `KNOWLEDGE_RERANKER_BASE_URL` pattern).~~ Fixed 2026-09-13.
- ~~Web: `MessageList.tsx`'s `showFeedback` was `completed_at !== null` with no `completion_reason` check, so the resolution-feedback form ("Вопрос решён?"/rating/comment) rendered even after a moderation close — contradicting B5's "moderation close never asks for feedback" and visibly contradicting the correct backend behavior (no `FEEDBACK_REQUESTED` published). Found while manually verifying B5 in the browser. Fixed 2026-09-13 by gating on `completion_reason !== "MODERATION"` too.~~ Fixed 2026-09-13.
- Web: `openNotificationStream` (`src/web/src/api/sse.ts:18`) и `POST /notifications/ack` нигде не вызываются — E2E #13 (toast/badge/OS-нотификация) не реализуем без этого; список кейсов не рефетчится по `CASE_COMPLETED`.
- Web/API: `prepare` не возвращает package preview, `HandoffCard` стартует с пустым summary (`quality.md §12 Handoff` «summary is inspectable/editable» не выполнен).
- Knowledge: `GET /v0/sources/{id}` без snapshot → `500 INTERNAL_ERROR` (лучше `503`); `answerability` без репозитория → `200 + KNOWLEDGE_UNAVAILABLE` вместо `503` (из `support-core-completion.md`).
- .NET: `HttpKnowledgeService`'s `CategoryFor(...)` не различает `UNKNOWN_FRAGMENT`/`UNKNOWN_DOCUMENT` (404 от `knowledge`) от реального `UNAVAILABLE` — оба падают в `KnowledgeFailureCategory.InvalidResponse` → `503 KNOWLEDGE_UNAVAILABLE` на API. Затрагивает и уже существующий `GET /api/v0/sources/{fragmentId}`, и новый `GET /api/v0/materials/{document_id}/sections` (E3). Отдельная задача — не блокирует демо, но UI не может показать честное «документ не найден».
- Knowledge: false-positive `SUFFICIENT` на «Как изменить данные банковской карты?» (D1) — нужен разбор condition cards/answerability до любого показа этого вопроса на демо.
- .NET: `TenderHack.Worker.Tests` отсутствует; web — один тест (`App.test.tsx`).
- Infra: `api-worker` пишет `Cannot load library libgssapi_krb5.so.2` (Npgsql на `aspnet:10.0` без krb5) — шум, не ошибка.
- Корпус лежит в git обычными blob'ами (51 МБ, файлы ≤ 26 МБ; `bootstrap-knowledge.ps1` fail-closed на >50 МБ/файл). При обновлении PDF организаторами — перейти на Git LFS до второй версии.
- Product-enhancement track (Context Passport, Resolution Plan, Applicability Card UI, Smart Recovery, Emerging Issues, Knowledge Gap Radar) — отдельный план `2026-09-product-enhancements.md` (2026-09-13); начинать только после закрытия Фазы 4 этого плана.
