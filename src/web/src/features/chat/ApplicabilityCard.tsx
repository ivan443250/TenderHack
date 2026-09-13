import { useState } from "react";

import type { ApiClient } from "../../api/client";
import type { Applicability, ContextSlotProvenance, SourceDetail } from "../../api/types";
import { ChevronRightIcon } from "../../design-system/icons";

const PROVENANCE_LABEL: Record<ContextSlotProvenance, string> = {
  user_explicit: "вы указали",
  trusted_portal_context: "из Портала",
  inferred: "предположение",
  unknown: "неизвестно"
};

/** Only `user_explicit`/`trusted_portal_context` are confirmed facts — `inferred`/`unknown` must
 * never look verified next to a checkmark (product-experience.md §5, AGENTS.md §3: "inferred/
 * simulated нигде не выглядят как verified"). */
const CONFIRMED_PROVENANCE = new Set<ContextSlotProvenance>(["user_explicit", "trusted_portal_context"]);

type ApplicabilityCardProps = {
  applicability: Applicability;
  api: ApiClient;
  onOpenSource: (source: SourceDetail) => void;
};

/** F1 (docs/plans/active/2026-09-product-enhancements.md): "Почему этот ответ применим" — renders
 * exactly the facts that already passed the Answerability Gate. No confidence score, no percentage. */
export function ApplicabilityCard({ applicability, api, onOpenSource }: ApplicabilityCardProps) {
  const [expanded, setExpanded] = useState(false);
  const [openingSource, setOpeningSource] = useState(false);
  const { entities, missing_conditions: missingConditions, questions, evidence_fragment_ids: evidenceFragmentIds } = applicability;

  if (entities.length === 0 && missingConditions.length === 0) {
    return null;
  }

  async function handleOpenSource() {
    const fragmentId = evidenceFragmentIds[0];
    if (!fragmentId || openingSource) return;
    setOpeningSource(true);
    try {
      onOpenSource(await api.getSource(fragmentId));
    } catch {
      // Swallowed: the button stays visible and clickable for a retry.
    } finally {
      setOpeningSource(false);
    }
  }

  return (
    <div className="w-full max-w-[560px] animate-fade-up rounded-[18px] border border-[var(--border-default)] bg-white">
      <button
        type="button"
        onClick={() => setExpanded((prev) => !prev)}
        className="flex w-full items-center justify-between px-4 py-3 text-left text-sm font-medium text-[var(--content-primary)]"
      >
        Почему этот ответ применим
        <ChevronRightIcon className={`size-4 shrink-0 transition-transform duration-150 ${expanded ? "rotate-90" : ""}`} />
      </button>

      {expanded && (
        <div className="flex flex-col gap-3 px-4 pb-4">
          {entities.length > 0 && (
            <ul className="flex flex-col gap-1">
              {entities.map((entity) => (
                <li key={`${entity.type}-${entity.value}`} className="flex items-baseline gap-1.5 text-xs text-[var(--content-secondary)]">
                  <span className={CONFIRMED_PROVENANCE.has(entity.provenance) ? "text-[var(--action-primary)]" : "opacity-0"}>✓</span>
                  <span>
                    {entity.type}: <span className="font-medium text-[var(--content-primary)]">{entity.value}</span> ({PROVENANCE_LABEL[entity.provenance]})
                  </span>
                </li>
              ))}
            </ul>
          )}

          {missingConditions.length > 0 && (
            <div className="flex flex-col gap-1">
              <p className="text-xs font-medium text-[var(--content-primary)]">Нужно уточнить</p>
              <ul className="list-inside list-disc text-xs text-[var(--content-secondary)]">
                {(questions.length === missingConditions.length ? questions : missingConditions).map((item, index) => (
                  <li key={missingConditions[index]}>{item}</li>
                ))}
              </ul>
            </div>
          )}

          {evidenceFragmentIds.length > 0 && (
            <button
              type="button"
              disabled={openingSource}
              onClick={handleOpenSource}
              className="self-start text-xs font-medium text-[var(--action-primary)] transition-opacity hover:opacity-80 disabled:opacity-50"
            >
              Открыть источник
            </button>
          )}
        </div>
      )}
    </div>
  );
}
