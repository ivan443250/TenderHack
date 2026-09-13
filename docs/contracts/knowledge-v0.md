# Contract v0 — `api` (.NET) ↔ `knowledge` (Python)

Статус: **frozen** (ADR-0001). Historical execution-plan language about freezing this boundary in the first hour explains sequencing, not current implementation maturity.

Это нормативная спецификация единственной границы между `api`/`api-worker` и `knowledge`/`knowledge-worker`
(`architecture.md §3, §10`). Машиночитаемый источник для генерации C#-клиента (NSwag) и для реализации
`knowledge` FastAPI-роутов — [`knowledge-v0.openapi.yaml`](knowledge-v0.openapi.yaml) в этой же папке.

Если этот файл и `architecture.md §10` расходятся — расхождение устраняется в этом же изменении;
`architecture.md §10` содержит только иллюстративные формы, этот документ и OpenAPI-файл — точные.

## 1. Правила границы (не переоткрывать без ADR)

1. **Факты/скоры/оценки evidence — да; `Decision`, `HandoffStatus`, `should_handoff`, decision-family
   `reason_codes` — никогда.** `knowledge` ничего не знает про эти типы.
2. **`api` не читает `kb_*`/`quality_*` напрямую.** Единственный путь — HTTP этого контракта.
3. **`knowledge` не вызывает `api` и не читает его таблицы.** Факты о кейсах приходят только push'ем
   (`POST /v0/quality/turns`, `POST /v0/quality/feedback`, `POST /v0/quality/completions`).
4. **Новые поля — только `optional`.** Breaking change версии (`/v1/...`) требует одновременного изменения
   обеих сторон в одном PR/change.
5. **C#-клиент генерируется**, не пишется руками; сгенерированный код не покидает `TenderHack.Infrastructure`.
6. Каждый запрос стадии оркестрации несёт трейс-заголовки (§2). Оба сервиса логируют их как есть.

## 2. Заголовки

| Заголовок | Обязателен для | Формат | Назначение |
|---|---|---|---|
| `X-Trace-Id` | всех запросов, кроме `GET /health` | UUID v4 | сквозная трассировка одного хода/запроса через оба сервиса |
| `X-Case-Id` | всех запросов, привязанных к кейсу (всё, кроме `GET /health`, `GET /v0/snapshots/current`, `GET /v0/sources/{fragment_id}`) | строка (case id) | группировка по кейсу в логах `knowledge` |
| `X-Turn-Id` | стадий оркестрации одного хода: `understand`, `moderation/context`, `retrieve`, `answerability`, `draft`, `verify` | строка (turn id) | привязка стадии к конкретному ходу |

`knowledge` не использует эти заголовки для авторизации или бизнес-логики — только для логирования/трассировки.
Отсутствие обязательного заголовка на стадийном эндпоинте — `400` с `ErrorResponse.code = "MISSING_TRACE_HEADER"`.

## 3. Категории ошибок и `KnowledgeFailure`

`KnowledgeFailure` — тип **на стороне `.NET`** (`TenderHack.Application`), не тело ответа `knowledge`. `Infrastructure`
классифицирует любой исход HTTP-вызова к `knowledge` в одну из категорий и поднимает это как `KnowledgeFailure`
дальше в `TurnOrchestrator`, который обязан привести ход к `TECHNICAL_ERROR` (`architecture.md §16`), а не к
«в базе нет информации».

| `KnowledgeFailure.category` | Когда присваивается |
|---|---|
| `TIMEOUT` | per-stage timeout (`api`-конфиг, `architecture.md §7`) истёк до получения ответа |
| `UNAVAILABLE` | соединение не установлено / connection refused / DNS-failure / `knowledge` вернул `5xx` |
| `INVALID_RESPONSE` | `2xx`, но тело не проходит десериализацию/валидацию по контракту (в т.ч. неожиданный decision-подобный field) |
| `MODEL_ERROR` | `knowledge` вернул явную ошибку модели/инференса — `ErrorResponse.code` из набора §3.1 |

### 3.1. `ErrorResponse` (тело `4xx`/`5xx`, которые `knowledge` формирует сам)

