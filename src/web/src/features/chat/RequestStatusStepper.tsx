import type { CaseSnapshot } from "../../api/types";

/** B6 (docs/plans/active/2026-09-demo-readiness.md): a failed turn is a distinct end state, not
 * "still on step 1" — it must never be confused with a fresh, not-yet-processed request. */
export const FAILED_STAGE = -1;

const STAGE_COPY: Record<number, { eyebrow: string; title: string }> = {
  [FAILED_STAGE]: { eyebrow: "Ошибка", title: "Техническая ошибка" },
  1: { eyebrow: "Этап 1 из 7", title: "Запрос принят" },
  2: { eyebrow: "Этап 2 из 7", title: "Ищем решение" },
  3: { eyebrow: "Этап 3 из 7", title: "Проверяем ответ" },
  4: { eyebrow: "Этап 4 из 7", title: "Проверяем источники" },
  5: { eyebrow: "Этап 5 из 7", title: "Передаём специалисту" },
  6: { eyebrow: "Этап 6 из 7", title: "Подтверждаем результат" },
  7: { eyebrow: "Этап 7 из 7", title: "Завершено" }
};

/**
 * web-api-v0.md has no 7-stage field — this is a presentation-only best-effort projection of
 * existing contract fields onto the Figma "Chat/Request Status" stepper (turn processing is a
 * single synchronous round trip today, so stages 2/3 are rarely observable in practice; they are
 * kept for when turn processing becomes async). Never treat this number as backend state.
 */
export function deriveRequestStage(snapshot: CaseSnapshot): number {
  if (snapshot.conversation_status !== "ACTIVE" || snapshot.completed_at) {
    return 7;
  }

  const handoffStatus = snapshot.handoff?.status;
  if (handoffStatus === "ACCEPTED" || handoffStatus === "SIMULATED_ACCEPTED") {
    return 6;
  }
  if (handoffStatus === "PENDING") {
    return 5;
  }

  const turnStatus = snapshot.active_turn?.status;
  if (turnStatus === "QUEUED") return 1;
  if (turnStatus === "RUNNING") return 2;
  if (turnStatus === "COMPLETED" && snapshot.timeline.length > 0) return 4;
  if (turnStatus === "FAILED") return FAILED_STAGE;
  return 1;
}

export function RequestStatusStepper({ snapshot }: { snapshot: CaseSnapshot }) {
  const stage = deriveRequestStage(snapshot);
  const copy = STAGE_COPY[stage];
  const isFailed = stage === FAILED_STAGE;
  const progress = isFailed ? 1 : stage / 7;

  return (
    <div className="flex h-12 items-center gap-3">
      <div
        className="relative size-12 shrink-0 rounded-full transition-[background] duration-500"
        style={{ background: `conic-gradient(${isFailed ? "#d92d20" : "var(--action-primary)"} ${progress * 360}deg, var(--border-default) 0deg)` }}
      >
        <div className={`absolute inset-1 flex items-center justify-center rounded-full bg-white text-xs font-semibold ${isFailed ? "text-[#d92d20]" : "text-[var(--content-primary)]"} ${stage > 0 && stage < 7 ? "animate-pulse [animation-duration:2.5s]" : ""}`}>
          {isFailed ? "!" : `${stage}/7`}
        </div>
      </div>
      <div key={stage} className="flex h-12 animate-fade-up flex-col justify-center gap-px">
        <p className={`text-xs ${isFailed ? "text-[#d92d20]" : "text-[var(--content-secondary)]"}`}>{copy.eyebrow}</p>
        <p className="text-sm font-medium text-[var(--content-primary)]">{copy.title}</p>
      </div>
    </div>
  );
}
