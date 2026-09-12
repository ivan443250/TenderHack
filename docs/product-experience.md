# Product experience — accepted enhancement direction

Этот документ фиксирует **принятый вектор продуктовой доработки** поверх `product-spec.md`. Он не заменяет core P0, state machine, safety/answerability rules и frozen contracts. Если здесь возникает конфликт с `product-spec.md`, формальными требованиями или контрактами — сначала исправить конфликт в source of truth, а не реализовывать скрытое исключение.

Цель слоя — не добавить «ещё AI», а превратить уже сильный технический контур в заметно более полезный продукт: меньше работы для пользователя и специалиста, больше прозрачности, более быстрый recovery и замкнутый цикл улучшения знаний.

## 1. Продуктовая рамка

### Для кого

1. **Пользователь Портала** — не должен знать внутреннюю терминологию, повторно сообщать известный Порталу контекст или превращать длинный ответ в план действий вручную.
2. **Специалист поддержки** — должен получать ready-to-solve handoff, а не перечитывать диалог и заново собирать контекст.
3. **Владелец поддержки / функционального развития** — должен видеть не только отдельные обращения, но и повторяющиеся симптомы, пробелы знаний и причины лишних handoff.

### Главный продуктовый принцип

```text
trusted context
→ grounded answer
→ actionable presentation
→ recovery when answer does not fit
→ prepared handoff when human is needed
→ analytics learns where product/knowledge fails
```

Новая функция оправдана только если она хотя бы одно из:

- уменьшает усилие пользователя;
- уменьшает работу специалиста;
- повышает понятность/доверие без скрытия неопределённости;
- повышает долю корректно завершённых self-service сценариев;
- быстрее выявляет knowledge/process/product gap.

«Выглядит AI-подобно» не является причиной добавлять функцию.

## 2. Приоритет и scope

Core P0 из `product-spec.md` остаётся обязательным. Эти улучшения входят в **product enhancement track** и добавляются только поверх стабильных core-веток.

| # | Фича | Приоритет | Зависимость |
|---|---|---|---|
| 1 | Portal Context Passport | P0+ conditional | trusted Portal/page context или явно помеченный demo-context |
| 2 | Interactive Resolution Plan | P0+ | проверенная procedure/condition структура и source anchors |
| 3 | Answer Applicability Card | P0+ | answerability/applicability facts |
| 4 | Smart Recovery | P0+ | сохранённый turn/evidence/context + orchestrator clarification path |
| 5 | Emerging Issue Detector | P1, high demo value | достаточный поток case analytics / issue groups |
| 6 | Knowledge Gap Radar | P1, high business value | причины abstain/handoff + quality/history data |
| 7 | Presentation Controls: проще / короче / пошагово | P0+, low cost | уже проверенный answer/evidence |

`P0+` означает: высокая продуктовая ценность, но функция **не имеет права задержать или сломать обязательный P0**. Перед feature freeze команда выбирает только те элементы, для которых есть реальные данные/контракт и которые проходят acceptance ниже.

## 3. Feature 1 — Portal Context Passport

### Проблема

Пользователь часто обращается из конкретного процесса Портала, но поддержка вынуждает его заново объяснять роль, раздел, документ, статус и действие. Это увеличивает clarifications и ухудшает routing/retrieval.

### Поведение

Перед первым AI-ответом система может сформировать компактный блок контекста:

```text
Контекст обращения
Роль: поставщик
Процесс: исполнение контракта
Документ: УПД
Статус: На подписании
```

Разрешённые источники поля:

- `trusted_portal_context` — факт, переданный доверенным интеграционным контекстом;
- `user_explicit` — пользователь явно сообщил/исправил значение;
- `inferred` — внутренний кандидат для reasoning, **не показывать как подтверждённый факт**;
- `unknown` — не подменять заглушкой.

Правила:

1. Не спрашивать у пользователя то, что уже пришло как `trusted_portal_context`.
2. Пользователь может исправить отображаемое значение; после исправления provenance становится `user_explicit`, а не «исправленный trusted fact».
3. Существенные поля Passport используются retrieval/answerability/routing только с сохранённым provenance.
4. Handoff package переносит тот же контекст и provenance, чтобы специалист не собирал его заново.
5. Если реальной интеграции с Порталом нет, demo-context должен быть явно помечен как simulated/demo. Нельзя выдавать fixture за реальный Portal state.

