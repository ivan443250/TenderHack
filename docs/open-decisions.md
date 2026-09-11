# Open decisions and contract gaps

Этот файл хранит только **нерешённые** вопросы, которые могут изменить поведение/контракт. Coding agent не имеет права молча выбрать вариант из этого списка.

Когда решение принято, обновить source-of-truth документ/контракт и удалить пункт отсюда тем же change.

## OD-001 — moderation: закрывать сразу или после предупреждения

**Статус:** needs organizer/team confirmation before G2.

### Текущий repository source of truth

`product-spec.md` и `quality.md` сейчас фиксируют:

```text
confirmed profanity → MODERATION_CLOSE
```

### Последнее продуктовое уточнение команды

Желаемый UX:

```text
first confirmed violation
→ warning in current chat
→ conversation stays active

next confirmed violation in the same chat
→ close/block this chat
```

Это не account ban, не удаление истории и не отмена уже accepted handoff.

### Архитектурная подготовка

`web-api-v0.md` резервирует server event/decision `MODERATION_WARNING` и `moderation_warning_count`, чтобы warning-first policy не требовала breaking frontend change.

До принятия решения:

- Domain policy не реализовывать на предположении;
- moderation evals должны уметь покрыть оба threshold-варианта fixtures;
- UI может реализовать rendering warning event, но не сам threshold;
- после подтверждения синхронно обновить `hackathon-requirements.md` (если это официальное уточнение), `product-spec.md`, `architecture.md`, `quality.md`, Domain enum/tests и BPMN.

## OD-002 — feedback granularity в `knowledge-v0`

**Статус:** additive contract gap; resolve before Quality Analytics G3.

### Product UX now requires

После завершения обращения пользователь должен иметь возможность раздельно передать:

- оценку работы специалиста (`POSITIVE | NEGATIVE | null`);
- оценку качества полученной информации (`POSITIVE | NEGATIVE | null`);
- `solved: bool?`;
- комментарий.

`web-api-v0.md` уже фиксирует эти browser-facing поля.

### Current gap

Замороженный `QualityFeedbackPush` в `knowledge-v0.openapi.yaml` пока содержит только:

```text
helpful?
solved?
comment_text?
```

Этого недостаточно для раздельной аналитики специалиста и информации.

### Planned additive resolution

До реализации G3 расширить `QualityFeedbackPush` **optional** полями без breaking change:

```text
specialist_rating?: POSITIVE | NEGATIVE
information_quality_rating?: POSITIVE | NEGATIVE
specialist_ref?: string
```

`specialist_ref` передаётся только если реальный/simulated adapter фактически сообщил идентификатор специалиста. Он нужен для trace/grouping, но P0 **не** строит персональный рейтинг сотрудника.

`helpful` можно сохранить как backward-compatible общий signal до конца v0; новая UI не должна подменять им два раздельных поля.

При изменении обязательно синхронно обновить:

- `docs/contracts/knowledge-v0.md`;
- `docs/contracts/knowledge-v0.openapi.yaml`;
- knowledge stub fixtures;
- generated NSwag client;
- quality contract tests.

## OD-003 — support system real API

**Статус:** external dependency unknown.

На текущий момент нет подтверждённого production API/SSO/SLA/status vocabulary Портала поддержки.

P0 использует `support-adapter-v0.md` + server-configured demo adapter. Нельзя:

- придумывать endpoint/credentials;
- фиксировать SLA;
- обещать assigned specialist/stage, если adapter их не вернул.

При появлении реальной спецификации нужен новый adapter implementation; Domain/Application contract меняется только если реальная система не может выразить текущие acknowledgement semantics.
