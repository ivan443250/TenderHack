# Documentation index

`docs/` — system of record для продукта и инженерных решений. Если код и документ расходятся, задача не считается завершенной, пока расхождение не устранено или явно не задокументировано как migration/debt.

## Читать сначала

| Документ | Назначение | Когда обязателен |
|---|---|---|
| [`hackathon-requirements.md`](hackathon-requirements.md) | Формальные функции, ограничения, scoring/defense layer без старой архитектуры | Scope/приоритеты/спор о требованиях |
| [`product-spec.md`](product-spec.md) | Что строим, P0/P1/non-goals, decision/state semantics, knowledge/routing/quality rules | Любое изменение поведения |
| [`architecture.md`](architecture.md) | Модули, boundaries, state, persistence, worker, handoff | Backend/API/data/frontend boundaries |
| [`stack.md`](stack.md) | Зафиксированный стек, версии, модели, rejected alternatives | Новые dependencies/runtime/infrastructure |
| [`adr/`](adr/) | Architecture decision records; ADR-0001 — граница .NET `api` / Python `knowledge` | Смена ownership/runtime/service/queue/DB/state dimension |
| [`contracts/`](contracts/) | Нормативные интерфейсы между независимо разрабатываемыми блоками | Любая работа на shared boundary |
| [`open-decisions.md`](open-decisions.md) | Нерешённые policy/contract gaps, которые нельзя выбирать молча | Перед реализацией затронутой неоднозначной области |
| [`workstreams.md`](workstreams.md) | Владение блоками, allowed scope и правила параллельной разработки | Деление работы между людьми/agents |
| [`agent-workflow.md`](agent-workflow.md) | Manager/subagent workflow, self-check и skeptic review | Любая существенная agentic работа |
| [`quality.md`](quality.md) | Evals, tests, regression, Definition of Done | Перед завершением implementation task |
| [`execution-plan.md`](execution-plan.md) | Порядок 40-часовой реализации и gates | Планирование/scope decisions |
| [`references.md`](references.md) | Первичные источники и OpenAI guidance | Спор об архитектуре/agent workflow/tech facts |

## Контракты

Начинать с [`contracts/README.md`](contracts/README.md).

Текущие shared boundaries:

- [`contracts/knowledge-v0.md`](contracts/knowledge-v0.md) + [`contracts/knowledge-v0.openapi.yaml`](contracts/knowledge-v0.openapi.yaml) — frozen `.NET api ↔ Python knowledge`;
- [`contracts/web-api-v0.md`](contracts/web-api-v0.md) — browser ↔ `.NET api`, server-driven chat timeline/SSE/handoff/feedback semantics;
- [`contracts/support-adapter-v0.md`](contracts/support-adapter-v0.md) — `.NET Application ↔ external/demo support adapter`.

## Приоритет источников

1. Явные требования организаторов/уточнения экспертов.
2. Явные текущие решения команды/пользователя, если они не противоречат п.1.
3. Реальные предоставленные данные и инструкции.
4. `hackathon-requirements.md` — сохраненная фиксация формального слоя.
5. `product-spec.md` — принятая продуктовая политика для неоднозначных мест.
6. `architecture.md`, `stack.md`, ADR и frozen contracts — инженерная реализация политики.
7. Остальные документы/комментарии/код.

Если новое официальное требование противоречит docs, не защищать старый документ: обновить решение, причину и зависимые документы/код. Если конфликт ещё не разрешён — записать его в `open-decisions.md` и не выбирать вариант скрыто.

## Что является фактом, решением и гипотезой

- **Факт** — подтвержден данными, официальным требованием, исходным документом или наблюдаемым runtime behavior.
- **Решение** — выбранная командой политика/архитектура.
- **Цель** — критерий будущей проверки, не уже достигнутый результат.
- **Гипотеза** — объяснение, которое еще нужно доказать.

Не превращать target architecture, план или model confidence в факт реализации.

## Документационная дисциплина

Если задача меняет:

- формальный scope/constraints → `hackathon-requirements.md`;
- product behavior/state policy → `product-spec.md`;
- module ownership/data/state boundary → `architecture.md`;
- shared transport/interface → соответствующий файл `contracts/` + обе стороны + contract tests;
- runtime ownership/new service/queue/DB/state dimension → новый ADR;
- dependency/model/runtime → `stack.md`;
- test gate/metric → `quality.md`;
- critical path → `execution-plan.md`;
- параллельное владение блоками → `workstreams.md`;
- нерешённый конфликт → `open-decisions.md`.

## Execution plans для крупных задач

После появления активной разработки сложные изменения должны иметь короткий versioned plan:

```text
docs/plans/
  active/
  completed/
```

План нужен, если работа затрагивает несколько модулей, меняет contract/state/data model, делится между несколькими agents, содержит benchmark/существенные unknowns или занимает больше одной coding-сессии.

Plan содержит: goal, non-goals, acceptance criteria, relevant docs/contracts, risks, workstreams, implementation steps, verification, decisions/progress/open issues.
