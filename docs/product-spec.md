# Product specification

## 1. Product verdict

TenderHack MVP — **локальный помощник поддержки Портала поставщиков**, который:

1. отвечает только по проверяемым источникам и с учетом условий применимости;
2. задает максимум полезных уточнений, когда они действительно меняют решение;
3. честно сообщает, когда подтвержденного ответа нет;
4. предлагает передачу сложного обращения с уже собранным контекстом;
5. отдельно анализирует качество доступных ответов/решений и повторяющиеся проблемы.

Это не полноценный helpdesk, не автономный исполнитель операций Портала и не свободный multi-agent runtime.

## 2. Зафиксированные решения

| Вопрос | Решение |
|---|---|
| Runtime orchestration | Один управляемый orchestrator/state machine с ограниченным числом LLM-вызовов |
| Coding workflow | Subagents разрешены и поощряются для разработки/review; это не product runtime |
| Vector storage | PostgreSQL + pgvector; отдельная vector DB не нужна на текущем масштабе |
| Retrieval | Exact entities + PostgreSQL FTS + dense retrieval + fusion + reranking |
| Нормативное знание | Версионированные инструкции и проверенные производные карточки с source anchors |
| Исторические решения | Только аналитический корпус; нельзя автоматически повторять как универсальный ответ |
| Operator UI | Полноценный кабинет не P0; нужен handoff package + read-only analytics |
| External AI APIs | Не использовать в штатном интеллектуальном контуре |
| Generation | Локальная модель; финальный runtime подтверждается hardware smoke-test |
| Главный выигрыш | Корректность развилок, источники, честные unknowns, надежные состояния, воспроизводимые измерения |

## 3. P0

Обязательная поставка:

1. Русскоязычный чат с восстанавливаемой историей текущего обращения; список своих обращений (активные / архив).
2. Поиск по всем предоставленным PDF с источником до фрагмента/страницы; кнопка «Открыть источник» в ответе, если ответ опирается на фрагмент.
3. Опечатки и варианты терминов без повреждения кодов, номеров, отрицаний и длительностей.
4. Decisions: `ANSWER`, `CLARIFY`, `HANDOFF_OFFER`, `ANSWER_AND_HANDOFF`, `MODERATION_WARNING`, `MODERATION_CLOSE`, `TECHNICAL_ERROR`.
5. Явный путь «подтверждённого ответа нет» с кнопкой «Обратиться к оператору поддержки» под ответом.
6. Маршрутизация L1/L2 с reason codes; возможная инженерная диагностика — признак, не автоматическая отправка в L3.
7. Модерация ненормативной лексики: предупреждение в чате при первом подтверждённом нарушении, закрытие чата при повторном (§14).
8. Передача одним действием с editable summary; виджет статуса передачи: `Pending / Accepted / Simulated / Failed`, а также этап и специалист — только если их сообщил адаптер (§17).
9. Завершение обращения (пользователем или по терминальному статусу поддержки) с уведомлением; завершённый чат уходит в архив и остаётся читаемым (§16.1).
10. Уведомления: in-app inbox + живой поток + системное уведомление браузера при скрытой вкладке (§16.2).
11. Feedback после завершения: четыре независимых сигнала — оценка специалиста, оценка качества информации, «проблема решена?», комментарий (§18).
12. Read-only аналитика качества и повторяющихся проблем.
13. Полностью локальный запуск, tests/evals, decision log, BPMN и reproducible deployment.

## 4. P1

Только после стабильного P0:

- OCR/vision для пользовательских вложений при согласованных ограничениях;
- proposal новой knowledge card по проверенному human resolution;
- semantic clustering поверх надежного baseline;
- длинная история диалога;
- draft ответа сотруднику;
- HNSW только после измеренного bottleneck;
- уведомления при закрытом браузере (Web Push / email / SMS) — требуют внешних push-сервисов или контактных данных, конфликтуют с офлайн-демо (ADR-0002);
- автозавершение обращения по неактивности.

## 5. Не делать на хакатоне

