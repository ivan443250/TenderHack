# AGENTS.md

`AGENTS.md` — короткая карта репозитория для coding agents. Глубокий контекст живёт в `docs/`; не дублируй его здесь.

## 1. Старт любой нетривиальной задачи

1. Проверь текущую ветку, `git status` и diff. Не затирай чужую работу.
2. Прочитай `docs/index.md`.
3. Открой только документы, релевантные задаче. Для user-facing UX, recovery, context или analytics enhancement обязательно открой `docs/product-experience.md`.
4. Проверь `docs/open-decisions.md`: не превращай нерешённую политику в скрытый факт.
5. Сформулируй acceptance criteria до кода.
6. Если меняется внешний/межмодульный интерфейс — сначала открой `docs/contracts/README.md` и соответствующий контракт.
7. Для работы дольше одной сессии или на нескольких блоках — используй execution plan по `docs/agent-workflow.md`.

Подробный manager/subagent/review loop: `docs/agent-workflow.md`.

## 2. Приоритет источников

1. Официальные требования организаторов и подтверждённые экспертные уточнения.
2. Явные текущие решения команды/пользователя, если они не противоречат п.1.
3. `docs/hackathon-requirements.md`.
4. `docs/product-spec.md`.
5. `docs/product-experience.md` для принятого product-enhancement поведения; он не переопределяет core state/decision policy из `product-spec.md`.
6. `docs/architecture.md`, `docs/stack.md`, ADR и frozen contracts.
7. Код, комментарии и старые drafts.

Если источники конфликтуют — не выбирай молча. Зафиксируй конфликт в `docs/open-decisions.md` или обнови source of truth тем же change.

## 3. Неподвижные архитектурные инварианты

- Физический source layout: `src/support-core`, `src/knowledge`, `src/web`; это layout change, а не новый runtime.
- Browser ходит только в `.NET api`; `knowledge` наружу не публикуется.
- Один product orchestrator/state machine находится в `.NET`.
- `.NET` владеет `Decision`, case/turn state, moderation policy, routing, handoff, feedback и idempotency.
- Python `knowledge` владеет ingestion, retrieval, evidence/answerability, draft/verify, model adapters и quality analytics.
- `knowledge` возвращает факты/скоры/evidence; он **никогда** не возвращает `Decision`, `should_handoff`, `HandoffStatus` или decision-family `reason_codes`.
- Направление runtime-зависимости: `api → knowledge`. Обратных HTTP-вызовов нет.
- Одна PostgreSQL допустима, но `api_rw` и `knowledge_rw` не читают/не мигрируют таблицы друг друга; общих views/ролей нет.
- Normative answer corpus и historical analytics corpus разделены. Историческое `Решение` не является ответом пользователю.
- `ANSWER != RESOLVED`; prepared handoff != accepted handoff. `RESOLVED` ставят только явный `complete` пользователя или терминальный факт адаптера.
- Этап/специалист/терминальный статус передачи — только факты адаптера (poll или подписанный webhook, ADR-0002); `null` остаётся `null`, UI не показывает заглушек.
- Уведомления — api-owned факты (inbox + owner SSE + Web Notifications API); внешних push/email в P0 нет.
- Модерация warning-first: порог — серверная конфигурация, не UI.
- Infrastructure failure != «в базе нет ответа».
- Внешние LLM/search API не используются в штатном интеллектуальном контуре.
- Не выводить chain-of-thought в UI/logs/docs.

Изменение границы runtime, нового сервиса/queue/DB/state dimension требует ADR.

## 4. Контракты прежде реализации

Реестр: `docs/contracts/README.md`.

| Граница | Source of truth |
|---|---|
| `.NET api ↔ Python knowledge` | `docs/contracts/knowledge-v0.md` + `knowledge-v0.openapi.yaml` |
| Browser ↔ `.NET api` | `docs/contracts/web-api-v0.md` |
| `.NET Application ↔ support adapter` | `docs/contracts/support-adapter-v0.md` |
| State/data ownership | `docs/architecture.md` |

Правила:

- breaking change контракта нельзя маскировать refactor'ом;
- additive поле должно быть optional, пока обе стороны не мигрировали;
- generated DTO не протаскиваются в Domain/Application;
- boundary input валидируется до бизнес-логики;
- контракт и обе стороны синхронизируются одним change/PR, когда это возможно;
- frontend не изобретает state, которого нет в API contract.

## 5. Рабочие блоки

Карта параллельной разработки: `docs/workstreams.md`.

| Блок | Владелец/runtime | Читать сначала |
|---|---|---|
| Support Core / state / orchestration | `.NET api` | `architecture.md`, `product-spec.md`, `contracts/` |
| Knowledge / retrieval / generation | Python `knowledge` | `product-spec.md`, `knowledge-v0.*`, `quality.md` |
| Ingestion / KB | `knowledge-worker` | `product-spec.md §6–9`, `architecture.md §8–9` |
| Web / chat timeline / product UX | React | `web-api-v0.md`, `product-spec.md`, `product-experience.md`, `architecture.md §13–14` |
| Handoff integration | `.NET api-worker` | `support-adapter-v0.md`, `product-spec.md §17`, `product-experience.md` |
| Quality / issue analytics | Python `knowledge-worker` | `quality.md`, `knowledge-v0.*`, `product-experience.md §7–8` |
| Deployment / observability | cross-cutting | `stack.md`, `architecture.md §16–18` |

Не редактируй чужой блок «заодно», если это не требуется контрактом задачи.

## 6. Обязательный цикл для существенного change

```text
DISCOVER → DEFINE ACCEPTANCE → PLAN → IMPLEMENT
→ SELF-REVIEW → SKEPTIC REVIEW → FIX → VERIFY → DOC SYNC
```

Root agent остаётся интегратором. Subagents можно использовать для независимых workstreams/review, но не для параллельного редактирования одного shared contract.

Перед завершением проверь минимум:

- product invariant/state transitions;
- contract compatibility;
- idempotency/retry/failure path;
- provenance и corpus separation;
- secrets/PII/logging;
- docs/code drift;
- отсутствие decision-логики в `knowledge`;
- отсутствие cross-runtime DB reads;
- product-facing feature убирает реальную работу/неопределённость, а не добавляет AI-theater;
- inferred/simulated/hypothesis данные нигде не выглядят как verified Portal/support fact.

## 7. Verification и Definition of Done

Полные gates: `docs/quality.md`. Для product enhancement дополнительно применяй acceptance/review checklist из `docs/product-experience.md`; если фича требует новый contract/state, обнови соответствующий source of truth и tests тем же change.

Не заявляй о passing tests/benchmarks, если они не запускались. Пока scaffold отсутствует, не выдумывай команды. После появления manifest/config реальная команда проверки должна быть добавлена в `docs/quality.md` в том же change.

Для bugfix — regression case до/вместе с fix, если это разумно. Для boundary changes — contract test обязателен.

## 8. Git и данные

- Не force-push/amend без прямого запроса.
- Не коммить raw organizer data, model weights, secrets, generated caches.
- Не восстанавливать retired scaffold из `1b57aa1`.
- Папка документации — `docs/` в нижнем регистре. Windows/macOS не различают регистр, git и GitHub — различают: новый файл, созданный в папке `Docs`, уедет в репозиторий как `Docs/...` и создаст вторую папку. Перед `git add` проверять `git ls-files | grep '^Docs/'` — должно быть пусто; если нет — `git mv -f Docs/<path> docs/<path>`.
- Перед финалом перечитать полный diff и текущий status.
- Если docs описывают target, а код ещё не существует, называй это **documented target**, не `implemented`.
