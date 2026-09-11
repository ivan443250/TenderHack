# TenderHack 2026 — интеллектуальная поддержка Портала поставщиков

Репозиторий команды для кейса TenderHack НН 2026: локальная система поддержки, которая отвечает только по проверяемым знаниям, умеет честно уточнять/отказываться/предлагать передачу специалисту и отдельно анализирует качество ответов и повторяющиеся проблемы.

## Статус

`master` намеренно очищен от раннего .NET-шаблона. Предыдущий scaffold фиксировал архитектуру до финального ревью требований и конфликтовал с принятой спецификацией. Текущая база — **docs-first, agent-first**: сначала зафиксированы формальные ограничения, продуктовые инварианты, архитектура, стек, quality gates и правила работы coding agents; реализация должна строиться поверх них.

## Что строим

Два равноправных контура:

1. **Пользовательский:** вопрос → контекст → поиск и проверка знаний → `ANSWER / CLARIFY / HANDOFF_OFFER / ANSWER_AND_HANDOFF` → понятный статус → feedback.
2. **Аналитический:** доступные обращения/ответы/отзывы → раздельная оценка качества текста, результата и обратной связи → объяснимые группы повторяющихся проблем.

Не строим полноценный helpdesk, автономное изменение сущностей Портала, voice, GraphRAG «для инновационности» или swarm автономных runtime-агентов.

## Принятый стек

- **Web:** React + TypeScript + Vite, Node.js 24 LTS, pnpm.
- **API / orchestration:** Python 3.12, FastAPI, Pydantic v2.
- **Persistence:** PostgreSQL 16, SQLAlchemy 2, Alembic, asyncpg.
- **Search:** PostgreSQL FTS + `pg_trgm` + `pgvector`; сначала exact vector search.
- **Parsing:** `pdfplumber` baseline; Docling/OCR адресно после измерения качества.
- **Embeddings:** `Qwen3-Embedding-0.6B`, до 1024 dimensions.
- **Reranker:** `BAAI/bge-reranker-v2-m3`.
- **Generation:** `Qwen3-4B-Instruct-2507`, local-only.
- **Inference:** vLLM после hardware smoke-test; один fallback на llama.cpp при несовместимости/нехватке VRAM.
- **Deployment:** Docker Compose; один modular monolith + отдельный worker process того же Python-проекта.

Подробности и обоснования: [`docs/stack.md`](docs/stack.md) и [`docs/architecture.md`](docs/architecture.md).

## Карта документации

Начинать с [`AGENTS.md`](AGENTS.md), затем открывать только нужные документы:

- [`docs/index.md`](docs/index.md) — карта источников истины;
- [`docs/hackathon-requirements.md`](docs/hackathon-requirements.md) — стабильный слой требований, ограничений, критериев и защиты;
- [`docs/product-spec.md`](docs/product-spec.md) — продуктовые границы и логика решений;
- [`docs/architecture.md`](docs/architecture.md) — модули, состояния, data boundaries;
- [`docs/stack.md`](docs/stack.md) — выбранный стек и rejected alternatives;
- [`docs/agent-workflow.md`](docs/agent-workflow.md) — обязательный цикл coding agents, subagents и skeptic review;
- [`docs/quality.md`](docs/quality.md) — тестирование, evals и Definition of Done;
- [`docs/execution-plan.md`](docs/execution-plan.md) — порядок реализации и gates;
- [`docs/references.md`](docs/references.md) — первичные источники и OpenAI guidance.

## Главные инженерные правила

- Внешние AI API не используются в интеллектуальном контуре.
- Исторические решения тикетов — аналитический корпус, **не** нормативная база ответов.
- Наличие похожего фрагмента не означает answerability.
- `ANSWER` не означает `RESOLVED`.
- `handoff prepared` не означает `handoff accepted`.
- Пользовательские факты не превращаются в verified Portal state.
- Нельзя показывать chain-of-thought как «прозрачность»; показываем source, condition, reason code, route и observable event.
- Для рискованных/неподтвержденных условий безопасный отказ или передача лучше уверенной галлюцинации.

## Планируемая структура после scaffold

```text
apps/
  api/          FastAPI modular monolith
  web/          React/Vite UI
worker/         background jobs using the same domain/application code
evals/          retrieval, decision, moderation, quality and E2E suites
scripts/        ingestion/dev/reproducibility helpers
docs/           repository knowledge system of record
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