- автоматическое изменение контрактов/документов/платежей/подписей;
- voice;
- полноценный многопользовательский helpdesk;
- персональные рейтинги сотрудников;
- autonomous web browsing;
- GraphRAG/knowledge graph без доказанной необходимости;
- fine-tuning генеративной LLM;
- несколько конкурирующих vector DB;
- swarm автономных runtime-агентов.

## 6. Корпуса знаний

### 6.1. Нормативный корпус

Для user-facing ответа разрешены:

- предоставленные инструкции;
- проверенные производные карточки процедур/условий;
- активная версия/snapshot базы знаний.

Каждый fragment должен иметь минимум:

```text
fragment_id
document_version_id
snapshot_id
page/section/source anchor
kind
text
role/process/status/error-code metadata
review_status
text_hash
```

### 6.2. Исторический аналитический корпус

Исторические обращения и `Решение` нужны для:

- тем/частот;
- пользовательской терминологии;
- примеров симптомов;
- поиска пробелов знаний;
- read-only quality analysis.

Они **не** разрешают генератору автоматически повторять решение другому пользователю.

## 7. Query understanding

Сохранять отдельно raw text и normalized text.

Нормализация не должна изменять:

- числа/ведущие нули;
- error codes и suffixes;
- номера документов;
- отрицания;
- длительности/единицы;
- точный текст статуса.

Контекстные slots:

- role;
- process;
- EDO/provider;
- document type;
- status;
- user action;
- error code;
- duration;
- already tried.

Для каждого slot хранить provenance: `user_explicit`, `trusted_portal_context`, `inferred`, `unknown`.

Не спрашивать все slots заранее. Уточнять только значение, которое меняет следующий decision.

## 8. Retrieval pipeline

Базовый порядок:

1. exact extraction кодов/статусов;
2. lexical candidates (до 30);
3. dense candidates (до 30);
4. ограниченное исправление опечаток через словарь/`pg_trgm`, если lexical weak;
5. fusion через RRF;
6. дедупликация одинакового evidence;
7. rerank до ~20 кандидатов;
8. выбрать 4–6 фрагментов и parent context при необходимости;
9. максимум одно дополнительное расширение поиска;
10. затем `CLARIFY` или `HANDOFF_OFFER`, а не бесконечный поиск.

Самый высокий score, несколько похожих chunks или self-reported LLM confidence не являются достаточным доказательством ответа.

## 9. Answerability Gate

Перед `ANSWER` проверить пять условий:

1. **Source:** есть допустимый fragment активного snapshot?
2. **Specificity:** он отвечает на конкретное действие/ошибку, а не просто на тему?
3. **Applicability:** совпадают существенные роль, process/provider, status/type?
4. **Completeness:** сохранены предпосылки, ограничения, исключения?
5. **Need human:** не требует ли ситуация фактической проверки/действия специалиста?

Результат при провале — expand once, `CLARIFY`, `HANDOFF_OFFER` или `TECHNICAL_ERROR` в зависимости от причины.

**Infrastructure failure нельзя выдавать за «в базе нет ответа».**

## 10. Orchestrator decisions

| Decision | Условие |
|---|---|
| `ANSWER` | Есть применимое подтверждение, внешняя проверка не нужна |
| `CLARIFY` | Не хватает конкретного условия, которое меняет ответ |
| `HANDOFF_OFFER` | Недостаточно знаний, нужна фактическая проверка или пользователь просит человека. Пользователь видит прямой текст «подтверждённого ответа нет» и кнопку «Обратиться к оператору поддержки» |
| `ANSWER_AND_HANDOFF` | Есть полезное подтвержденное объяснение, но инструкция/ситуация требует поддержки |
| `MODERATION_WARNING` | Первое подтверждённое нарушение moderation policy в этом чате: предупреждение, чат остаётся активным |
| `MODERATION_CLOSE` | Подтверждённое нарушение при уже исчерпанных предупреждениях: чат закрывается |
| `TECHNICAL_ERROR` | Search/model/storage не позволяют безопасно закончить turn |

На обычном turn — не более двух генеративных вызовов; на сложном — максимум три. Exact parsing, direct human request и простые deterministic rules не требуют LLM.

