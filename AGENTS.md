# AGENTS.md

Этот файл — **карта**, а не энциклопедия. Подробные продуктовые и технические решения живут в `docs/` и являются source of truth.

## 1. Перед любой нетривиальной задачей

1. Прочитай этот файл.
2. Прочитай `docs/index.md`.
3. Открой только релевантные документы из `docs/`.
4. Проверь текущий `git status`/diff и не затирай чужие изменения.
5. Сформулируй acceptance criteria задачи до написания кода.
6. Если задача сложная или затрагивает несколько независимых областей — создай/обнови execution plan по правилам `docs/agent-workflow.md`.

## 2. Неизменные продуктовые инварианты

- Это локальная система поддержки Портала поставщиков, а не универсальный helpdesk.
- Runtime продукта — **один управляемый orchestrator/state machine**, не swarm автономных product agents.
- Coding subagents разрешены и поощряются для ускорения разработки; это не меняет runtime-архитектуру продукта.
- Внешние AI API в штатном интеллектуальном контуре запрещены.
- Нормативный корпус ответов и исторический аналитический корпус разделены.
- Историческое поле `Решение` не является автоматически разрешенным ответом новому пользователю.
- Найденный похожий текст не равен доказанному ответу.
- `ANSWER` не означает `RESOLVED`.
- Подготовленная передача не означает принятую передачу.
- Не выдумывай Portal state, SLA, контакты, line mappings или данные интеграций, которых нет.
- Не выводи внутренний chain-of-thought пользователю, в лог или UI.
- При нехватке подтверждений предпочитай `CLARIFY`/`HANDOFF_OFFER`, а не правдоподобную галлюцинацию.

Подробности: `docs/product-spec.md`.

## 3. Архитектурная граница

Целевой MVP — два backend runtime с одной PostgreSQL (`docs/adr/0001-dotnet-support-core-python-knowledge-service.md`):

