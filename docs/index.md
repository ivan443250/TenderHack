# Documentation index

`docs/` — system of record для продуктовых и инженерных решений. Если код и активная документация расходятся, задача не считается завершённой, пока расхождение не устранено или явно не отмечено как documented target / known debt.

Важно различать **активную документацию**, **исторические ADR/plans/benchmarks** и **фактическую реализацию**. Исторический документ сохраняет контекст решения, но не переопределяет более новый source of truth.

## Читать сначала

| Документ | Назначение | Когда обязателен |
|---|---|---|
| [`hackathon-requirements.md`](hackathon-requirements.md) | Формальные функции, ограничения, scoring/defense layer без привязки к старой архитектуре | Scope/приоритеты/спор о требованиях |
| [`product-spec.md`](product-spec.md) | Core product behavior, P0/P1/non-goals, decision/state semantics, knowledge/routing/quality rules | Любое изменение core-поведения |
| [`product-experience.md`](product-experience.md) | Принятый вектор продуктовой доработки: 7 enhancement-фич, UX-поведение, ограничения и acceptance | User-facing UX, recovery, context, analytics improvements |
| [`architecture.md`](architecture.md) | Модули, boundaries, state, persistence, workers, handoff | Backend/API/data/frontend boundaries |
| [`stack.md`](stack.md) | Зафиксированный стек, модели, runtime gates, rejected alternatives | Dependencies/runtime/infrastructure/model choices |
| [`adr/`](adr/) | История и обоснование принятых архитектурных решений | Смена ownership/runtime/service/queue/DB/state/model-runtime policy |
| [`contracts/`](contracts/) | Нормативные интерфейсы между независимо разрабатываемыми блоками | Любая работа на shared boundary |
| [`open-decisions.md`](open-decisions.md) | Реально открытые внешние/policy gaps + компактный register закрытых решений | Перед реализацией затронутой неоднозначной области |
| [`workstreams.md`](workstreams.md) | Владение блоками, allowed scope и правила параллельной разработки | Деление работы между людьми/agents |
| [`agent-workflow.md`](agent-workflow.md) | Manager/subagent workflow, self-check и skeptic review | Любая существенная agentic работа |
| [`quality.md`](quality.md) | Evals, tests, regression, Definition of Done | Перед завершением implementation task |
| [`execution-plan.md`](execution-plan.md) | 40-часовой critical-path reference и gates; не current progress tracker | Планирование оставшегося scope / восстановление критического пути |
| [`references.md`](references.md) | Внешние первичные источники: OpenAI guidance, модели, PostgreSQL/retrieval/parsing | Проверка внешнего обоснования/версий |

## Контракты

Начинать с [`contracts/README.md`](contracts/README.md).

Текущие shared boundaries:

- [`contracts/knowledge-v0.md`](contracts/knowledge-v0.md) + [`contracts/knowledge-v0.openapi.yaml`](contracts/knowledge-v0.openapi.yaml) — frozen `.NET api ↔ Python knowledge`;
- [`contracts/web-api-v0.md`](contracts/web-api-v0.md) — browser ↔ `.NET api`;
- [`contracts/support-adapter-v0.md`](contracts/support-adapter-v0.md) — `.NET Application ↔ external/demo support adapter`.

Phase labels вроде `G0/G1/G2` в старых формулировках объясняют порядок первоначальной реализации, но не означают, что контракт снова «ждёт scaffold».

## Приоритет источников

1. Явные требования организаторов/уточнения экспертов.
2. Явные текущие решения команды/пользователя, если они не противоречат п.1.
3. Реальные предоставленные данные и инструкции.
4. `hackathon-requirements.md` — сохранённый формальный слой.
5. `product-spec.md` — core product policy.
6. `product-experience.md` — product enhancement policy; не переопределяет core state/decision semantics молча.
7. `architecture.md`, `stack.md`, ADR и frozen contracts — инженерная реализация политики.
8. Остальные активные документы.
9. Исторические plans/benchmarks/старые комментарии — только как provenance, если не подтверждены текущим source of truth.

Если новое официальное требование противоречит docs, не защищать старый документ: обновить решение, причину и зависимые документы/код. Если конфликт ещё не разрешён — записать его в `open-decisions.md` и не выбирать вариант скрыто.

## Что является фактом, решением и гипотезой

- **Факт** — подтверждён текущим кодом/тестом/данными, официальным требованием или исходным документом.
- **Решение** — выбранная командой политика/архитектура.
- **Цель / target** — критерий будущей проверки, не уже достигнутый результат.
- **Гипотеза** — объяснение/выбор, который ещё нужно доказать benchmark/данными.
- **Исторический факт** — был истинным для конкретного run/commit и не переносится автоматически на текущий stack.

Не превращать target architecture, plan, выбранный model artifact, старый benchmark или product enhancement в факт текущей реализации.

## Документационная дисциплина

Если задача меняет:

- формальный scope/constraints → `hackathon-requirements.md`;
- core product behavior/state policy → `product-spec.md`;
- product UX/differentiation/recovery/context/analytics enhancement → `product-experience.md`, а при изменении core behavior также `product-spec.md`;
- module ownership/data/state boundary → `architecture.md`;
- shared transport/interface → соответствующий файл `contracts/` + обе стороны + contract tests;
- runtime ownership/new service/queue/DB/state dimension → новый ADR;
- dependency/model/runtime → `stack.md` + ADR, если меняется принятая model/runtime policy;
- test gate/metric → `quality.md`;
- critical path → `execution-plan.md` или active plan;
- параллельное владение блоками → `workstreams.md`;
- нерешённый конфликт → `open-decisions.md`.

README внутри runtime-папки должен описывать **текущий модуль**, а не вечное состояние «scaffold». Если реализация неполна, перечислять конкретный gap, а не объявлять весь модуль несуществующим.

## Execution plans

```text
docs/plans/
  active/       # только незавершённая работа
  completed/    # завершённые планы, сохраняются как provenance
```

План нужен, если работа затрагивает несколько модулей, меняет contract/state/data model, делится между несколькими agents, содержит benchmark/существенные unknowns или занимает больше одной coding-сессии.

Plan содержит: goal, non-goals, acceptance criteria, relevant docs/contracts, risks, workstreams, implementation steps, verification, decisions/progress/open issues.

Когда цель плана достигнута или план superseded, он должен быть помечен и перенесён из `active/` в `completed/` тем же документационным change. Не оставлять завершённый foundation-plan активным: агенты воспринимают `active/` как текущую работу.

## Проверка drift перед крупным change

Перед существенной работой агент должен быстро проверить:

- нет ли в активных docs формулировок `scaffold/not implemented/TBD`, которые уже опровергаются текущим кодом;
- не ссылается ли документ на несуществующий файл/path;
- не выдаётся ли historical benchmark старого model stack за текущий;
- совпадают ли P0/P1/product-enhancement приоритеты;
- не расходятся ли список quality push endpoints (`turns/feedback/completions`), state enums и ownership между architecture/contracts/workstreams;
- не находится ли завершённый plan в `plans/active`.
