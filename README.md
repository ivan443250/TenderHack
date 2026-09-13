# TenderHack 2026 — интеллектуальная поддержка Портала поставщиков

Система поддержки, которая работает не как «LLM поверх PDF», а как управляемый процесс обращения: сохраняет контекст пользователя, ищет нормативное основание, отдельно проверяет применимость ответа, формирует grounded-ответ, умеет безопасно уточнить недостающее условие или передать уже подготовленный кейс специалисту, а после завершения обращения превращает feedback/outcome в данные для quality-аналитики.

Ключевая архитектурная формула проекта:

```text
контекст → проверяемые знания → Answerability Gate → grounded draft → verification → Decision / handoff → quality analytics
```

`.NET Support Core` владеет состоянием, бизнес-переходами и финальными решениями; `Python Knowledge & Inference` отвечает за знания, retrieval, answerability, generation/verification и read-only аналитику. Модель формирует текст, но не владеет состоянием кейса и не может сама объявить проблему решённой.

## Как решение закрывает критерии оценки

Ниже — короткий маршрут для code review: каждый критерий связан не с презентационным обещанием, а с конкретным кодом, тестами и документацией в репозитории.

### 1. Работоспособный прототип — 40 баллов

Проект реализован как сквозной vertical slice, а не как набор mock-экранов: Web работает через публичный `.NET API`, `TurnOrchestrator` проводит обращение через moderation/context/retrieval/answerability/draft/verify, состояние сохраняется в PostgreSQL, handoff отправляется через outbox/worker, а completion/feedback и quality-поток продолжают жизненный цикл кейса после ответа.

Что особенно важно для этого критерия:

- **единый authoritative workflow:** [`TurnOrchestrator`](src/support-core/src/TenderHack.Application/Orchestration/TurnOrchestrator.cs) связывает все этапы обращения и сохраняет финальный `Decision`;
- **настоящая state machine, а не состояние в UI:** [`Case`](src/support-core/src/TenderHack.Domain/Cases/Case.cs), [`Enums`](src/support-core/src/TenderHack.Domain/Cases/Enums.cs), [`TurnContext`](src/support-core/src/TenderHack.Domain/Cases/TurnContext.cs);
- **реальный публичный backend boundary:** [`TenderHack.Api`](src/support-core/src/TenderHack.Api/Program.cs), а браузер не обращается к inference напрямую;
- **асинхронные side effects:** [`TenderHack.Worker`](src/support-core/src/TenderHack.Worker/) доставляет handoff/status/quality-события без превращения пользовательского HTTP-запроса в долгую интеграционную транзакцию;
- **handoff как полноценный процесс:** [`HandoffPackageBuilder`](src/support-core/src/TenderHack.Application/Handoff/HandoffPackageBuilder.cs), [`PrepareHandoffUseCase`](src/support-core/src/TenderHack.Application/UseCases/PrepareHandoffUseCase.cs), [`ConfirmHandoffUseCase`](src/support-core/src/TenderHack.Application/UseCases/ConfirmHandoffUseCase.cs), [`HandoffSubmitWorker`](src/support-core/src/TenderHack.Worker/HandoffSubmitWorker.cs), [`HandoffStatusSyncWorker`](src/support-core/src/TenderHack.Worker/HandoffStatusSyncWorker.cs);
- **рабочий Web-контур:** [`ChatScreen`](src/web/src/features/chat/ChatScreen.tsx), [`caseStore`](src/web/src/state/caseStore.tsx), API/SSE-клиент в [`src/web/src/api`](src/web/src/api/);
- **интеграционные и регрессионные проверки:** [`CaseLifecycleTests`](src/support-core/tests/TenderHack.Api.Tests/CaseLifecycleTests.cs), [`DecisionFixtureTests`](src/support-core/tests/TenderHack.Api.Tests/DecisionFixtureTests.cs), [`TurnOrchestratorTests`](src/support-core/tests/TenderHack.Application.Tests/TurnOrchestratorTests.cs), Python-тесты в [`src/knowledge/tests`](src/knowledge/tests/), Web-тесты рядом с компонентами;
- **воспроизводимый запуск:** [`compose.yaml`](compose.yaml), [`bootstrap-knowledge.ps1`](scripts/bootstrap-knowledge.ps1), актуальный статус демонстрационного контура — [`2026-09-demo-readiness.md`](docs/plans/active/2026-09-demo-readiness.md).