- **`api` / `api-worker` — .NET support core** (C#, clean architecture без церемониальных слоёв): cases, turns, orchestrator/state machine, финальный `Decision`, deterministic moderation, routing, handoff + outbox, feedback, HTTP/SSE для браузера, authz. Единственная публичная граница.
- **`knowledge` / `knowledge-worker` — Python knowledge & inference service**: ingestion, `kb_*`, retrieval, answerability assessment, draft/verify, LLM-проверка неоднозначности модерации, quality analytics, evals. Только внутренняя сеть.
- React frontend ходит только в `api`.

Правила границы, которые нельзя нарушать без нового ADR:

- оркестратор один и он в .NET; Python возвращает факты/скоры/оценки evidence, никогда `Decision`, `HandoffStatus`, `should_handoff`;
- контракт `api → knowledge` — `v0`, OpenAPI из FastAPI, C#-клиент генерируется, не пишется руками; новые поля optional;
- зависимость только `api → knowledge`, и по HTTP, и по данным: общих таблиц/view/ролей нет; факты для аналитики `api-worker` пушит в `knowledge` (`/v0/quality/*`), `api` не читает `kb_*`/`quality_*`;
- недоступность `knowledge` — отдельная категория ошибки, не «в базе нет информации».

Старый .NET scaffold из коммита `1b57aa1` не восстанавливать — его модель не соответствует state model. Не вводить дополнительные сервисы без измеренной причины.

Ключевые зависимости и rejected alternatives: `docs/stack.md`.

Модули, состояния и data boundaries: `docs/architecture.md`.

## 4. Обязательный рабочий цикл агента

Для любой существенной задачи используй цикл:

```text
DISCOVER
→ PLAN
→ DELEGATE independent work if useful
→ IMPLEMENT
→ SELF-REVIEW
→ SKEPTIC REVIEW
→ FIX
→ VERIFY
→ DOC SYNC
→ FINAL STATUS
```

### DISCOVER

- Проследи реальный flow до файлов, которые будут изменены.
- Не строй решение на названии функции/комментарии, если можно проверить код или данные.
- Явно разделяй факт, принятое проектное решение и гипотезу.

### PLAN

План должен содержать:

- цель;
- затрагиваемые модули;
- acceptance criteria;
- риски/неизвестные;
- проверки после изменения.

Не пиши большой план для однострочной обратимой правки.

### DELEGATE

Если harness предоставляет subagents/collaboration tools, делегируй независимые ветки, когда это экономит время или повышает качество.

Root-agent всегда остается manager и отвечает за финальную интеграцию.

Хорошие задачи для subagents:

- repo/data reconnaissance;
- поиск несоответствия спецификации;
- независимое исследование библиотеки/контракта;
- реализация непересекающихся модулей;
- review тестов;
- security/data-leak review;
- skeptic review готового diff.

Не делегировать двум агентам одновременное редактирование одних и тех же файлов. По умолчанию depth delegation = 1; рекурсивная делегация только при явной пользе.

### IMPLEMENT

- Минимальный change set, который полностью закрывает acceptance criteria.
- Сначала корректность и наблюдаемое поведение, затем abstraction/polish.
- Не добавляй dependency, service, agent, queue, ANN-index или cache «на будущее» без реальной необходимости.
- Парси/валидируй данные на boundary; не опирайся на guessed shapes.
- Ошибки должны приводить к честному состоянию, а не к ложному success.

### SELF-REVIEW

Перед внешним review root-agent обязан сам перечитать diff как reviewer:

1. Соответствует ли изменение задаче и `docs/product-spec.md`?
2. Не добавлено ли скрытое новое требование/поведение?
3. Не нарушены ли состояния и idempotency?
4. Не появилась ли возможность галлюцинации/подмены provenance?
5. Не смешаны ли normative knowledge и historical analytics?
6. Не появились ли secrets/PII/raw private data в логах/fixtures?
7. Обработаны ли failure/timeout/empty/unknown paths?
8. Документация все еще описывает реальный код?
9. Не появилась ли decision-логика в `knowledge` или прямое чтение чужих таблиц (правила `§3`)?

### SKEPTIC REVIEW

Для нетривиального изменения после self-review запусти независимого reviewer-subagent, если инструмент доступен.

Skeptic получает: исходную задачу, acceptance criteria, релевантные docs и diff. Его инструкция: **предположить, что решение ошибочно, и найти конкретные способы его сломать**.

Он проверяет минимум:

- spec drift;
- неподтвержденные предположения;
- неправильные boundary/state transitions;
- data leakage / prompt injection / unsafe rendering;
- утечка `Decision`/handoff-логики в `knowledge` или обход контракта `v0`;
- race/idempotency/retry проблемы;
- ложные success states;
- недостающие edge cases;
- тесты, которые лишь повторяют реализацию;
- переусложнение и скрытую инфраструктурную стоимость.

Ответ skeptic: список findings с severity `critical/high/medium/low`, доказательством и предлагаемой проверкой. Не проси skeptic переписывать всю реализацию.

Root-agent обязан исправить findings либо явно зафиксировать, почему finding неприменим. Затем повторить targeted checks. Максимум два обычных review-цикла; продолжать дальше только если остаются critical/high проблемы.

### VERIFY

Проверки должны быть соразмерны изменению.

- Сначала самые узкие тесты/линты, которые доказывают измененный контракт.
- После их успеха расширяй проверки только если затронут shared boundary, migration, state machine, retrieval, security или есть нерешенные риски.
- Не создавай бессмысленные тесты, зеркально повторяющие implementation.
- Для bugfix сначала добавь воспроизводящий regression case, если это разумно.
- Не заявляй, что тесты прошли, если они не запускались.

Команды становятся обязательными только после появления соответствующих manifest/config files. Актуальные команды должны быть записаны здесь или в `docs/quality.md` сразу после scaffold.

### DOC SYNC

Если изменился architecture boundary, state, API contract, chosen dependency, evaluation policy или product behavior — обнови соответствующий файл `docs/` в том же change.

Не оставляй `TODO` в source-of-truth документации без owner/условия удаления.

## 5. Правила git

- Работай в текущей ветке, если пользователь явно не попросил другую.
- Не переписывай чужую историю, не force-push без прямого запроса.
- Не делай amend существующих commits.
- Перед завершением проверь diff/status.
- Если пользователь просит один commit — все связанные изменения должны попасть в один атомарный commit.
- Не коммить raw hackathon data, model weights, secrets или generated caches.

## 6. Качество кода

- Предпочитай явные typed contracts и маленькое число хорошо очерченных модулей.
- Не создавай abstraction только ради «clean architecture».
- I/O boundaries должны быть валидируемыми и тестируемыми.
- Логи — structured и без chain-of-thought/секретов.
- User-facing error отличается от `knowledge not found`; infrastructure failure нельзя выдавать за отсутствие ответа в БЗ.
- Все state-changing operations проектировать с idempotency/retry semantics.

## 7. Порядок чтения docs по типу задачи

| Задача | Читать |
|---|---|
| Любая продуктовая логика | `docs/product-spec.md` |
| Backend/state/API (.NET `api`) | `docs/architecture.md`, `docs/product-spec.md` |
| Knowledge service / contract `v0` | `docs/architecture.md §10`, `docs/adr/0001-*.md` |
| Dependency/runtime/model | `docs/stack.md`, `docs/references.md` |
| Смена границы api/knowledge | `docs/adr/` — новый ADR обязателен |
| Retrieval/RAG/evals | `docs/product-spec.md`, `docs/quality.md`, `docs/stack.md` |
| Frontend/UX | `docs/product-spec.md`, `docs/architecture.md` |
| Agentic dev workflow/review | `docs/agent-workflow.md` |
| Планирование хакатона | `docs/execution-plan.md` |
| Tests/benchmark/DoD | `docs/quality.md` |

## 8. Источники OpenAI guidance

Repository workflow намеренно следует актуальному OpenAI agent-first подходу: короткий `AGENTS.md` как map, versioned repository docs как system of record, планы как first-class artifacts, manager-style delegation, self-review + независимые agent reviews и соразмерное testing/verification.

Ссылки и точный контекст: `docs/references.md`.
