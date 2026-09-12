import { useState } from "react";
import { useNavigate } from "react-router-dom";

import type { ApiClient } from "../../api/client";
import { Composer } from "../chat/Composer";
import { SuggestionChip } from "../../design-system/Chip";
import { InteractiveGlow } from "./InteractiveGlow";

const SUGGESTIONS = [
  "УПД завис в «Отправке»",
  "Изменить данные контракта",
  "Ошибка электронного исполнения",
  "Больше популярных вопросов",
  "Еще один вопрос к поддержке"
];

/** Главный экран (Figma 129:784). The Figma glow is a WebGPU particle shader that domes away from
 * the cursor — not a shipping web technology (it calls for an unreleased "HTML-in-Canvas" API), so
 * `InteractiveGlow` reproduces it with Canvas2D as a smooth colour field (the shader's particles are
 * dense enough to read as a gradient) with the same cursor physics. */
export function HomeScreen({ api }: { api: ApiClient }) {
  const navigate = useNavigate();
  const [busy, setBusy] = useState(false);

  async function startCase(text: string) {
    if (busy) return;
    setBusy(true);
    try {
      const created = await api.createCase();
      await api.sendMessage(created.case_id, text, crypto.randomUUID());
      navigate(`/cases/${created.case_id}`);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="relative flex h-full flex-1 items-center justify-center overflow-hidden bg-[var(--app-background)]">
      <InteractiveGlow className="absolute" />

      <SuggestionChip className="absolute left-[13%] top-[20%] animate-float [animation-delay:0s]" onClick={() => startCase(SUGGESTIONS[1])} disabled={busy}>
        {SUGGESTIONS[1]}
      </SuggestionChip>
      <SuggestionChip className="absolute left-[6%] top-[33%] animate-float [animation-delay:-1.2s]" onClick={() => startCase(SUGGESTIONS[0])} disabled={busy}>
        {SUGGESTIONS[0]}
      </SuggestionChip>
      <SuggestionChip className="absolute right-[6%] top-[26%] animate-float [animation-delay:-2.4s]" onClick={() => startCase(SUGGESTIONS[2])} disabled={busy}>
        {SUGGESTIONS[2]}
      </SuggestionChip>
      <SuggestionChip className="absolute bottom-[13%] right-[10%] animate-float [animation-delay:-3.6s]" onClick={() => startCase(SUGGESTIONS[3])} disabled={busy}>
        {SUGGESTIONS[3]}
      </SuggestionChip>
      <SuggestionChip className="absolute bottom-[19%] left-[15%] animate-float [animation-delay:-4.8s]" onClick={() => startCase(SUGGESTIONS[4])} disabled={busy}>
        {SUGGESTIONS[4]}
      </SuggestionChip>

      <div className="relative flex animate-pop-in flex-col items-center gap-10">
        <h1 className="animate-glow-pulse text-[48px] font-bold tracking-[0.08px] text-white">
          Вопрос по Порталу?
        </h1>
        <Composer context="home" onSubmit={startCase} disabled={busy} />
      </div>
    </div>
  );
}
