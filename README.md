# TenderHack 2026 — интеллектуальная поддержка Портала поставщиков

Локальная система поддержки Портала поставщиков: отвечает только по проверяемым знаниям, умеет уточнять существенные условия, честно abstain/предлагать передачу специалисту и отдельно анализирует качество ответов и повторяющиеся проблемы.

## Текущее состояние репозитория

`master` — уже не foundation-only scaffold. В репозитории существуют три runtime-блока (`src/support-core`, `src/knowledge`, `src/web`), PostgreSQL/Compose-инфраструктура, замороженные контракты и существенная реализация Support Core/Knowledge. Web пока остаётся минимальным shell и является одним из основных незавершённых product surfaces.

Исторический .NET-шаблон из `1b57aa1` не используется. Текущая архитектура — .NET Support Core + Python Knowledge/Inference по [`ADR-0001`](docs/adr/0001-dotnet-support-core-python-knowledge-service.md). Исторические plans/benchmarks сохраняются как provenance и не должны читаться как описание текущей реализации.

## Что строим

Два связанных контура:

1. **Пользовательский, chat-first:** сообщение → moderation/context → retrieval → answerability → проверенный `ANSWER`, полезный `CLARIFY` или честный `HANDOFF_OFFER` / `ANSWER_AND_HANDOFF` → при передаче статус только из фактов адаптера → completion/archive/feedback.
2. **Аналитический:** turns/completions/feedback → раздельная оценка качества текста, результата и пользовательских сигналов → read-only группы повторяющихся проблем и гипотезы.

Поверх core принят отдельный product-enhancement слой: Context Passport, Resolution Plan, Applicability Card, Smart Recovery, Emerging Issue Detector, Knowledge Gap Radar и presentation controls. Их точная семантика и ограничения: [`docs/product-experience.md`](docs/product-experience.md).

Не строим полноценный helpdesk, автономное изменение сущностей Портала, voice, GraphRAG «для инновационности» или swarm автономных runtime-агентов.

## Принятый стек

- **Web:** React + TypeScript + Vite, Node.js 24 LTS, pnpm.
- **Support Core (`api`, `api-worker`):** C# / .NET 10 LTS, ASP.NET Core Minimal API, EF Core + Npgsql. Владеет cases/state machine/`Decision`, moderation, routing, handoff/outbox/status sync, completion/archive, notifications, feedback, HTTP/SSE.
- **Knowledge & inference (`knowledge`, `knowledge-worker`):** Python 3.12, FastAPI, Pydantic v2, SQLAlchemy 2, Alembic, asyncpg. Владеет ingestion, retrieval, answerability/draft/verify, model adapters и read-only quality analytics.
- **Контракт:** внутренний HTTP `v0`; Python возвращает facts/scores/evidence, финальные product decisions принимает .NET.
- **Persistence/retrieval:** PostgreSQL 16 + FTS + `pg_trgm` + `pgvector`; отдельной vector DB нет.
- **Embeddings:** `ai-sage/Giga-Embeddings-instruct-3B-0826`, 2048 dimensions; выбор принят ADR-0003, runtime/quality подтверждаются отдельной certification-процедурой.
- **Reranker:** `Querit/Querit-4B`; включается только после сертификации runtime/scoring path, иначе safe fallback без reranker.
- **Generation:** `empero-ai/Qwen3.8-4B-Distill`, Q6_K GGUF, local-only через llama.cpp.
- **Deployment:** Docker Compose; model artifacts и organizer data монтируются снаружи git.

Подробности и актуальные ограничения: [`docs/stack.md`](docs/stack.md), [`docs/architecture.md`](docs/architecture.md), [`docs/adr/0003-model-stack-v2.md`](docs/adr/0003-model-stack-v2.md).

## Карта документации

Начинать с [`AGENTS.md`](AGENTS.md) и [`docs/index.md`](docs/index.md). Основные sources of truth:

- [`docs/hackathon-requirements.md`](docs/hackathon-requirements.md) — формальный слой требований;
- [`docs/product-spec.md`](docs/product-spec.md) — core product policy/state/decisions;
- [`docs/product-experience.md`](docs/product-experience.md) — принятый product-enhancement вектор;
- [`docs/architecture.md`](docs/architecture.md) — ownership, state, boundaries, persistence/workers;
- [`docs/stack.md`](docs/stack.md) — стек/model/runtime policy;
- [`docs/contracts/`](docs/contracts/) — frozen boundaries;
- [`docs/open-decisions.md`](docs/open-decisions.md) — реально открытые external/policy gaps;
- [`docs/quality.md`](docs/quality.md) — tests/evals/DoD;
- [`docs/plans/active/`](docs/plans/active/) — только незавершённые execution/certification plans;
- [`docs/plans/completed/`](docs/plans/completed/) — исторические завершённые планы;
- [`docs/references.md`](docs/references.md) — внешние первичные источники, включая OpenAI guidance и model/runtime sources.

## Главные инженерные правила

- Browser ходит только в `.NET api`; `knowledge` не является вторым публичным backend.
- `.NET` принимает product/business decisions; Knowledge возвращает evidence/facts/scores.
- Исторические решения тикетов — аналитический корпус, **не** нормативная база ответов.
- Наличие похожего фрагмента не означает answerability.
- `ANSWER != RESOLVED`; positive feedback тоже не означает resolution.
- `handoff prepared != accepted`; stage/specialist/terminal — только факты адаптера, simulated всегда помечен.
- Infrastructure/model failure != «в базе нет ответа».
- Пользовательские/inferred данные не превращаются в verified Portal state.
- Chain-of-thought не является прозрачностью: показываем источники, условия, observable events, route/reason и limitations.
- Product-enhancement функция не может молча расширить frozen contract или создать новый authoritative state в frontend.

## Структура репозитория

```text
src/
  support-core/   .NET Domain / Application / Infrastructure / Api / Worker + tests
  knowledge/      Python Knowledge/Inference + worker + migrations + benchmarks
  web/            React/Vite UI (сейчас минимальный shell)
evals/            repository-level evaluation workspace
tests/e2e/        cross-runtime smoke/E2E workspace
infra/            Compose/bootstrap/inference configuration
scripts/          ingestion/dev/reproducibility helpers
docs/             system of record + ADR/contracts/plans
```

## Запуск для ручной проверки (без RunPod)

Образы не пересобираются автоматически — после `git pull` всегда `docker compose build`, иначе стек может часами работать на образах со старым кодом.

```bash
docker compose --profile stub up -d --build
powershell -ExecutionPolicy Bypass -File .\scripts\bootstrap-knowledge.ps1
```

Профиль `stub` поднимает `generator-stub` — извлекающий тестовый дублёр реального генератора (`infra/inference/stub/README.md`), без него ветка `ANSWER` падает в `MODEL_UNAVAILABLE`. `bootstrap-knowledge.ps1` идемпотентно загружает корпус (6 PDF, ~3900 фрагментов). После этого UI — `http://localhost:5173`.

## Базовые проверки

Актуальный список и интерпретация результатов — [`docs/quality.md`](docs/quality.md). Основные команды:

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

Не переносить historical benchmark на новый model stack и не называть выбранный runtime/model `certified`, пока соответствующий active certification plan не дал measured evidence.

## Работа coding agents

Для нетривиальных задач root-agent действует как manager: сначала source-of-truth/acceptance, затем ограниченное параллельное делегирование, интеграция, self-review, независимый skeptic review, verification и docs sync. Подробный протокол: [`docs/agent-workflow.md`](docs/agent-workflow.md).
