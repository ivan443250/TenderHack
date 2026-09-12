import { useState } from "react";

import { ButtonPrimary } from "../../design-system/Button";
import { PillChoice } from "../../design-system/Chip";
import { TextField } from "../../design-system/TextField";
import type { CaseSnapshot, FeedbackRating } from "../../api/types";
import { FeedbackThanks } from "./FeedbackThanks";

type ResolutionFeedbackProps = {
  snapshot: CaseSnapshot;
  onComplete: (solved: boolean | null) => Promise<void>;
  onSubmitFeedback: (feedback: {
    specialist_rating: FeedbackRating | null;
    information_quality_rating: FeedbackRating | null;
    solved: boolean | null;
    comment_text: string | null;
  }) => Promise<void>;
};

/** Chat/Resolution & Feedback (Figma 222:387 family), gated by web-api-v0.md §9.3 rules:
 * `solved` control only while resolution_status === UNKNOWN; `specialist_rating` only when a
 * handoff was accepted/simulated-accepted; feedback is submit-once. */
export function ResolutionFeedback({ snapshot, onComplete, onSubmitFeedback }: ResolutionFeedbackProps) {
  const [solved, setSolved] = useState<boolean | null>(null);
  const [infoRating, setInfoRating] = useState<FeedbackRating | null>(null);
  const [specialistRating, setSpecialistRating] = useState<FeedbackRating | null>(null);
  const [comment, setComment] = useState("");
  const [busy, setBusy] = useState(false);

  if (snapshot.feedback) {
    return <FeedbackThanks />;
  }

  const showSolvedControl = snapshot.resolution_status === "UNKNOWN";
  const showSpecialistControl =
    snapshot.handoff?.status === "ACCEPTED" || snapshot.handoff?.status === "SIMULATED_ACCEPTED";

  async function handleSubmit() {
    setBusy(true);
    try {
      if (showSolvedControl && solved !== null) {
        await onComplete(solved);
      }
      await onSubmitFeedback({
        specialist_rating: specialistRating,
        information_quality_rating: infoRating,
        solved,
        comment_text: comment.trim() || null
      });
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex w-full max-w-[512px] flex-col gap-3.5 rounded-[18px] border border-[var(--border-default)] bg-white p-4">
      {showSolvedControl && (
        <div className="flex flex-col gap-2">
          <p className="text-xs font-medium text-[var(--content-primary)]">Вопрос решён?</p>
          <div className="flex gap-2">
            <PillChoice selected={solved === true} onClick={() => setSolved(true)}>
              Решён
            </PillChoice>
            <PillChoice selected={solved === false} onClick={() => setSolved(false)}>
              Не решён
            </PillChoice>
          </div>
        </div>
      )}

      <div className="flex flex-col gap-2">
        <p className="text-xs font-medium text-[var(--content-primary)]">Оцените качество ответа</p>
        <div className="flex gap-2">
          <PillChoice selected={infoRating === "POSITIVE"} onClick={() => setInfoRating("POSITIVE")}>
            Полезно
          </PillChoice>
          <PillChoice selected={infoRating === "NEGATIVE"} onClick={() => setInfoRating("NEGATIVE")}>
            Не помогло
          </PillChoice>
        </div>
      </div>

      {showSpecialistControl && (
        <div className="flex flex-col gap-2">
          <p className="text-xs font-medium text-[var(--content-primary)]">
            Оцените работу специалиста{snapshot.handoff?.integration_mode === "SIMULATED" ? " (демо)" : ""}
          </p>
          <div className="flex gap-2">
            <PillChoice selected={specialistRating === "POSITIVE"} onClick={() => setSpecialistRating("POSITIVE")}>
              Полезно
            </PillChoice>
            <PillChoice selected={specialistRating === "NEGATIVE"} onClick={() => setSpecialistRating("NEGATIVE")}>
              Не помогло
            </PillChoice>
          </div>
        </div>
      )}

      <div className="flex flex-col gap-2">
        <p className="text-xs font-medium text-[var(--content-primary)]">Комментарий (необязательно)</p>
        <div className="flex items-center gap-3">
          <TextField
            value={comment}
            onChange={(event) => setComment(event.target.value)}
            placeholder="Напишите комментарий"
            className="flex-1"
          />
          <ButtonPrimary disabled={busy} onClick={handleSubmit} className="w-[150px]">
            Отправить
          </ButtonPrimary>
        </div>
      </div>
    </div>
  );
}