### Acceptance

- отсутствие известного контекста не блокирует core flow;
- trusted поле не запрашивается повторно;
- inferred поле не отображается как verified;
- исправление пользователя влияет на следующий turn;
- handoff показывает, что было известно и откуда.

### Продуктовые метрики

- среднее число clarifications до решения;
- доля handoff с заполненными ключевыми context fields;
- time-to-first-useful-answer.

## 4. Feature 2 — Interactive Resolution Plan

### Проблема

Даже корректный длинный ответ заставляет пользователя самому преобразовывать текст инструкции в последовательность действий и отслеживать условия.

### Поведение

Если evidence содержит последовательную процедуру, UI показывает её как структурированный plan:

```text
Как решить
1. Проверьте условие ...
2. Откройте ...
3. Выберите ...
4. Подпишите ...
```

Каждый критичный шаг должен быть связан с тем же verified evidence/source, что и ответ.

Различать:

- **instruction step** — что нужно сделать;
- **condition** — что должно быть верно перед шагом;
- **observed completion** — подтверждено пользователем или trusted Portal context;
- **unknown** — не отмечать выполненным.

Правила:

1. Не превращать procedure в интерактивный plan, если порядок/условия не подтверждены источником.
2. Не ставить `✓` только потому, что AI «думает», что шаг выполнен.
3. High-risk действие остаётся инструкцией/условием; MVP не выполняет его автоматически.
4. Если procedure неполная — обычный grounded answer лучше искусственного checklist.
5. Источник должен открываться на релевантном fragment/page.

### Acceptance

- изменение порядка шагов не допускается без source support;
- неизвестное условие вызывает clarification, а не зелёный статус;
- каждый существенный шаг трассируется к evidence;
- plan деградирует в обычный ответ без потери core flow.

## 5. Feature 3 — Answer Applicability Card

### Проблема

Кнопка «Источник» доказывает происхождение текста, но не объясняет пользователю, **почему именно эта инструкция подходит его ситуации**.

### Поведение

Рядом с grounded answer отображать компактный блок:

```text
Почему этот ответ подходит
✓ Роль: поставщик
✓ Документ: УПД
✓ Этап: исполнение контракта
Источник: Инструкция ..., стр. ...
```

Если существенное условие неизвестно:

```text
Нужно уточнить
Статус документа влияет на дальнейшие действия.
```

Правила:

1. Card строится из тех же applicability facts, которые прошли Answerability Gate.
2. Не показывать «AI confidence 87%» или аналогичный псевдопроцент.
3. `inferred` condition нельзя рисовать зелёным verified check; его либо уточнить, либо явно обозначить как предположение, если это действительно нужно UX.
4. При неполной applicability система не должна визуально заявлять «ответ точно подходит».
5. Card не меняет `Decision`; она объясняет уже принятое решение наблюдаемыми фактами.

### Acceptance

- условия card совпадают с фактическими answerability inputs;
- неизвестное/непроверенное не выглядит подтверждённым;
- источник резолвится;
- card можно скрыть без изменения результата turn.

## 6. Feature 4 — Smart Recovery

### Проблема

Типичный chatbot после неподходящего ответа заставляет пользователя либо повторить вопрос, либо сразу уйти к оператору. Система уже знает предыдущий evidence/context и должна использовать его для следующего полезного шага.

### Поведение

После `ANSWER`/`ANSWER_AND_HANDOFF` доступны действия:

```text
[Помогло]
[Не совпадает с моей ситуацией]
[Нужен специалист]
```

#### «Помогло»

- фиксирует положительный in-turn product signal;
- **не устанавливает `RESOLVED` автоматически**;
- может предложить существующий explicit completion flow («Завершить обращение?»).

#### «Не совпадает с моей ситуацией»

1. Создать новый recovery turn/event, не переписывая историю прошлого ответа.
2. Использовать сохранённые context/evidence/conditions прошлого turn.
3. Найти **одно** наиболее вероятное неизвестное/несовпавшее существенное условие.
4. Задать один targeted clarification либо выполнить одно разрешённое дополнительное расширение retrieval.
5. Снова пройти обычный Answerability Gate; нельзя «договорить» ответ вне core pipeline.
6. Соблюдать существующие лимиты clarifications/search; после исчерпания — handoff.