```json
{
  "code": "STRING_ENUM",
  "message": "human-readable, без chain-of-thought",
  "detail": { "...": "опционально, structured" }
}
```

`code` (нормативный список, расширяется только additive):

| `code` | HTTP статус | Смысл |
|---|---|---|
| `VALIDATION_ERROR` | 422 | тело запроса не прошло Pydantic-валидацию |
| `MISSING_TRACE_HEADER` | 400 | обязательный заголовок §2 отсутствует |
| `UNKNOWN_SNAPSHOT` | 404 | `snapshot_id` не существует |
| `UNKNOWN_FRAGMENT` | 404 | `fragment_id` не найден в `GET /v0/sources/{fragment_id}` |
| `UNKNOWN_DOCUMENT` | 404 | `document_id` не входит в текущий нормативный snapshot (`GET /v0/materials/{document_id}/sections`; E3, 2026-09-13) |
| `MODEL_UNAVAILABLE` | 503 | inference runtime (embedder/reranker/generator) не отвечает → маппится в `KnowledgeFailure.UNAVAILABLE` |
| `MODEL_ERROR` | 500 | inference runtime ответил, но генерация/оценка упала (напр. structured output parse failure) → маппится в `KnowledgeFailure.MODEL_ERROR` |
| `INTERNAL_ERROR` | 500 | любая иная необработанная ошибка → `KnowledgeFailure.UNAVAILABLE` |

`.NET` обязан трактовать **любой** незадокументированный `5xx` как `UNAVAILABLE`, а любой не проходящий JSON-схему
`2xx` как `INVALID_RESPONSE` — не падать необработанным исключением.

## 4. Идемпотентность push-эндпоинтов

- `POST /v0/quality/turns` идемпотентен по `(turn_id, revision)`: повторная доставка того же `turn_id`+`revision`
  (retry после потери ack, at-least-once outbox) — upsert без ошибки, ответ `202` в обоих случаях.
- `POST /v0/quality/feedback` идемпотентен по `feedback_id`: повторная доставка — upsert, `202`.
- `POST /v0/quality/completions` идемпотентен по `case_id`: повторная доставка — upsert, `202` (кейс завершается ровно один раз, поэтому ключ — сам `case_id`). `api-worker` отправляет его повторно с обновлённым `resolution_status`, если терминальный факт адаптера пришёл уже после `CLOSED_USER` и перевёл `UNKNOWN → RESOLVED/UNRESOLVED`; `knowledge` берёт последнюю версию.
- `202` означает **«сохранено»**, не «оценено»: оценка запускается асинхронно `knowledge-worker`.
- `knowledge` никогда не отвечает `409` на эти три эндпоинта — по правилам push-модели (`architecture.md §10`)
  `api-worker` всегда отправляет полный, детерминированный по коммиту payload; конфликт содержимого при том же
  ключе означает баг в `api-worker`, а не легитимный конкурентный сценарий, и должен быть обнаружен тестами
  (`quality.md §8`), а не HTTP-статусом.

## 5. Эндпоинты — сводка

Полные схемы — [`knowledge-v0.openapi.yaml`](knowledge-v0.openapi.yaml). Здесь — назначение и владение каждым полем.

| Метод/путь | Стадия оркестратора | Требует `X-Turn-Id` |
|---|---|---|
| `GET /health` | инфраструктурная проверка | нет |
| `POST /v0/understand` | понимание запроса (`architecture.md §5.2`) | да |
| `POST /v0/moderation/context` | контекстная проверка неоднозначной модерации | да |
| `POST /v0/retrieve` | извлечение кандидатов | да |
| `POST /v0/answerability` | оценка достаточности evidence | да |
| `POST /v0/draft` | генерация черновика | да |
| `POST /v0/verify` | проверка claims черновика | да |
| `GET /v0/sources/{fragment_id}` | открытие источника (вне хода, из UI) | нет |
| `GET /v0/materials` | список документов текущего нормативного snapshot (вкладка «Материалы», E3, 2026-09-13) | нет |
| `GET /v0/materials/{document_id}/sections` | оглавление одного документа по `section` фрагментов (E3, 2026-09-13) | нет |
| `GET /v0/snapshots/current` | текущий snapshot базы знаний | нет |
| `POST /v0/quality/turns` | push факта о ходе (`api-worker`) | нет (`turn_id` в теле) |
| `POST /v0/quality/feedback` | push фидбэка: `specialist_rating`, `information_quality_rating`, `solved`, `comment_text` (`api-worker`) | нет (`turn_id` в теле) |
| `POST /v0/quality/completions` | push факта завершения обращения: `completion_reason`, `resolution_status`, `specialist_ref?` (`api-worker`; additive, 2026-09-12) | нет (`case_id` в теле) |
| `GET /v0/quality/evaluations?case_id=` | read-only аналитика (proxy `api`) | нет |
| `GET /v0/quality/issue-groups` | read-only аналитика (proxy `api`) | нет |