Принцип прототипа: даже отказ Knowledge/model не должен уничтожить сохранённый кейс или заставить систему выдавать инфраструктурную ошибку за «нет ответа в базе».

### 2. Структурирование базы знаний и качество ответов — 20 баллов

База знаний строится как версионируемый нормативный snapshot с provenance, а не как папка с PDF. Организаторский корпус проходит extraction/chunking, сохраняет document/page/section anchors и метаданные, после чего ответ проходит несколько независимых quality gates.

Контур знаний:

```text
6 нормативных PDF
→ extraction / structure-aware chunking
→ versioned fragments + provenance
→ exact / PostgreSQL FTS / pg_trgm
→ Answerability Gate
→ grounded generation
→ claim/source verification
→ ответ с открываемым источником
```

Доказательства в коде:

- **ingestion и воспроизводимый snapshot:** [`ingestion`](src/knowledge/src/tenderhack_knowledge/ingestion/), [`data/organizer`](data/organizer/), миграции provenance/version semantics в [`src/knowledge/migrations`](src/knowledge/migrations/);
- **структурированные условия применимости:** [`conditions`](src/knowledge/src/tenderhack_knowledge/conditions/) и condition-card migration [`0006_condition_cards.py`](src/knowledge/migrations/versions/0006_condition_cards.py);
- **production retrieval:** [`retrieval/service.py`](src/knowledge/src/tenderhack_knowledge/retrieval/service.py) — exact entities/codes + PostgreSQL FTS + `pg_trgm`;
- **решение «можно ли отвечать» вынесено до LLM:** [`answerability/service.py`](src/knowledge/src/tenderhack_knowledge/answerability/service.py);
- **генерация ограничена найденным evidence:** [`generation/service.py`](src/knowledge/src/tenderhack_knowledge/generation/service.py), [`inference/generator.py`](src/knowledge/src/tenderhack_knowledge/inference/generator.py);
- **draft не публикуется без проверки:** [`verification/service.py`](src/knowledge/src/tenderhack_knowledge/verification/service.py);
- **пользователь может проверить основание ответа:** [`SourceCitation`](src/web/src/features/chat/SourceCitation.tsx), [`SourceModal`](src/web/src/features/chat/SourceModal.tsx), [`ContextPanel`](src/web/src/features/chat/ContextPanel.tsx), [`ApplicabilityCard`](src/web/src/features/chat/ApplicabilityCard.tsx);
- **регрессии по ключевым границам:** [`test_ingestion_bootstrap.py`](src/knowledge/tests/test_ingestion_bootstrap.py), [`test_retrieval_postgres.py`](src/knowledge/tests/test_retrieval_postgres.py), [`test_answerability.py`](src/knowledge/tests/test_answerability.py), [`test_grounded_generation.py`](src/knowledge/tests/test_grounded_generation.py), [`test_condition_cards.py`](src/knowledge/tests/test_condition_cards.py), [`test_source_endpoints.py`](src/knowledge/tests/test_source_endpoints.py).

Качество здесь обеспечивается не одной «уверенностью модели», а архитектурой: `retrieved != applicable`, `draft != published answer`. Неизвестное существенное условие ведёт к `CLARIFY`, недостаток evidence — к handoff, unsupported draft — к повторной проверке/безопасному отказу от публикации.

### 3. Определение линии поддержки и нецензурной лексики — 10 баллов

Обе задачи вынесены из prompt'ов в проверяемые доменные политики.

**Routing.** [`RoutingPolicy`](src/support-core/src/TenderHack.Domain/Routing/RoutingPolicy.cs) возвращает не только линию, а полноценное решение: `ServiceNeed`, `RecommendedLine`, `DispatchQueue`, `EngineeringReviewSuggested` и traceable `ReasonCodes`. Политика различает явный запрос человека, нехватку evidence, condition-dependent сценарии и техническую диагностику; неоднозначность не превращается в уверенно выдуманную маршрутизацию. Регрессии: [`RoutingPolicyTests`](src/support-core/tests/TenderHack.Domain.Tests/RoutingPolicyTests.cs), [`DirectHumanRequestDetectorTests`](src/support-core/tests/TenderHack.Domain.Tests/DirectHumanRequestDetectorTests.cs).