#### «Нужен специалист»

- прямой переход в существующий `HANDOFF_OFFER`; никакой forced FAQ loop.

Правила:

- recovery не превращается в бесконечный auto-retry;
- прошлый verified answer остаётся в timeline;
- новое пользовательское уточнение может supersede running turn по существующим state rules;
- reason «answer did not fit» сохраняется как аналитический сигнал.

### Acceptance

- «Помогло» не меняет resolution state;
- «Не совпадает» не повторяет тот же ответ без нового condition/evidence;
- максимум один targeted question за recovery step;
- «Нужен специалист» работает даже при недоступном generator, если handoff infrastructure жива.

## 7. Feature 5 — Emerging Issue Detector

### Проблема

Массовая новая проблема выглядит как десятки независимых тикетов, пока человек вручную не заметит повторяемость.

### Поведение

`knowledge` analytics агрегирует недавние обращения по наблюдаемым признакам: topic/symptom, decision/handoff outcome, unresolved/negative signals, representative examples. Для временного окна формируется read-only card:

```text
Возможная повторяющаяся проблема
Тема: подписание УПД / расчётный счёт
23 похожих обращения за 2 часа
рост относительно baseline: ...
unresolved: ...
limitations: ...
```

Допустимые выводы:

- «аномальный рост похожих обращений»;
- «гипотеза системной/процессной проблемы»;
- «нужна проверка».

Недопустимые без внешнего подтверждения:

- «обнаружен баг Портала»;
- «инцидент подтверждён»;
- выдуманный ETA/масштаб влияния.

Правила:

1. Всегда показывать `n`, окно времени, representative examples и limitations.
2. Cluster/group не является доказательством incident.
3. P1 detector сначала read-only; он не меняет routing/Decision автоматически.
4. Если позже active-case enrichment будет влиять на routing или user message, это отдельный contract/policy change.
5. Пользовательское сообщение «видим похожие обращения» допустимо только после явной продуктовой политики и достаточного observable evidence; не внедрять молча.

### Acceptance

- один кейс не создаёт «массовый инцидент»;
- spike можно объяснить реальными case IDs/examples;
- baseline/window/config сохраняются;
- hypothesis маркируется как hypothesis.

## 8. Feature 6 — Knowledge Gap Radar

### Проблема

Повторяющиеся `HANDOFF_OFFER` из-за отсутствующих знаний — это не только нагрузка поддержки, но и измеримый пробел knowledge base.

### Поведение

Radar группирует обращения, где evidence был insufficient / отсутствовала применимая инструкция / после ограниченных clarifications всё равно потребовался человек.

Для группы показывать:

```text
Пробел знаний
Тема / symptom
N обращений
N handoff из-за отсутствия подтверждённого ответа
Representative questions
Какие источники уже проверялись
Human resolutions available: N (если есть)
```

Приоритизация может учитывать:

- volume;
- unresolved rate;
- negative information-quality signal;
- долю handoff именно по knowledge insufficiency;
- наличие повторяющегося verified human resolution.

### Candidate knowledge card

Если есть достаточно проверенных human resolutions, система может подготовить **proposal** новой/обновлённой knowledge card:

```text
candidate only
→ source/human-resolution provenance
→ reviewer checks
→ explicit approval
→ new knowledge snapshot
```

Никакого auto-publish/self-learning в normative corpus.

Правила:

1. Причина handoff должна быть отделена от infrastructure failure и от случая «нужна factual state check».
2. Historical resolution остаётся аналитическим evidence, пока человек не утвердил normative card.
3. Proposal обязан содержать provenance и uncertainty/limitations.
4. Radar не обвиняет специалиста и не превращает частый вопрос автоматически в корректную инструкцию.

### Acceptance

- infrastructure errors не считаются knowledge gaps;
- группы показывают реальные representative cases;
- candidate card невозможно использовать user-facing retrieval до review/approval;
- после публикации создаётся новая версия/snapshot, старый provenance не теряется.

## 9. Feature 7 — Presentation Controls: «Короче / Пошагово / Объяснить проще»

### Проблема

