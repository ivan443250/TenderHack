# Open decisions and contract gaps

Этот файл хранит только **нерешённые** вопросы, которые могут изменить поведение/контракт. Coding agent не имеет права молча выбрать вариант из этого списка.

Когда решение принято, обновить source-of-truth документ/контракт и удалить пункт отсюда тем же change.

## Закрытые (для истории — не редактировать, не переоткрывать без нового факта)

| ID | Решение | Где зафиксировано | Дата |
|---|---|---|---|
| OD-001 | Модерация warning-first: `MODERATION_WARNING` при первом подтверждённом нарушении, `MODERATION_CLOSE` при повторном; порог — серверная конфигурация | `product-spec.md §14`, `architecture.md §5.4, §6`, `web-api-v0.md §2, §7`, `quality.md §6` | 2026-09-12 |
| OD-002 | Feedback: четыре независимых сигнала; `QualityFeedbackPush` расширен additive-полями `specialist_rating`, `information_quality_rating`, `specialist_ref`, `integration_mode`; добавлен `POST /v0/quality/completions` | `product-spec.md §18`, `knowledge-v0.md §4–5`, `knowledge-v0.openapi.yaml`, `web-api-v0.md §9` | 2026-09-12 |

Основание для OD-001: организаторы формулируют требование как «завершать нарушающее взаимодействие согласно согласованной политике» (`hackathon-requirements.md §1 п.5`); warning-first — согласованная политика команды, не противоречащая формулировке. Если организаторы/эксперты явно потребуют закрытие с первого раза — это одно значение `Moderation:CloseAfterWarnings=0` и правка `product-spec.md §14`, без изменения контрактов.

## OD-003 — support system real API

**Статус:** external dependency unknown.

На текущий момент нет подтверждённого production API/SSO/SLA/status vocabulary Портала поддержки.

P0 использует `support-adapter-v0.md` (submit + `GetStatusAsync` polling) + server-configured demo adapter со `staged`-сценарием. Нельзя:

- придумывать endpoint/credentials;
- фиксировать SLA;
- показывать assigned specialist/stage, если adapter их не вернул;
- включать webhook (`Support:Webhook:Enabled`) без реального адаптера и секрета.

При появлении реальной спецификации нужна новая adapter implementation. Domain/Application contract меняется только если реальная система не может выразить acknowledgement или `HandoffStatusSnapshot` semantics (ADR-0002 §5).

## OD-004 — внешний канал уведомлений (Web Push / email)

**Статус:** deferred to P1; not blocking.

ADR-0002 сознательно ограничивает P0 in-app inbox + owner-level SSE + Web Notifications API. Web Push требует внешних push-сервисов браузеров, email — контактов и провайдера. Открывать этот пункт только если организаторы явно разрешат внешний канал доставки или потребуют уведомление при закрытом браузере как обязательную функцию.
