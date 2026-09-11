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

1. Русскоязычный чат с восстанавливаемой историей текущего обращения.
2. Поиск по всем предоставленным PDF с источником до фрагмента/страницы.
3. Опечатки и варианты терминов без повреждения кодов, номеров, отрицаний и длительностей.
4. Decisions: `ANSWER`, `CLARIFY`, `HANDOFF_OFFER`, `ANSWER_AND_HANDOFF`, `MODERATION_CLOSE`, `TECHNICAL_ERROR`.
5. Маршрутизация L1/L2 с reason codes; возможная инженерная диагностика — признак, не автоматическая отправка в L3.
6. Модерация ненормативной лексики по требованиям кейса.
7. Передача одним действием с editable summary и честным статусом передачи.
8. Feedback: отдельно «ответ полезен?» и «проблема решена?». 
9. Read-only аналитика качества и повторяющихся проблем.
10. Полностью локальный запуск, tests/evals, decision log, BPMN и reproducible deployment.

## 4. P1

Только после стабильного P0:

- OCR/vision для пользовательских вложений при согласованных ограничениях;
- proposal новой knowledge card по проверенному human resolution;
- semantic clustering поверх надежного baseline;
- длинная история диалога;
- draft ответа сотруднику;
- HNSW только после измеренного bottleneck.

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
| `HANDOFF_OFFER` | Недостаточно знаний, нужна фактическая проверка или пользователь просит человека |
| `ANSWER_AND_HANDOFF` | Есть полезное подтвержденное объяснение, но инструкция/ситуация требует поддержки |
| `MODERATION_CLOSE` | Подтверждено нарушение выбранной moderation policy |
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

Формальное требование: подтвержденная ненормативная лексика завершает текущий диалог.

Pipeline:

1. отдельная moderation normalization;
2. словарь/формы/word boundaries;
3. обработка очевидной obfuscation;
4. ambiguous case — локальная contextual check или консервативное отсутствие auto-close;
5. хранить `rule_id`, version и source offset.

После `MODERATION_CLOSE` не запускать консультационный retrieval/generation.

Не блокировать пользователя навсегда, не удалять историю и не отменять уже принятую handoff-заявку.

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
conversation_status: ACTIVE | CLOSED_USER | CLOSED_MODERATION
resolution_status: UNKNOWN | RESOLVED | UNRESOLVED
turn_status: QUEUED | RUNNING | COMPLETED | FAILED | SUPERSEDED
last_decision: decision enum
handoff_status: NOT_REQUESTED | PENDING | ACCEPTED | SIMULATED_ACCEPTED | FAILED
```

Ключевые инварианты:

- `ANSWER` не устанавливает `RESOLVED`.
- Positive usefulness feedback не устанавливает `RESOLVED`.
- Handoff success только после adapter acknowledgement.
- Новый user input может supersede running turn.
- UI восстанавливает state с API, а не из локальной animation.
- State-changing requests имеют idempotency semantics.

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

Статусы UI:

- Pending — отправляем;
- Accepted — подтверждено принимающей системой;
- Simulated — демонстрационная передача, реальной заявки нет;
- Failed — данные сохранены, можно повторить.

Не обещать SLA, которого нет.

## 18. Feedback и quality analytics

Не смешивать:

1. качество текста ответа;
2. фактический результат проблемы;
3. пользовательский feedback.

Рубрика одного ответа: `0 / 1 / 2 / UNKNOWN / NOT_APPLICABLE` по dimensions:

- factual support;
- completeness;
- clarity;
- next step.

Каждый вывод quality evaluator должен иметь наблюдаемое evidence и limitation. `UNKNOWN` лучше фиктивного нуля.

Отрицательный отзыв не равен плохому сотруднику. Possible cause classes — hypothesis с evidence, а не обвинение.

P0 не строит персональный рейтинг сотрудников.

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
