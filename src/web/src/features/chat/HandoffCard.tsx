import { useState } from "react";

import { ButtonPrimary, SpecialistCta } from "../../design-system/Button";
import { TextField } from "../../design-system/TextField";
import { HeadsetIcon } from "../../design-system/icons";
import type { HandoffView } from "../../api/types";

type HandoffCardProps = {
  handoff: HandoffView | null;
  onPrepare: () => Promise<void>;
  onConfirm: (summary: string) => Promise<void>;
  onRetry: (summary: string) => Promise<void>;
};

/** Chat/Handoff Card (Figma 199:357) extended with the prepare→confirm flow from web-api-v0 §8:
 * `prepare` only creates an editable package, `confirm` is the durable submission. */
export function HandoffCard({ handoff, onPrepare, onConfirm, onRetry }: HandoffCardProps) {
  const [preparing, setPreparing] = useState(false);
  const [summary, setSummary] = useState("");
  const [busy, setBusy] = useState(false);

  const status = handoff?.status ?? "NOT_REQUESTED";

  if (status === "NOT_REQUESTED" && !preparing) {
    return (
      <SpecialistCta
        icon={<HeadsetIcon className="size-[18px]" />}
        onClick={async () => {
          setPreparing(true);
          await onPrepare();
        }}
      />
    );
  }

  if (status === "NOT_REQUESTED" && preparing) {
    return (
      <div className="flex w-full max-w-[512px] flex-col gap-3 rounded-[18px] border border-[var(--border-default)] bg-white p-3.5">
        <p className="text-xs font-medium text-[var(--content-primary)]">Опишите ситуацию для специалиста</p>
        <TextField value={summary} onChange={(event) => setSummary(event.target.value)} placeholder="Коротко опишите проблему" />
        <ButtonPrimary
          disabled={busy || !summary.trim()}
          onClick={async () => {
            setBusy(true);
            try {
              await onConfirm(summary);
            } finally {
              setBusy(false);
            }
          }}
        >
          Подтвердить передачу
        </ButtonPrimary>
      </div>
    );
  }

  if (status === "FAILED") {
    return (
      <div className="flex w-full max-w-[512px] flex-col gap-3 rounded-[18px] border border-[#f5c7cc] bg-[#fff6f7] p-3.5">
        <p className="text-xs text-[var(--content-primary)]">Не удалось передать обращение специалисту.</p>
        <ButtonPrimary
          disabled={busy}
          onClick={async () => {
            setBusy(true);
            try {
              await onRetry(summary || "Повторная передача обращения специалисту.");
            } finally {
              setBusy(false);
            }
          }}
        >
          Повторить попытку
        </ButtonPrimary>
      </div>
    );
  }

  const isSimulated = handoff?.integration_mode === "SIMULATED";
  const isPending = status === "PENDING";

  return (
    <div className="flex w-full max-w-[512px] flex-col gap-1.5 rounded-[18px] border border-[var(--border-default)] bg-white p-3.5">
      <p className="text-xs font-medium text-[var(--content-primary)]">
        Обращение передано специалисту{isSimulated ? " (демо-режим)" : ""}
      </p>
      {isPending && <p className="text-xs text-[var(--content-secondary)]">Ожидаем подтверждения приёма.</p>}
      {handoff?.stage?.display_name && <p className="text-xs text-[var(--content-secondary)]">Этап: {handoff.stage.display_name}</p>}
      {handoff?.assigned_specialist?.display_name && (
        <p className="text-xs text-[var(--content-secondary)]">Специалист: {handoff.assigned_specialist.display_name}</p>
      )}
      {handoff?.stale && <p className="text-xs text-[var(--content-tertiary)]">Статус не обновляется.</p>}
    </div>
  );
}
