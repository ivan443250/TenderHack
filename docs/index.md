# Documentation index

`docs/` — system of record для продукта и инженерных решений. Если код и документ расходятся, задача не считается завершенной, пока расхождение не устранено или явно не задокументировано как migration/debt.

## Читать сначала

| Документ | Назначение | Когда обязателен |
|---|---|---|
| [`hackathon-requirements.md`](hackathon-requirements.md) | Формальные функции, ограничения, scoring/defense layer без старой архитектуры | Scope/приоритеты/спор о требованиях |
| [`product-spec.md`](product-spec.md) | Что строим, P0/P1/non-goals, decision/state semantics, knowledge/routing/quality rules | Любое изменение поведения |
| [`architecture.md`](architecture.md) | Модули, boundaries, state, persistence, worker, handoff | Backend/API/data/frontend contracts |
| [`stack.md`](stack.md) | Зафиксированный стек, версии, модели, rejected alternatives | Новые dependencies/runtime/infrastructure |
| [`agent-workflow.md`](agent-workflow.md) | Как coding agents планируют, делегируют, проверяют и ревьюят | Любая существенная агентная работа |
| [`quality.md`](quality.md) | Evals, tests, regression, Definition of Done | Перед завершением implementation task |
| [`execution-plan.md`](execution-plan.md) | Порядок 40-часовой реализации и gates | Планирование/scope decisions |
| [`references.md`](references.md) | Первичные источники и OpenAI guidance | Спор об архитектуре/agent workflow/tech facts |

## Приоритет источников

1. Явные требования организаторов/уточнения экспертов.
2. Реальные предоставленные данные и инструкции.
3. `hackathon-requirements.md` — сохраненная командная фиксация стабильного формального слоя.
4. `product-spec.md` — принятая командная политика для неоднозначных мест.
5. `architecture.md` и `stack.md` — принятая инженерная реализация политики.
6. Остальные документы/комментарии/код.

Если новое официальное требование противоречит docs, **не защищать старый документ**: обновить решение, записать причину и синхронно изменить зависимые документы/код.

## Что является фактом, решением и гипотезой

Во всех документах придерживаться терминов:

- **Факт** — подтвержден данными, официальным требованием, исходным документом или наблюдаемым runtime behavior.
- **Решение** — выбранная командой политика/архитектура.
- **Цель** — критерий будущей проверки, не уже достигнутый результат.
- **Гипотеза** — объяснение, которое еще нужно доказать.

Не превращать цель, план или model confidence в факт.

## Документационная дисциплина для agents

OpenAI agent-first guidance используется буквально: root `AGENTS.md` — карта, а не гигантский мануал; глубокий контекст живет здесь и открывается по необходимости.

Если задача меняет:

- формальное понимание scope/constraints → обновить `hackathon-requirements.md`;
- продуктовую семантику → обновить `product-spec.md`;
- модуль/boundary/state → `architecture.md`;
- библиотеку/model/runtime → `stack.md`;
- тестовый gate/метрику → `quality.md`;
- порядок исполнения/critical path → `execution-plan.md`.

## Execution plans для крупных задач

После появления активной разработки сложные изменения должны иметь короткий versioned plan. Рекомендуемая структура:

```text
docs/plans/
  active/
  completed/
```

Не создавать планы ради каждой мелкой правки. План нужен, если работа:

- затрагивает несколько модулей;
- требует миграции/смены контракта;
- идет параллельно несколькими agents;
- содержит существенные неизвестные;
- длится больше одной нормальной coding-сессии.

Plan содержит: цель, acceptance criteria, affected boundaries, риски, последовательность, delegated work, progress, decisions, verification и rollback/fallback.
