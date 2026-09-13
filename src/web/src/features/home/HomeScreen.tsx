import { useRef, useState } from "react";
import { useNavigate } from "react-router-dom";

import type { ApiClient } from "../../api/client";
import type { InitialCaseMessage } from "../../state/caseStore";
import { Composer, type ComposerHandle } from "../chat/Composer";
import { SuggestionChip } from "../../design-system/Chip";
import { InteractiveGlow } from "./InteractiveGlow";
import { MORE_QUESTIONS, QUESTION_CHIPS } from "./popularQuestions";

/** Главный экран (Figma 129:784). The Figma glow is a WebGPU particle shader that domes away from
 * the cursor — not a shipping web technology (it calls for an unreleased "HTML-in-Canvas" API), so
 * `InteractiveGlow` reproduces it with Canvas2D as a smooth colour field (the shader's particles are
 * dense enough to read as a gradient) with the same cursor physics.
 *
 * E2 (docs/plans/active/2026-09-demo-readiness.md Фаза 6): only `QUESTION_CHIPS` send their text as
 * a question. "Больше популярных вопросов" and "Еще один вопрос к поддержке" are action chips —
 * they never call `createCase`/`sendMessage` on their own. */
export function HomeScreen({ api }: { api: ApiClient }) {
  const navigate = useNavigate();
  const [busy, setBusy] = useState(false);
  const [expanded, setExpanded] = useState(false);
  const composerRef = useRef<ComposerHandle>(null);

  async function startCase(text: string) {
    if (busy) return;
    setBusy(true);
    try {
      const created = await api.createCase();
      const initialMessage: InitialCaseMessage = { text, clientMessageId: crypto.randomUUID() };
      navigate(`/cases/${created.case_id}`, { state: { initialMessage, initialSnapshot: created } });
    } catch {
      setBusy(false);
    }
  }

  return (
    <div className="relative flex h-full flex-1 flex-col items-center justify-center overflow-hidden bg-[var(--app-background)]">
      <InteractiveGlow className="absolute" />

      <SuggestionChip className="absolute left-[13%] top-[20%] animate-float [animation-delay:0s]" onClick={() => startCase(QUESTION_CHIPS[1])} disabled={busy}>
        {QUESTION_CHIPS[1]}
      </SuggestionChip>
      <SuggestionChip className="absolute left-[6%] top-[33%] animate-float [animation-delay:-1.2s]" onClick={() => startCase(QUESTION_CHIPS[0])} disabled={busy}>
        {QUESTION_CHIPS[0]}
      </SuggestionChip>
      <SuggestionChip className="absolute right-[6%] top-[26%] animate-float [animation-delay:-2.4s]" onClick={() => startCase(QUESTION_CHIPS[2])} disabled={busy}>
        {QUESTION_CHIPS[2]}
      </SuggestionChip>
      <SuggestionChip
        className="absolute bottom-[13%] right-[10%] animate-float [animation-delay:-3.6s]"
        onClick={() => setExpanded((prev) => !prev)}
        aria-expanded={expanded}
      >
        {expanded ? "Скрыть" : "Больше популярных вопросов"}
      </SuggestionChip>
      <SuggestionChip
        className="absolute bottom-[19%] left-[15%] animate-float [animation-delay:-4.8s]"
        onClick={() => composerRef.current?.focus()}
        disabled={busy}
      >
        Еще один вопрос к поддержке
      </SuggestionChip>

      <div className="relative flex animate-pop-in flex-col items-center gap-10">
        <h1 className="animate-glow-pulse text-[48px] font-bold tracking-[0.08px] text-white">
          Вопрос по Порталу?
        </h1>
        <Composer ref={composerRef} context="home" onSubmit={startCase} disabled={busy} />
      </div>

      {expanded && (
        <div className="relative z-10 mt-8 flex max-w-[720px] animate-fade-up flex-wrap justify-center gap-2.5 px-6">
          {MORE_QUESTIONS.map((question) => (
            <SuggestionChip key={question} onClick={() => startCase(question)} disabled={busy}>
              {question}
            </SuggestionChip>
          ))}
        </div>
      )}
    </div>
  );
}
