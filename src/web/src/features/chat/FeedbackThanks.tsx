import { useNavigate } from "react-router-dom";

import robotTRex from "../../assets/robot-trex.svg";

/** Chat/Feedback Thanks (Figma 295:1410): post-feedback confirmation — calm product state with the
 * Portal mascot (Illustration/Robot T-Rex 299:1274: 182px art inside a 148px clip at (-19,-16)). */
export function FeedbackThanks() {
  const navigate = useNavigate();
  return (
    <div className="flex h-[220px] w-full max-w-[512px] items-center justify-between overflow-hidden rounded-[18px] border border-[var(--border-default)] bg-white py-5 pl-[22px] pr-[18px]">
      <div className="flex w-[300px] flex-col items-start justify-center gap-3">
        <p className="text-lg font-semibold text-[var(--action-primary)]">Спасибо за отзыв</p>
        <p className="w-[286px] text-xs leading-[18px] text-[var(--content-secondary)]">
          Он поможет нам точнее оценивать качество ответов и улучшать поддержку Портала.
        </p>
        <button
          type="button"
          onClick={() => navigate("/")}
          className="inline-flex h-9 items-center rounded-full border border-[var(--border-default)] bg-[var(--surface-subtle)] px-3.5 text-xs font-medium text-[var(--content-primary)] transition-colors hover:bg-white"
        >
          Задать новый вопрос
        </button>
      </div>
      <span className="relative block size-[148px] shrink-0 overflow-hidden" aria-hidden="true">
        <img src={robotTRex} alt="" className="absolute left-[-19px] top-[-16px] size-[182px] max-w-none" />
      </span>
    </div>
  );
}
