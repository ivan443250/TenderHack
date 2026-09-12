import type { CaseSnapshot } from "../../api/types";

const STAGE_COPY: Record<number, { eyebrow: string; title: string }> = {
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
  return 1;
}

export function RequestStatusStepper({ snapshot }: { snapshot: CaseSnapshot }) {
  const stage = deriveRequestStage(snapshot);
  const copy = STAGE_COPY[stage];
  const progress = stage / 7;

  return (
    <div className="flex h-11 items-center gap-3">
      <div
        className="relative size-11 shrink-0 rounded-full"
        style={{ background: `conic-gradient(var(--action-primary) ${progress * 360}deg, var(--border-default) 0deg)` }}
      >
        <div className="absolute inset-1 flex items-center justify-center rounded-full bg-white text-[10px] font-medium text-[var(--content-primary)]">
          {stage}/7
        </div>
      </div>
      <div className="flex h-11 flex-col justify-center gap-px">
        <p className="text-[10px] text-[var(--content-secondary)]">{copy.eyebrow}</p>
        <p className="text-xs font-medium text-[var(--content-primary)]">{copy.title}</p>
      </div>
    </div>
  );
}