**Moderation.** Сначала работает детерминированный слой: [`ModerationNormalizer`](src/support-core/src/TenderHack.Domain/Moderation/ModerationNormalizer.cs) нормализует обходные написания, [`ProfanityMatcher`](src/support-core/src/TenderHack.Domain/Moderation/ProfanityMatcher.cs) применяет versioned rules, а контекстная проверка нужна только для неоднозначных совпадений. Политика warning-first реализована серверно: первое подтверждённое нарушение предупреждает, повторное закрывает обращение до тяжёлого retrieval/generation-контура. Регрессии: [`ProfanityMatcherTests`](src/support-core/tests/TenderHack.Domain.Tests/ProfanityMatcherTests.cs), [`ModerationNormalizerTests`](src/support-core/tests/TenderHack.Domain.Tests/ModerationNormalizerTests.cs), [`ModerationPolicyTests`](src/support-core/tests/TenderHack.Domain.Tests/ModerationPolicyTests.cs), [`ModerationRegressionTests`](src/support-core/tests/TenderHack.Domain.Tests/ModerationRegressionTests.cs).

Сильная сторона этого критерия — объяснимость: routing и moderation дают воспроизводимый reason/state, который можно тестировать независимо от поведения LLM.

### 4. Методика оценки специалистов и качества донесения информации — 20 баллов

Quality-контур специально не сводит работу поддержки к одному рейтингу. В данных разделены **оценка специалиста**, **качество донесения информации**, **полезность ответа** и **факт решения проблемы**; это не позволяет автоматически обвинить сотрудника за негатив, вызванный пробелом базы знаний, сложным процессом или технической проблемой.

Методика строится в несколько слоёв:

1. `.NET api-worker` асинхронно передаёт в Knowledge факты turn/feedback/completion через outbox — см. [`QualityPushWorker`](src/support-core/src/TenderHack.Worker/QualityPushWorker.cs), [`QualityContracts`](src/support-core/src/TenderHack.Application/Knowledge/QualityContracts.cs), [`QualityOutboxMessages`](src/support-core/src/TenderHack.Application/Knowledge/QualityOutboxMessages.cs).
2. [`quality/schema.py`](src/knowledge/src/tenderhack_knowledge/quality/schema.py) хранит раздельные `specialist_rating`, `information_quality_rating`, `helpful`, `solved`, `specialist_ref`, outcome/handoff/routing facts и provenance.
3. [`quality/service.py`](src/knowledge/src/tenderhack_knowledge/quality/service.py) оценивает четыре независимые измерения: **factual support, completeness, clarity, next step**; критическая ошибка хранится отдельным флагом и не может «усредниться» хорошей формулировкой.
4. При нехватке данных используются `UNKNOWN` / `NOT_APPLICABLE`, а не искусственно точный балл.
5. Повторяющиеся случаи группируются с `sample_size`, representative cases, negative/unresolved counts, limitations и явно маркированными hypotheses — это даёт путь от оценки отдельного ответа к поиску системных причин.
6. Read-only результаты доступны через [`AnalyticsEndpoints`](src/support-core/src/TenderHack.Api/Endpoints/AnalyticsEndpoints.cs); нормативная методика и критерии проверки закреплены в [`docs/quality.md`](docs/quality.md).

Это делает методику пригодной и для оценки качества коммуникации специалиста, и для root-cause анализа: **negative feedback != плохой специалист**, а отсутствие данных != нулевая оценка.

### 5. Интуитивность веб-интерфейса — 10 баллов

Web построен вокруг пользовательского сценария, а не вокруг внутренних сущностей системы. Текущий UI уже реализует основные поверхности обращения: чат, восстановление серверного состояния, прогресс обращения, источники/материалы, объяснение применимости, handoff, completion/feedback, moderation и error states.

Основные точки code review:

- [`ChatScreen`](src/web/src/features/chat/ChatScreen.tsx) собирает единый рабочий экран обращения;
- [`caseStore`](src/web/src/state/caseStore.tsx) восстанавливает authoritative snapshot из API/SSE вместо создания параллельной клиентской state machine;
- [`ContextPanel`](src/web/src/features/chat/ContextPanel.tsx) объединяет использованные источники и нормативные материалы;
- [`ApplicabilityCard`](src/web/src/features/chat/ApplicabilityCard.tsx) отвечает на пользовательский вопрос «почему эта инструкция подходит мне» и визуально отличает verified context от предположения;
- [`RequestStatusStepper`](src/web/src/features/chat/RequestStatusStepper.tsx) делает жизненный цикл обращения наблюдаемым;
- [`HandoffCard`](src/web/src/features/chat/HandoffCard.tsx) переводит эскалацию в понятный управляемый сценарий;
- [`ResolutionFeedback`](src/web/src/features/chat/ResolutionFeedback.tsx) и [`FeedbackThanks`](src/web/src/features/chat/FeedbackThanks.tsx) закрывают outcome/feedback loop;
- [`SourceModal`](src/web/src/features/chat/SourceModal.tsx) позволяет открыть конкретный фрагмент/страницу, а не просто увидеть декоративную ссылку на документ.

Интерфейс сознательно показывает пользователю **следующий шаг, источник и реальное состояние**, а не chain-of-thought, псевдоточность confidence-процентов или локально придуманный статус.

## Что реализовано как продуктовый контур

Два связанных контура работают на одной модели кейса:

1. **Пользовательский:** сообщение → moderation/context → retrieval → answerability → `ANSWER`, `CLARIFY`, `HANDOFF_OFFER` или `ANSWER_AND_HANDOFF` → status/completion/feedback.
2. **Аналитический:** turns/completions/feedback → раздельная оценка качества текста, результата и пользовательских сигналов → read-only issue groups / hypotheses.

Поверх core развивается product-enhancement слой: Context Passport, Interactive Resolution Plan, Applicability Card, Smart Recovery, Emerging Issue Detector, Knowledge Gap Radar и presentation controls. Их правила и acceptance criteria: [`docs/product-experience.md`](docs/product-experience.md).

## Архитектура одним взглядом

```text
Browser / React
      │ HTTP + SSE
      ▼
.NET Support Core ───────────────► PostgreSQL (cases/state/outbox/feedback)
      │
      │ internal HTTP v0
      ▼
Python Knowledge & Inference ────► PostgreSQL (knowledge/quality)
      │
      └──────────────────────────► local generation runtime

.NET api-worker  ─► handoff / status / quality delivery
Python worker    ─► ingestion / quality / issue groups
```

Главные ownership-правила:

- Browser ходит только в `.NET api`;
- `.NET` принимает product/business decisions; Knowledge возвращает evidence/facts/assessments;
- исторические тикеты — аналитический корпус, не нормативная база ответов;
- `ANSWER != RESOLVED`;
- `handoff prepared != accepted`;
- infrastructure/model failure != «в базе нет ответа»;
- inferred/user data не превращаются в verified Portal state;
- frontend отображает backend state, а не изобретает собственную бизнес-истину.

Подробно: [`docs/architecture.md`](docs/architecture.md), [`ADR-0001`](docs/adr/0001-dotnet-support-core-python-knowledge-service.md), [`docs/product-spec.md`](docs/product-spec.md).

## Стек

- **Web:** React + TypeScript + Vite, Node.js 24 LTS, pnpm.
- **Support Core:** C# / .NET 10 LTS, ASP.NET Core Minimal API, EF Core + Npgsql.
- **Knowledge & inference:** Python 3.12, FastAPI, Pydantic v2, SQLAlchemy 2, Alembic, asyncpg.
- **Persistence:** PostgreSQL 16 с раздельным ownership таблиц между runtime'ами.
- **Production retrieval:** exact entities/codes + PostgreSQL FTS + `pg_trgm`.
- **Generation:** `empero-ai/Qwen3.8-4B-Distill`, Q6_K GGUF, local-only через llama.cpp.
- **Ranking extensions:** optional/gated; основной support flow не зависит от доступности отдельного reranker runtime.
- **Deployment:** Docker Compose; model artifacts и organizer data не коммитятся как runtime weights/secrets.

