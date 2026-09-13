import type { CaseSnapshot } from "../../api/types";

const STAGE_COPY: Record<number, { eyebrow: string; title: string }> = {
  1: { eyebrow: "Этап 1 из 4", title: "Запрос принят" },
  2: { eyebrow: "Этап 2 из 4", title: "Ищем решение" },
  3: { eyebrow: "Этап 3 из 4", title: "Проверяем условия" },
  4: { eyebrow: "Этап 4 из 4", title: "Готовим ответ" }
};

/**
 * web-api-v0.md has no numeric stage field — this is a presentation-only projection of the active
 * turn plus its server-authored TURN_STAGE events. It disappears as soon as the turn reaches any
 * terminal result and must never be treated as a second backend state machine.
 */
export function deriveRequestStage(snapshot: CaseSnapshot): number | null {
  if (snapshot.conversation_status !== "ACTIVE" || snapshot.completed_at) {
    return null;
  }

  const turnStatus = snapshot.active_turn?.status;
  if (turnStatus !== "QUEUED" && turnStatus !== "RUNNING") return null;

  const turnId = snapshot.active_turn?.turn_id;
  const publishedStages = snapshot.timeline.filter(
    (item) => item.type === "TURN_STAGE" && (!turnId || item.turn_id === turnId)
  ).length;
  return Math.max(turnStatus === "RUNNING" ? 2 : 1, Math.min(4, publishedStages + 1));
}

export function RequestStatusStepper({ snapshot }: { snapshot: CaseSnapshot }) {
  const stage = deriveRequestStage(snapshot);
  if (stage === null) return null;
  const copy = STAGE_COPY[stage];
  const progress = stage / 4;

  return (
    <div className="flex h-12 items-center gap-3">
      <div
        className="relative size-12 shrink-0 rounded-full transition-[background] duration-500"
        style={{ background: `conic-gradient(var(--action-primary) ${progress * 360}deg, var(--border-default) 0deg)` }}
      >
        <div className="absolute inset-1 flex items-center justify-center rounded-full bg-white text-xs font-semibold text-[var(--content-primary)] animate-pulse [animation-duration:2.5s]">
          {stage}/4
        </div>
      </div>
      <div key={stage} className="flex h-12 animate-fade-up flex-col justify-center gap-px">
        <p className="text-xs text-[var(--content-secondary)]">{copy.eyebrow}</p>
        <p className="text-sm font-medium text-[var(--content-primary)]">{copy.title}</p>
      </div>
    </div>
  );
}