## 11. Clarification rules

- Один вопрос за раз.
- Не спрашивать уже известное.
- Не более двух последовательных clarifications в одном нерешенном сценарии.
- Не спрашивать повторно slot после «не знаю».
- Пользователь может попросить поддержку в любой момент.
- На multi-part request ответить на подтвержденную часть и отдельно обозначить нерешенную.

## 12. Post-generation verification

Генератор возвращает structured draft с support refs.

Кодом проверить:

- каждый `fragment_id` реально был передан модели и доступен;
- quoted support присутствует в canonical text;
- коды, числа, сроки, единицы и названия кнопок не появились из ничего;
- source URLs/документы не сгенерированы моделью;
- нет утверждения о фактической проверке Portal state без инструмента;
- нет неподдержанного изменения/удаления/подписи;
- существенные инструкции имеют source и applicable conditions.

При failure: один ограниченный rewrite/экстрактивный fallback; затем handoff. Непроверенный draft не публикуется.

## 13. Risk levels

- **Low:** навигация, просмотр сведений → обычный grounded answer.
- **Medium:** создать/исправить данные до отправки → подтвердить роль/стадию/условия.
- **High:** удаление/аннулирование/замена подписанного документа, финансово значимое действие → только проверенная conditional card; при неизвестных условиях clarify/handoff.

MVP сам такие операции не выполняет.

## 14. Moderation

Формальное требование организаторов: подтверждённая ненормативная лексика завершает текущее нарушающее взаимодействие «согласно согласованной политике». Принятая политика команды — **warning-first** (решение 2026-09-12, закрывает OD-001):

```text
первое подтверждённое нарушение в чате
→ Decision = MODERATION_WARNING
→ в чате: «Предупреждение 1 из 1. При повторном нарушении чат будет закрыт.»
→ moderation_warning_count = 1, чат остаётся ACTIVE, ход завершён без retrieval/generation

повторное подтверждённое нарушение в том же чате
→ Decision = MODERATION_CLOSE
→ ConversationStatus = CLOSED_MODERATION, чат read-only для новых сообщений
```

Порог `Moderation:CloseAfterWarnings` — серверная конфигурация (по умолчанию `1`); frontend никогда не решает, первое это нарушение или второе. Счётчик привязан к чату: новое обращение начинается с `0`.

Pipeline:

1. отдельная moderation normalization;
2. словарь/формы/word boundaries;
3. обработка очевидной obfuscation;
4. ambiguous case — локальная contextual check (`knowledge.moderation_context`); `UNCERTAIN` → **не** считается подтверждённым нарушением, ход продолжается как обычный;
5. хранить `rule_id`, version и source offset.

После `MODERATION_WARNING` и `MODERATION_CLOSE` не запускать консультационный retrieval/generation в этом ходе.

Не блокировать пользователя навсегда (он может открыть новое обращение), не удалять историю и не отменять уже принятую handoff-заявку. Feedback-виджет после `CLOSED_MODERATION` не показывается.

## 15. Routing

Не путать topic и support line.

Хранить:

- `service_need`: consultation / account_or_state_check / complex_process / technical_diagnosis;
- `recommended_line`: L1/L2;
- `dispatch_queue`: фактическая очередь согласно channel policy;
- `engineering_review_suggested`: возможная необходимость L3 после диагностики;
- `reason_codes`.

Если точных labels линии в датасете нет, не выдавать self-labeled training как ground truth.

Низкая уверенность → консервативный L2/manual path с reason code, а не уверенный выдуманный диагноз.

## 16. State model

Не хранить всё в одном `status`.

```text
conversation_status: ACTIVE | CLOSED_USER | CLOSED_SUPPORT | CLOSED_MODERATION
resolution_status: UNKNOWN | RESOLVED | UNRESOLVED
turn_status: QUEUED | RUNNING | COMPLETED | FAILED | SUPERSEDED
last_decision: decision enum
handoff_status: NOT_REQUESTED | PENDING | ACCEPTED | SIMULATED_ACCEPTED | FAILED
moderation_warning_count: int
handoff.stage / handoff.assigned_specialist / handoff.terminal: факты адаптера, nullable
```

