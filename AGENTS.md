# AGENTS.md

`AGENTS.md` — короткая карта репозитория для coding agents. Глубокий контекст живёт в `docs/`; не дублируй его здесь и не превращай этот файл в энциклопедию.

## 1. Старт любой нетривиальной задачи

1. Проверь текущую ветку, `git status` и diff. Не затирай чужую работу.
2. Прочитай `docs/index.md`.
3. Открой только релевантные source-of-truth docs. Для user-facing UX, recovery, context или analytics enhancement обязательно открой `docs/product-experience.md`.
4. Проверь `docs/open-decisions.md`: не превращай нерешённую политику в скрытый факт.
5. Сформулируй acceptance criteria до кода.
6. Если меняется внешний/межмодульный интерфейс — сначала открой `docs/contracts/README.md` и соответствующий контракт.
7. Для большой/многомодульной работы используй execution plan по `docs/agent-workflow.md`; текущий progress не выводи из исторического `docs/execution-plan.md`.

Подробный manager/subagent/review loop: `docs/agent-workflow.md`.

## 2. Приоритет источников

1. Официальные требования организаторов и подтверждённые экспертные уточнения.
2. Явные текущие решения команды/пользователя, если они не противоречат п.1.
3. `docs/hackathon-requirements.md`.
4. `docs/product-spec.md`.
5. `docs/product-experience.md` для принятого enhancement-поведения; он не переопределяет core state/decision policy из `product-spec.md`.
6. `docs/architecture.md`, `docs/stack.md`, ADR и frozen contracts.
7. Active plans.
8. Код/тесты как факт реализации.
9. Completed plans, historical benchmarks, старые drafts/comments — только как provenance.

Если источники конфликтуют — не выбирай молча. Обнови source of truth или зафиксируй нерешённый конфликт в `docs/open-decisions.md`.

## 3. Неподвижные архитектурные инварианты

- Source layout: `src/support-core`, `src/knowledge`, `src/web`.
- Browser ходит только в `.NET api`; `knowledge` наружу не публикуется.
- Один product orchestrator/state machine находится в `.NET`.
- `.NET` владеет `Decision`, case/turn state, moderation policy, routing, handoff, completion, notifications, feedback и idempotency.
- Python `knowledge` владеет ingestion, retrieval, evidence/answerability, draft/verify, model adapters и read-only quality analytics.
- `knowledge` возвращает факты/скоры/evidence; он **никогда** не возвращает `Decision`, `should_handoff`, `HandoffStatus` или decision-family `reason_codes`.
- Runtime dependency: `api → knowledge`; обратных HTTP-вызовов и cross-runtime DB reads нет.
- Одна PostgreSQL допустима, но `api_rw` и `knowledge_rw` не читают/не мигрируют таблицы друг друга.
- Normative answer corpus и historical analytics corpus разделены. Историческое `Решение` не является нормативным ответом.
- `ANSWER != RESOLVED`; prepared handoff != accepted handoff.
- Этап/специалист/terminal handoff — только факты адаптера; `null` остаётся `null`, simulated всегда помечен.
- Infrastructure failure != «в базе нет ответа».
- Внешние LLM/search API не используются в штатном интеллектуальном контуре.
- Не выводить chain-of-thought в UI/logs/docs.

Изменение runtime boundary, нового сервиса/queue/DB/state dimension или принятой model/runtime policy требует ADR.

## 4. Контракты прежде реализации

Реестр: `docs/contracts/README.md`.

| Граница | Source of truth |
|---|---|
| `.NET api ↔ Python knowledge` | `docs/contracts/knowledge-v0.md` + `knowledge-v0.openapi.yaml` |
| Browser ↔ `.NET api` | `docs/contracts/web-api-v0.md` |
| `.NET Application ↔ support adapter` | `docs/contracts/support-adapter-v0.md` |
| State/data ownership | `docs/architecture.md` |

Правила:

- breaking change нельзя маскировать refactor'ом;
- additive поле optional, пока обе стороны не мигрировали;
- generated DTO не протаскиваются в Domain/Application;
- boundary input валидируется до бизнес-логики;
- контракт и обе стороны синхронизируются одним coordinated change;
- frontend не изобретает state/fact, которого нет в API;
- product-experience feature не меняет frozen contract «по намерению»: нужные поля/routes/events сначала документируются и тестируются.

## 5. Рабочие блоки

Карта параллельной разработки: `docs/workstreams.md`.

| Блок | Владелец/runtime | Читать сначала |
|---|---|---|
| Support Core / state / orchestration | `.NET api` | `architecture.md`, `product-spec.md`, `contracts/` |
| Knowledge / retrieval / generation | Python `knowledge` | `product-spec.md`, `knowledge-v0.*`, `quality.md`, `stack.md` |
| Ingestion / KB | `knowledge-worker` | `product-spec.md §6–9`, `architecture.md §8–9` |
| Web / chat timeline / product UX | React | `web-api-v0.md`, `product-spec.md`, `product-experience.md`, `architecture.md §13–14` |
| Handoff integration | `.NET api-worker` | `support-adapter-v0.md`, `product-spec.md §17`, ADR-0002 |
| Quality / issue analytics | Python `knowledge-worker` | `quality.md`, `knowledge-v0.*`, `product-experience.md §7–8` |
| Deployment / observability | cross-cutting | `stack.md`, `architecture.md §16–18` |

Не редактируй чужой блок «заодно», если это не требуется контрактом задачи.

## 6. Обязательный цикл для существенного change

```text
DISCOVER → DEFINE ACCEPTANCE → PLAN → IMPLEMENT
→ SELF-REVIEW → SKEPTIC REVIEW → FIX → VERIFY → DOC SYNC
```

Root agent остаётся интегратором. Subagents используются для независимых workstreams/review, но не для параллельного редактирования одного shared contract.

Перед завершением проверь минимум:

- product invariant/state transitions;
- contract compatibility;
- idempotency/retry/failure path;
- provenance и corpus separation;
- secrets/PII/logging;
- docs/code drift и broken paths;
- отсутствие decision-логики в `knowledge`;
- отсутствие cross-runtime DB reads;
- product-facing feature убирает реальную работу/неопределённость, а не добавляет AI-theater;
- inferred/simulated/hypothesis нигде не выглядят как verified Portal/support fact;
- исторический benchmark/model stack не выдан за текущий.

## 7. Verification и Definition of Done

Полные gates и реальные команды: `docs/quality.md`. Для product enhancement дополнительно применяй acceptance/review checklist из `docs/product-experience.md`.

Не заявляй passing tests/benchmarks, если они не запускались на текущем change/config. Manifests и runnable runtime уже существуют: не используй старую формулировку «scaffold ещё отсутствует» как основание пропустить проверки. Если реальная команда проверки меняется, синхронизируй `docs/quality.md` тем же change.

Для bugfix — regression case до/вместе с fix, если разумно. Для boundary changes — contract test обязателен.

## 8. Документационный drift

При работе с docs отличай active truth от истории:

- accepted ADR сохраняет исторический контекст и не переписывается только потому, что позже появился код;
- completed plan живёт в `docs/plans/completed/`, не в `active/`;
- stub/fixture mode — тестовая способность, если real implementation уже существует;
- `TBD`, `target`, `not implemented`, `scaffold` допустимы только для конкретного реально незакрытого gap;
- module README описывает текущий модуль, а не состояние первого foundation-коммита;
- model/runtime selection и measured certification — разные вещи.

## 9. Git и данные

- Не force-push/amend без прямого запроса.
- Не коммить raw organizer data, model weights, secrets, generated caches.
- Не восстанавливать retired scaffold из `1b57aa1`.
- Папка документации — `docs/` в нижнем регистре; не создавай параллельный `Docs/`.
- Перед финалом перечитай полный diff и текущий branch HEAD.
- Если docs описывают target, а runtime ещё не подтверждён, называй это **documented target / unverified**, не `implemented` или `certified`.
