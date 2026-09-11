# TenderHack — первая линия поддержки Портала поставщиков

RAG-бот поверх структурированной базы знаний с явным отказом от ответа, модерацией и маршрутизацией обращений, аналитикой отзывов. Полное ТЗ — [`Docs/HackDocs/tz-tenderhack.md`](Docs/HackDocs/tz-tenderhack.md), план по этапам — [`Docs/HackDocs/implementation-plan.md`](Docs/HackDocs/implementation-plan.md).

## Сервисы

| Сервис | Стек | Ответственность |
|---|---|---|
| `web` | React + Vite + TS | Чат, кабинет оператора, дашборд, BPMN |
| `api` | ASP.NET Core (net9.0) | Домен, пайплайн, реальное время, аналитика |
| `ml` | FastAPI | Поиск, триаж, генерация, кластеризация |
| `db` | PostgreSQL 16 + pgvector | Домен + база знаний |
| `ingest` | Python, разовый job | Индексация базы знаний |

`ml` никогда не вызывает `api`; решение об эскалации принимает `api` по `no_answer`/`confidence` из ответа `ml`.

## Слои `api`

```
TenderHack.sln
├── src/
│   ├── TenderHack.Domain          — без зависимостей вообще
│   ├── TenderHack.Application     — → Domain (+ DI.Abstractions)
│   ├── TenderHack.Infrastructure  — → Application (EF Core, Npgsql, Polly, SignalR-хосты — нет)
│   └── TenderHack.Web             — → Application, Infrastructure (Minimal API, SignalR, Serilog)
└── tests/
    └── TenderHack.UnitTests       — → Domain, Application
```

**Правило зависимостей:** `Domain.csproj` не содержит ни одного `PackageReference`; `Application.csproj` не содержит EF Core и `Microsoft.AspNetCore.*`. `ml` не имеет доступа к доменным таблицам (`tickets`, `messages`, ...) — только к `kb_*`, которые не мапятся EF.

## Запуск

```bash
docker compose up --build
```

Поднимает `db` (pgvector) и `api` (порт 8080, Swagger на `/swagger` в Development). `ml`/`ingest`/`web` — закомментированы в `docker-compose.yml` до появления соответствующих директорий.

Локально без Docker:

```bash
dotnet build
dotnet test tests/TenderHack.UnitTests
dotnet run --project src/TenderHack.Web
```

## Обоснование выбора моделей (раздел 4 ТЗ)

- **Поиск** — гибрид BM25 (Postgres FTS, русский словарь) + pgvector, слияние через RRF; отдельная векторная БД не нужна для нескольких тысяч чанков.
- **Эмбеддинги** — `intfloat/multilingual-e5-small` (~118M) — баланс качества и скорости для русского языка; `e5-large` сознательно не взят как избыточно тяжёлый для задачи.
- **Опечатки** — SymSpell поверх словаря из самой базы знаний (эмбеддинги прощают опечатки лишь частично, а требование в ТЗ явное).
- **Генерация** — llama.cpp, 1.5–3B Q4_K_M, строгий RAG-промпт; экстрактивный фолбэк, если генерация нестабильна на демо.
- **Модерация мата** — нормализатор обфускации + словарь + `rubert-tiny-toxicity` (29M) как второй сигнал; ни один из двух сигналов отдельно не работает надёжно.
- **Линия поддержки** — логрегрессия поверх тех же e5-эмбеддингов — обучается за секунды, объясняется жюри одной фразой, не нагружает инференс.

Общий принцип: везде легковесные модели с высоким инференсом на CPU, ничего не сравнимого с внешними LLM-API (что и запрещено ограничениями раздела 2.3 ТЗ).

## Деградация при отказе `ml`

`MlServiceClient` (Infrastructure) никогда не выбрасывает исключение наружу — любая ошибка HTTP-вызова превращается в безопасный дефолт (`no_answer: true`, `is_profane: false`). Погашенный `ml`-контейнер выглядит для пользователя как обычная эскалация к оператору, а не как сбой интерфейса.