Ключевые инварианты:

- `ANSWER` не устанавливает `RESOLVED`.
- Positive feedback (по информации или по специалисту) не устанавливает `RESOLVED`.
- `RESOLVED`/`UNRESOLVED` появляются только из явного `complete(solved)` пользователя или из терминального статуса адаптера.
- Handoff success только после adapter acknowledgement; этап/специалист — только из фактов адаптера.
- Новый user input может supersede running turn.
- UI восстанавливает state с API, а не из локальной animation.
- State-changing requests имеют idempotency semantics.

### 16.1. Завершение и архив

Обращение завершается **только** одним из трёх явных способов (`architecture.md §5.8`):

| Кто | Как | Результат |
|---|---|---|
| Пользователь | кнопка «Завершить обращение» → вопрос «Проблема решена?» (да / нет / пропустить) | `CLOSED_USER`, `resolution_status` по ответу |
| Поддержка | терминальный статус от адаптера (реального или demo) | `CLOSED_SUPPORT`, `resolution_status` по факту адаптера |
| Модерация | второе подтверждённое нарушение | `CLOSED_MODERATION` |

При завершении пользователь получает уведомление (§16.2) и — кроме модерационного закрытия — feedback-виджет прямо в диалоге (§18). Завершённый чат:

- read-only для новых сообщений (`CASE_CLOSED` при попытке написать);
- остаётся полностью читаемым, включая источники и статус передачи;
- отображается в разделе «Архив» списка обращений (проекция `conversation_status != ACTIVE`).

Автозавершение по таймеру/неактивности не делаем.

### 16.2. Уведомления

Уведомление — серверный факт, порождённый событием кейса (`HANDOFF_UPDATED`, `CASE_COMPLETED`, `FEEDBACK_REQUESTED`), сохранённый в `api` и привязанный к владельцу сессии.

Как пользователь его получает:

1. **Вкладка открыта** — toast в интерфейсе из живого потока уведомлений (независимо от того, какой чат открыт).
2. **Вкладка скрыта / другое приложение** — системное уведомление браузера (Web Notifications API), если пользователь дал разрешение. Работает локально, без внешних push-серверов.
3. **Браузер был закрыт** — при возвращении: badge на списке обращений и inbox непрочитанных.

Web Push, email и SMS — вне P0 (§4): требуют внешних сервисов доставки или контактных данных, которых продукт не собирает, и ломают офлайн-демо.

`MODERATION_WARNING` не является уведомлением — оно живёт в ленте чата.

## 17. Handoff

Передавать минимум:

- case/channel/dispatch queue;
- summary без выдуманных фактов;
- user-reported context отдельно от verified Portal context;
- already tried;
- unknown fields;
- sources checked;
- handoff reason;
- relevant message IDs;
- engineering review flag;
- integration mode.

Перед отправкой пользователь может поправить summary. Существенное изменение context пересчитывает routing.

Виджет статуса передачи появляется в диалоге после подтверждения и живёт до завершения обращения. Статусы:

- Pending — отправляем;
- Accepted — подтверждено принимающей системой;
- Simulated — демонстрационная передача, реальной заявки нет (обязательно видимая пометка «демо»);
- Failed — данные сохранены, можно повторить.

После Accepted/Simulated виджет дополнительно показывает **только те факты, которые сообщил адаптер** (`contracts/support-adapter-v0.md §6`):

- этап (`stage.display_name`, например «В очереди» / «Назначен специалист» / «В работе»);
- специалист (`assigned_specialist.display_name`), если адаптер вернул;
- время последнего обновления.

Строка, для которой адаптер ничего не вернул, скрывается — не «Специалист: уточняется», а отсутствие строки. Не выводить этап или специалиста из очереди/линии маршрутизации. Demo-адаптер в режиме `staged` проигрывает детерминированный сценарий «в очереди → назначен Демо-специалист → в работе → решено» и помечает каждый шаг как simulated.