Один и тот же корректный ответ может быть слишком формальным, длинным или сложным для конкретного пользователя.

### Поведение

После verified answer пользователь может выбрать форму представления:

```text
[Короче]
[Пошагово]
[Объяснить проще]
```

Это **presentation transform**, а не новый поиск и не новая истина.

Правила:

1. Использовать тот же `snapshot_id`, evidence fragments и applicable conditions.
2. Нельзя добавлять новые факты, коды, сроки, кнопки, действия или источники.
3. После transform повторно проверить критичные codes/numbers/durations/actions/source refs.
4. При неудачной проверке оставить исходный verified answer; не публиковать unsafe rewrite.
5. `Пошагово` может отрендерить Interactive Resolution Plan, только если source поддерживает последовательность; иначе это просто структурированное форматирование исходного ответа.
6. Новый retrieval запускается только если пользователь добавил новый контекст/вопрос, а не из-за смены presentation mode.

### Acceptance

- source list не меняется от presentation toggle;
- фактический смысл/ограничения сохраняются;
- transform failure не ломает основной answer;
- high-risk warning/condition нельзя «упростить» до исчезновения.

## 10. Сквозной UX: показывать работу, не chain-of-thought

Семь фич должны опираться на уже существующую server-driven timeline. Прозрачность означает:

- что система проверяет сейчас;
- какой источник использует;
- какие условия применимости известны/неизвестны;
- что будет дальше;
- кому передано обращение и какой статус вернул adapter;
- почему аналитика считает проблему повторяющейся (наблюдаемые counts/examples).

Не показывать hidden reasoning, внутренние prompts или декоративные «агенты думают».

`Resolution Journey` остаётся cross-cutting UI pattern поверх реальных `case_events`, а не восьмой независимой AI-фичей.

## 11. Рекомендуемый порядок реализации

После стабильного core flow/G2:

1. **Applicability Card** — дешёво делает Answerability Gate видимым пользователю.
2. **Presentation Controls** — низкая стоимость, тот же verified evidence.
3. **Smart Recovery** — максимальная ценность для сложного tail без нового сервиса.
4. **Interactive Resolution Plan** — после проверки procedure/condition структуры.
5. **Portal Context Passport** — как только есть trusted integration/demo context contract.
6. **Knowledge Gap Radar** — поверх реальных причин abstain/handoff.
7. **Emerging Issue Detector** — после устойчивого analytics baseline и достаточного `n`.

Не реализовывать семь функций параллельно только ради количества. Лучше 2–3 полностью работающих и измеримых product flows, чем семь карточек без данных и failure paths.

## 12. Demo flow, который связывает продукт

```text
Пользователь открывает поддержку из процесса Портала
→ Context Passport уже содержит trusted role/process/document
→ вопрос пользователя
→ grounded retrieval + Answerability
→ Answer + Applicability Card + source
→ Resolution Plan / «Объяснить проще»
→ пользователь: «Не совпадает с моей ситуацией»
→ Smart Recovery задаёт один полезный вопрос
→ если evidence всё ещё недостаточно: prepared handoff
→ case/feedback попадают в analytics
→ повторяющиеся no-answer кейсы появляются в Knowledge Gap Radar
→ повторяющийся временной spike появляется как Emerging Issue hypothesis
```

Этот flow демонстрирует не больше моделей, а **меньше пользовательского труда и более зрелый support loop**.

## 13. Product review checklist

Перед merge user-facing/analytics feature skeptic должен спросить:

1. Какую реальную работу пользователя/специалиста эта функция убирает?
2. Какой authoritative state/evidence её питает?
3. Что она показывает, когда данных нет?
4. Может ли UI выдать inferred/simulated/hypothesis за verified fact?
5. Добавляет ли функция новый model/service без измеренной необходимости?
6. Что происходит при partial failure?
7. Какая метрика или observable event докажет пользу?
8. Требует ли это изменения frozen contract/state model? Если да — обновлены ли source-of-truth и contract tests?
9. Не нарушает ли функция `ANSWER != RESOLVED`, corpus separation, handoff truth или moderation rules?
10. Можно ли удалить эту функцию и не потерять core correctness? Если нет — возможно, её место уже в `product-spec.md`/P0, а не только здесь.