### 5.1. Нормативные ограничения полей

- `Decision`, `HandoffStatus`, `should_handoff`, `reason_codes` семейства решений — **запрещённые ключи** в любом
  ответе `knowledge`. Их наличие в теле ответа — `INVALID_RESPONSE` независимо от статус-кода.
- `decision`, `reason_codes`, `handoff_status`, `recommended_line`, `service_need` в теле `POST /v0/quality/turns`,
  а также `completion_reason`, `resolution_status` в `POST /v0/quality/completions` — это **opaque строки**,
  присланные `api`; `knowledge` не парсит и не воспроизводит их семантику (`architecture.md §5.7`).
- `specialist_ref` (в feedback и completions) — opaque идентификатор специалиста от адаптера. `knowledge` может
  группировать по нему (`n`, распределение `specialist_rating`, limitations), но **не публикует** персональный
  рейтинг/ранжирование (`product-spec.md §18.2`). `null` означает «человек не участвовал или адаптер не сообщил».
- `helpful` в `QualityFeedbackPush` — deprecated, дублирует `information_quality_rating`; новые consumers
  читают только два явных рейтинга.
- `model_version`/`retrieval_config_version` в `QualityTurnPush` — additive optional поля (2026-09-12):
  генератор/retrieval config для этого хода, когда `draft`/`retrieve` реально выполнялись; `null` для
  ходов без сгенерированного ответа (`CLARIFY`/`HANDOFF_OFFER`/`TECHNICAL_ERROR`).
- `corpus` в `POST /v0/retrieve` обязателен и по умолчанию не подразумевается — вызывающая сторона (`Application`)
  всегда передаёт `NORMATIVE` явно для user-facing ответа; `HISTORICAL` используется только для аналитики/evals,
  никогда для генерации ответа пользователю (`product-spec.md §6.2`).

## 6. Версионирование

- Путь `/v0/...` фиксирован до первого breaking change.
- Breaking change = удаление/переименование required-поля, смена типа, смена семантики enum-значения.
- Breaking change поднимает путь до `/v1/...`; старый `/v0/...` может быть удалён только после того как
  `api` полностью мигрировал в одном change (`ADR-0001 §3` правило 3).
- Каждый response, где применимо, несёт `model_version` и/или `retrieval_config_version` — они не участвуют
  в версионировании контракта, только в воспроизводимости конкретного ответа (`architecture.md §8`,
  «Knowledge versioning»).

## 7. Deterministic fixture / stub mode

Детерминированный stub/fixture path сохраняется как **тестовый инструмент** для contract tests и `.NET` decision fixtures. Он не описывает текущую зрелость Knowledge и не является product fallback вместо реального retrieval/model path.

Fixture mode обязан отвечать на эндпоинты §5 правдоподобным статическим/детерминированным JSON, соответствующим OpenAPI-схеме, включая:

- фиксированные `fragment_id`/`snapshot_id` для смоук-тестов `api`;
- контролируемые answerability/verify варианты для проверки `ANSWER` / `CLARIFY` / `HANDOFF_OFFER` / `TECHNICAL_ERROR` без зависимости от текущего качества модели;
- честные fixture/stub `model_version` / `retrieval_config_version`, чтобы тестовый результат никогда не выглядел как real-model measurement в логах/демо.

Real retrieval/answerability/draft/verify и fixture path обязаны сохранять один и тот же boundary contract. `evals/decisions` / Application tests не должны дублировать C# decision policy в Python ради удобства.
