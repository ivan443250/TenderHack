# TenderHack 2026 — интеллектуальная поддержка Портала поставщиков

Репозиторий команды для кейса TenderHack НН 2026: локальная система поддержки, которая отвечает только по проверяемым знаниям, умеет честно уточнять/отказываться/предлагать передачу специалисту и отдельно анализирует качество ответов и повторяющиеся проблемы.

## Статус

`master` намеренно очищен от раннего .NET-шаблона (`1b57aa1`): его доменная модель фиксировала архитектуру до финального ревью требований. Текущая база — **docs-first, agent-first**: сначала зафиксированы формальные ограничения, продуктовые инварианты, архитектура, стек, quality gates и правила работы coding agents; реализация должна строиться поверх них.

Backend разделён на .NET support core и Python knowledge-сервис по [`docs/adr/0001-dotnet-support-core-python-knowledge-service.md`](docs/adr/0001-dotnet-support-core-python-knowledge-service.md). Новый `apps/api` пишется по текущему `architecture.md`, а не восстанавливается из старого scaffold.

## Что строим

Два равноправных контура:

1. **Пользовательский (чат-first):** вопрос → контекст → поиск и проверка знаний → `ANSWER` с кнопкой источника / `CLARIFY` / «подтверждённого ответа нет» + кнопка оператора / `ANSWER_AND_HANDOFF` → предупреждение и закрытие при мате → виджет статуса передачи (этап/специалист только из фактов адаптера) → завершение с уведомлением → архив + feedback из четырёх сигналов.
2. **Аналитический:** доступные обращения/ответы/отзывы → раздельная оценка качества текста, результата и обратной связи → объяснимые группы повторяющихся проблем.

Не строим полноценный helpdesk, автономное изменение сущностей Портала, voice, GraphRAG «для инновационности» или swarm автономных runtime-агентов.

## Принятый стек

- **Web:** React + TypeScript + Vite, Node.js 24 LTS, pnpm.
- **Support core (`api`, `api-worker`):** C# / .NET 10 LTS, ASP.NET Core Minimal API, EF Core + Npgsql. Владеет cases, state machine, `Decision`, moderation rules, routing, handoff/outbox + status sync с адаптером, завершением/архивом, уведомлениями, feedback, HTTP/SSE.
- **Knowledge & inference (`knowledge`, `knowledge-worker`):** Python 3.12, FastAPI, Pydantic v2, SQLAlchemy 2, Alembic, asyncpg. Владеет ingestion, retrieval, моделями, answerability/verify, quality analytics.
- **Контракт между ними:** внутренний HTTP `v0`, OpenAPI → сгенерированный C#-клиент; Python возвращает факты/скоры, решения принимает .NET.
- **Persistence:** PostgreSQL 16, одна БД, строгое владение таблицами по runtime.
- **Search:** PostgreSQL FTS + `pg_trgm` + `pgvector`; сначала exact vector search.
- **Parsing:** `pdfplumber` baseline; Docling/OCR адресно после измерения качества.
- **Embeddings:** `Qwen3-Embedding-0.6B`, до 1024 dimensions.
- **Reranker:** `BAAI/bge-reranker-v2-m3`.
- **Generation:** `Qwen3-4B-Instruct-2507`, local-only.
- **Inference:** vLLM после hardware smoke-test; один fallback на llama.cpp при несовместимости/нехватке VRAM.
- **Deployment:** Docker Compose; `web`, `api`, `api-worker`, `knowledge`, `knowledge-worker`, `postgres`, `inference`.

Подробности и обоснования: [`docs/stack.md`](docs/stack.md) и [`docs/architecture.md`](docs/architecture.md).

## Карта документации

Начинать с [`AGENTS.md`](AGENTS.md), затем открывать только нужные документы:

- [`docs/index.md`](docs/index.md) — карта источников истины;
- [`docs/hackathon-requirements.md`](docs/hackathon-requirements.md) — стабильный слой требований, ограничений, критериев и защиты;
- [`docs/product-spec.md`](docs/product-spec.md) — продуктовые границы и логика решений;
- [`docs/architecture.md`](docs/architecture.md) — модули, состояния, data boundaries;
- [`docs/stack.md`](docs/stack.md) — выбранный стек и rejected alternatives;
- [`docs/adr/`](docs/adr/) — architecture decision records; ADR-0001 — граница .NET `api` / Python `knowledge`; ADR-0002 — inbound handoff status sync и in-app уведомления;
- [`docs/contracts/`](docs/contracts/) — замороженные контракты границ (`knowledge-v0`, `web-api-v0`, `support-adapter-v0`);
- [`docs/open-decisions.md`](docs/open-decisions.md) — нерешённые вопросы, которые нельзя выбирать молча;
- [`docs/agent-workflow.md`](docs/agent-workflow.md) — обязательный цикл coding agents, subagents и skeptic review;
- [`docs/quality.md`](docs/quality.md) — тестирование, evals и Definition of Done;
- [`docs/execution-plan.md`](docs/execution-plan.md) — порядок реализации и gates;
- [`docs/references.md`](docs/references.md) — первичные источники и OpenAI guidance.

## Главные инженерные правила

- Внешние AI API не используются в интеллектуальном контуре.
- Исторические решения тикетов — аналитический корпус, **не** нормативная база ответов.
- Наличие похожего фрагмента не означает answerability.
- `ANSWER` не означает `RESOLVED`; `RESOLVED` ставит только пользователь (`complete`) или терминальный факт адаптера.
- `handoff prepared` не означает `handoff accepted`; этап и специалист показываются только если их сообщил адаптер.
- Пользовательские факты не превращаются в verified Portal state.
- Нельзя показывать chain-of-thought как «прозрачность»; показываем source, condition, reason code, route и observable event.
- Для рискованных/неподтвержденных условий безопасный отказ или передача лучше уверенной галлюцинации.

## Планируемая структура после scaffold

```text
apps/
  api/          .NET solution: Domain / Application / Infrastructure / Api / Worker + tests
  knowledge/    Python FastAPI knowledge & inference service + worker entrypoint
  web/          React/Vite UI
evals/          retrieval, decision, moderation, quality and E2E suites
scripts/        ingestion/dev/reproducibility helpers
docs/           repository knowledge system of record (+ docs/adr/)
```

Не создавать эти каталоги пустыми ради вида. Первый implementation change должен создать только реально используемый scaffold и одновременно обновить команды в `AGENTS.md`/docs.

## Первый implementation gate

До расширения функций должен работать один вертикальный путь на реальных документах:

```text
реальный вопрос
→ локальный retrieval
→ применимый fragment/source
→ проверенный ответ или честный abstain
→ состояние сохраняется
→ UI открывает источник
```

После этого добавляются routing/moderation/handoff, затем quality analytics, затем улучшения retrieval и polish.

## Работа coding agents

Для нетривиальных задач root-agent действует как manager: читает карту docs, формирует короткий план, параллелит независимые исследования/проверки через subagents, интегрирует изменения, делает self-review, затем запускает независимый skeptic review и только после исправлений/проверок считает работу завершенной. Подробный протокол: [`docs/agent-workflow.md`](docs/agent-workflow.md).
