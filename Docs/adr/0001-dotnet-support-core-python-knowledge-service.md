# ADR-0001 — .NET support core + Python knowledge/inference service

- **Статус:** accepted (2026-09-12)
- **Заменяет:** решение «Python modular monolith owns the backend» из `stack.md §2` (версия до этого ADR)
- **Затрагивает:** `AGENTS.md`, `architecture.md`, `stack.md`, `quality.md`, `execution-plan.md`, `references.md`, `README.md`

## 1. Контекст

Предыдущая версия `stack.md` выбрала один Python modular monolith (FastAPI + worker) и явно отклонила ASP.NET Core как «дублирование Python-оркестрации/ML-границ». Формальные требования организаторов стек не фиксируют (`hackathon-requirements.md`, раздел про implementation choices).

Изменившийся факт: backend/integration-инженер команды существенно быстрее и увереннее пишет на C#, чем на асинхронном Python-стеке (SQLAlchemy 2 async, asyncpg, Alembic). Роли в `execution-plan.md §2` уже разделяют «Backend/integration» и «ML/data»; физическая граница между runtime совпадает с организационной.

Это **решение**, принятое по людям, а не по измеренной технической проблеме монолита. Реверсия обратно в монолит остаётся допустимым fallback (§8).

## 2. Решение

Backend делится на два runtime с одной PostgreSQL:

| Runtime | Язык | Владеет |
|---|---|---|
| **`api` + `api-worker`** (support core) | C# / .NET 10 LTS, ASP.NET Core, EF Core, Npgsql | Cases, turns/revisions, orchestrator как state machine, финальный `Decision`, deterministic moderation, routing policy, handoff + outbox, feedback, idempotency, HTTP/SSE для браузера, authz, demo mode |
| **`knowledge` + `knowledge-worker`** (knowledge & inference) | Python 3.12, FastAPI, Pydantic v2 | Ingestion/parsing, kb-таблицы и snapshots, embeddings, retrieval (exact + FTS + pgvector + RRF + reranker), answerability assessment, draft generation, claim verification, LLM-проверка неоднозначности модерации, quality evaluator, issue groups, evals |

`api` — единственная публичная HTTP-граница. Браузер никогда не ходит в `knowledge` напрямую; read-only analytics `api` проксирует с проверкой прав.

Старый .NET scaffold из коммита `1b57aa1` (`Ticket/Specialist/operator queue`, `IThresholdProvider`, `Result<T>`) **не восстанавливается**: его доменная модель не соответствует текущему state model (`architecture.md §6`) и содержит церемониальные слои, которые `architecture.md §4` запрещает.

## 3. Правила границы (нормативные)

1. **Оркестратор один и он в .NET.** Последовательность стадий хода, суперсид старых turn'ов, атомарная публикация решения и выбор `Decision` реализуются в C#-коде. Python не знает о `Decision`, `HandoffStatus`, `ResolutionStatus`.
2. **Python возвращает факты, скоры и оценки evidence, а не решения.** Допустимо: `candidates[]`, `scores`, `evidence_sufficiency`, `missing_conditions[]`, `claims[].supported`. Недопустимо: `decision`, `should_handoff`, `reason_codes` из семейства решений.
3. **Контракт `v0` фиксируется в первый час** (`execution-plan.md §3`). Python отдаёт заглушки с правдоподобным JSON; .NET пишется против них. Новые поля добавляются как optional; breaking change контракта требует одновременной правки обоих сервисов в одном change.
4. **DTO не пишутся руками дважды.** Python-сервис отдаёт OpenAPI; C#-клиент генерируется (NSwag). Сгенерированные классы не выходят за пределы `Infrastructure`; `Application` видит только собственные record'ы.
5. **Зависимость строго однонаправленная: `api → knowledge`, и по вызовам, и по данным** (`architecture.md §4.3, §8`). Runtime мигрирует и читает только свои таблицы; общих view и ролей нет. Факты о кейсах, нужные аналитике, `api-worker` **пушит** через outbox (`POST /v0/quality/turns`, `POST /v0/quality/feedback`), `knowledge` хранит их в своей `quality_cases`. Недостающее поле добавляется в payload как optional, а не вычитывается из БД. .NET не читает `kb_*`/`quality_*` — только через API `knowledge`. Следствие: `knowledge` поднимается и тестируется без `api`.
6. **Недоступность `knowledge` — отдельная категория ошибки** (`architecture.md §15`), никогда не «в базе нет информации». Handoff-пакет собирается без участия Python.
7. **Trace сквозной.** `X-Trace-Id`, `X-Case-Id`, `X-Turn-Id` передаются во все вызовы `knowledge` и попадают в логи обоих сервисов.
8. **Evals по решениям (`evals/decisions`) гоняются против .NET через HTTP** с поднятым `knowledge` в stub/fixture-режиме. Не дублировать decision-логику в Python ради удобства тестов.