Терминальный статус от адаптера (`RESOLVED` / `CLOSED_UNRESOLVED` / `CANCELLED`) завершает обращение (§16.1).

Не обещать SLA, которого нет.

## 18. Feedback и quality analytics

### 18.1. Feedback пользователя (после завершения)

Интерактивный виджет в диалоге после `CASE_COMPLETED` (кроме `CLOSED_MODERATION`). Четыре независимых сигнала, каждый nullable (решение 2026-09-12, закрывает OD-002):

| Сигнал | Значения | Когда показывать |
|---|---|---|
| `specialist_rating` — оценка работы специалиста | `POSITIVE` / `NEGATIVE` / null | только если в обращении участвовал человек (`handoff_status ∈ {ACCEPTED, SIMULATED_ACCEPTED}`); при simulated — с пометкой «демо» |
| `information_quality_rating` — оценка качества полученной информации | `POSITIVE` / `NEGATIVE` / null | всегда |
| `solved` — «проблема решена?» | `true` / `false` / null | если пользователь ещё не ответил при завершении |
| `comment_text` | свободный текст | всегда |

Правила:

- feedback можно оставить один раз на обращение; повтор с тем же `Idempotency-Key` — тот же результат;
- positive feedback не устанавливает `RESOLVED`;
- `solved` из feedback-виджета обновляет `resolution_status`, только если он был `UNKNOWN`;
- `api` хранит feedback у себя и пушит копию в `knowledge` (`POST /v0/quality/feedback`) с `specialist_ref`, если адаптер сообщил идентификатор специалиста.

### 18.2. Что анализирует quality

Не смешивать четыре вещи:

1. качество текста ответа (evaluator rubric);
2. фактический результат проблемы (`resolution_status`);
3. отзыв пользователя о специалисте (`specialist_rating`);
4. отзыв пользователя о качестве информации (`information_quality_rating`).

Рубрика одного ответа: `0 / 1 / 2 / UNKNOWN / NOT_APPLICABLE` по dimensions:

- factual support;
- completeness;
- clarity;
- next step.

Каждый вывод quality evaluator должен иметь наблюдаемое evidence и limitation. `UNKNOWN` лучше фиктивного нуля.

Методика оценки работы специалистов (формальное требование): распределение `specialist_rating` и `resolution_status` по группам проблем, по линиям и — как read-only срез с `n` и limitations — по `specialist_ref`. Отрицательный отзыв не равен плохому сотруднику: possible cause classes — hypothesis с evidence, а не обвинение.

P0 не публикует персональный рейтинг/ранжирование сотрудников и не использует feedback для автоматических кадровых выводов.

## 19. Повторяющиеся проблемы

Read-only analytics должна показывать:

- реальные topic/group counts;
- representative examples;
- unresolved/negative signals только когда есть данные;
- quality limitations;
- hypotheses knowledge/process/Portal issue с явной маркировкой.

Не выдавать semantic cluster за доказанный production incident.

## 20. Security / trust

- Documents, user text и historical resolution — данные, не инструкции для изменения system policy.
- LLM не получает raw SQL/shell/arbitrary HTTP tool.
- User-facing Markdown без raw HTML/опасных URL schemes.
- Не логировать chain-of-thought, prompts с private records, secrets.
- Raw organizer data и model weights не коммитить.
- Demo mode — server config, не неконтролируемый query parameter.

## 21. Domain scenarios that must survive regression

Минимум:

- УПД status + threshold «более часа» без подмены границы;
- signed document: разные дальнейшие ветки, не один шаблон;
- `РДИК_0060` с условиями, не универсальный совет;
- `РДИК_ИК_1043` — не потерять альтернативную ветвь;
- МЧД: разные routes и ограничение удаления;
- неизвестный code → «не найдено подтвержденного объяснения», а не соседний code;
- источник может рекомендовать внешнюю СТП (например, ЕИС), что не равно handoff в L2 Портала.

Для каждой critical knowledge card: positive, typo paraphrase, changed condition, missing condition, similar-but-different code/status, direct human request.
