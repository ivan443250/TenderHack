/** Home screen chip content (E2, docs/plans/active/2026-09-demo-readiness.md Фаза 6).
 *
 * Split into question chips (send immediately, standard chat UX) and a static "more" list revealed
 * by the "Больше популярных вопросов" action chip. P0 source is a fixed list of real formulations
 * from `src/knowledge/benchmarks/final-e2e-support-demo.json` / `organizer-questions.md` — sourcing
 * this from `GET /api/v0/analytics/issue-groups` instead is a future improvement, not in this plan. */

export const QUESTION_CHIPS = [
  "Как создать СТЕ для оферты?",
  "Как загрузить YML в каталог?",
  "Как добавить МЧД?"
] as const;

export const MORE_QUESTIONS = [
  "Как создать оферту?",
  "Как удалить МЧД?",
  "Как подписать УПД?",
  "Что делать при ошибке отправки документа исполнения?",
  "Как изменить данные контракта?",
  "УПД завис в статусе «Отправка»"
] as const;