## 4. Что мы принимаем как цену

| Минус | Значимость | Смягчение |
|---|---|---|
| Граница проходит по самому нестабильному коду (evidence pack, verify result) — каждое изменение = Python + regen + C# | **высокая** в фазе 2–16 ч | правило 3: заморозка `v0`, optional-поля, stubs с первого часа |
| Split brain оркестратора: decision-логика расползается в Python «потому что score уже тут» | **высокая** вероятность, средняя цена | правила 1–2; skeptic review проверяет их явно |
| Аналитике нужны case-данные, которыми владеет .NET | средняя → низкая | правило 5: push-модель через outbox; дублирование текста вопроса/ответа в `quality_cases` принято при масштабе хакатона |
| Двойная тестовая петля; `evals/decisions` требует обоих сервисов | средняя | правило 8; фикстуры `knowledge` в stub-режиме |
| Меньшая legibility для coding agents (два языка, HTTP-контракт между ними) | средняя при агентной разработке | `AGENTS.md §3` называет владельца каждого модуля; контракт задокументирован в одном месте (`architecture.md §10`) |
| Частичные отказы внутри хода (retrieve ок, draft timeout) | низкая-средняя | правило 6; категория ошибки на каждый вызов |
| Две системы миграций/конфигов | низкая при дисциплине | правило 5; общие enum-значения задокументированы в `architecture.md §6` |
| +2 контейнера, порядок старта, health checks | низкая | Compose `depends_on` + healthcheck |

Ожидаемый суммарный overhead: порядка 15–25 % времени на связке backend/ML относительно монолита, компенсируемый скоростью backend-инженера в C#.

## 5. Что не меняется

- Все продуктовые инварианты `AGENTS.md §2` и `product-spec.md`.
- State model, инварианты и идемпотентность (`architecture.md §6–7`).
- Retrieval внутри PostgreSQL, локальный inference, отказ от Redis/Kafka/Qdrant/k8s (`stack.md §15`).
- Frontend-стек.
- Правило «no business decision only in frontend state or prompt prose» — расширяется: и не только в Python-сервисе.

## 6. Условия пересмотра

Вернуться к Python-монолиту (или перенести оркестратор в Python), если до Gate G2 наблюдается любое из:

- контракт `v0` ломался breaking-образом ≥ 3 раз за фазу 8–16 ч;
- decision-логика обнаружена в Python при skeptic review и не была устранена за один цикл;
- backend-инженер выбывает или переключается на ML-часть (граница перестаёт совпадать с ролями).

## 7. Последствия для документации

Синхронно с этим ADR обновлены: `AGENTS.md §3, §4 (self-review/skeptic), §7`, `README.md`, `docs/index.md`, `architecture.md` (переписан целиком: владелец у каждого модуля, §8 владение таблицами, §10 контракт `v0`, §12 два worker'а), `stack.md §1–4, 6, 13–16`, `quality.md §8, §13`, `execution-plan.md §2–5`, `references.md §6`.

## 8. Fallback

Если срабатывает §6: домен и application-слой .NET переносятся в `apps/knowledge` как Python-модули по тем же именам; `kb_*`-таблицы и `knowledge` API уже принадлежат Python, поэтому обратный путь — перенос только case-таблиц и оркестратора. Web-контракт (`api` ↔ браузер) сохраняется. Push-эндпоинты `/v0/quality/*` при этом становятся внутренними вызовами функций — `knowledge` не имел обратной зависимости, поэтому с его стороны переделок нет.