Подробности: [`docs/stack.md`](docs/stack.md), [`docs/adr/`](docs/adr/).

## Структура репозитория

```text
src/
  support-core/   .NET Domain / Application / Infrastructure / Api / Worker + tests
  knowledge/      Python Knowledge/Inference + worker + migrations + benchmarks
  web/            React/Vite product UI + component tests
evals/            evaluation workspace
tests/e2e/        cross-runtime smoke/E2E workspace
infra/            Compose/bootstrap/inference configuration
scripts/          ingestion/dev/reproducibility helpers
docs/             system of record + ADR/contracts/plans
```

## Быстрая ручная проверка

После изменения кода образы нужно пересобрать — `git pull` сам не обновляет уже собранные Docker images.

```bash
docker compose --profile stub up -d --build
powershell -ExecutionPolicy Bypass -File .\scripts\bootstrap-knowledge.ps1
```

После bootstrap UI доступен на `http://localhost:5173`. Профиль `stub` — детерминированный development/test double генератора для воспроизводимой локальной проверки ветки `ANSWER`; production/demo inference contract остаётся тем же OpenAI-compatible internal endpoint и может работать с локальным Qwen runtime.

### Тестовая админ-панель

`http://localhost:8080/admin` — отдельная страница на `api` (не часть SPA и не часть `web-api-v0`): сводка по вопросам за период из таблиц `api` — ходы и решения (`ANSWER/CLARIFY/HANDOFF_OFFER/…`), статусы, handoff, завершения, feedback, разбивка по дням, частые вопросы и список сообщений. JSON: `GET /admin/api/summary?from=<ISO>&to=<ISO>&limit=200` (по умолчанию последние 24 ч). Ролей нет — включается `ADMIN_PANEL_ENABLED` (по умолчанию `true`), на любом общем хосте задайте `ADMIN_PANEL_TOKEN` (передаётся как `X-Admin-Token` или `?token=`) или выключите.

## Базовые проверки

Полная стратегия качества и release gate: [`docs/quality.md`](docs/quality.md). Базовые команды:

```text
dotnet build src/support-core/TenderHack.sln -c Release
dotnet test src/support-core/TenderHack.sln -c Release
uv sync --extra test --project src/knowledge
uv run --project src/knowledge pytest
pnpm --dir src/web install
pnpm --dir src/web typecheck
pnpm --dir src/web test
pnpm --dir src/web build
docker compose config --quiet
```

Фактический статус последней сквозной проверки, demo-профилей и runtime evidence хранится отдельно от архитектурных обещаний: [`docs/plans/active/2026-09-demo-readiness.md`](docs/plans/active/2026-09-demo-readiness.md).

## Карта документации

- [`docs/hackathon-requirements.md`](docs/hackathon-requirements.md) — формальные требования кейса;
- [`docs/product-spec.md`](docs/product-spec.md) — core product policy/state/decisions;
- [`docs/product-experience.md`](docs/product-experience.md) — продуктовые улучшения и acceptance criteria;
- [`docs/architecture.md`](docs/architecture.md) — ownership, boundaries, state, workers;
- [`docs/stack.md`](docs/stack.md) — runtime/model/technology policy;
- [`docs/contracts/`](docs/contracts/) — frozen inter-service/API boundaries;
- [`docs/quality.md`](docs/quality.md) — методика тестирования, evals и Definition of Done;
- [`docs/plans/active/`](docs/plans/active/) — текущие execution/readiness планы;
- [`docs/plans/completed/`](docs/plans/completed/) — история завершённых решений;
- [`docs/references.md`](docs/references.md) — внешние первичные источники.

## Работа coding agents

Для нетривиальных задач root-agent сначала фиксирует source-of-truth и acceptance criteria, затем выполняет ограниченное делегирование, интеграцию, self-review, skeptic-review, verification и docs sync. Протокол: [`docs/agent-workflow.md`](docs/agent-workflow.md).
