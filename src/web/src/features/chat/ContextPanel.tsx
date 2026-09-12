import { useState } from "react";

import { ChevronRightIcon, ExternalLinkIcon, FileTextIcon } from "../../design-system/icons";
import type { SourceDetail } from "../../api/types";

type ContextPanelProps = {
  usedSources: SourceDetail[];
  onClose: () => void;
};

/** Chat/Context Panel (Figma 258:743). "Materials" (the local instruction library browser) is out
 * of scope for P0 — there is no such API in web-api-v0.md — so only the Sources tab is wired to
 * real data; keeping the tab visible documents the target without inventing an endpoint. */
export function ContextPanel({ usedSources, onClose }: ContextPanelProps) {
  const [tab, setTab] = useState<"sources" | "materials">("sources");

  return (
    <aside className="flex h-full w-[380px] shrink-0 animate-slide-in-right flex-col border-l border-[var(--border-default)] bg-white">
      <div className="flex h-16 items-center justify-between pl-5 pr-4">
        <p className="text-sm font-semibold text-[var(--content-primary)]">Источники и материалы</p>
        <button type="button" onClick={onClose} aria-label="Закрыть панель" className="flex size-9 items-center justify-center rounded-2xl transition-[background-color,transform] duration-150 hover:bg-[var(--surface-subtle)] active:scale-95">
          <ChevronRightIcon className="size-4" />
        </button>
      </div>

      <div className="flex flex-col gap-3.5 overflow-y-auto px-5 pb-5 pt-2.5">
        <div className="flex h-[42px] gap-1.5 rounded-xl bg-[#f6f6f6] p-1">
          <button
            type="button"
            onClick={() => setTab("sources")}
            className={`flex-1 rounded-[9px] text-xs font-medium transition-[background-color,color,box-shadow] duration-200 ${tab === "sources" ? "bg-white text-[var(--content-primary)] shadow-sm" : "text-[#5e6975]"}`}
          >
            Источники · {usedSources.length}
          </button>
          <button
            type="button"
            onClick={() => setTab("materials")}
            className={`flex-1 rounded-[9px] text-xs font-medium transition-[background-color,color,box-shadow] duration-200 ${tab === "materials" ? "bg-white text-[var(--content-primary)] shadow-sm" : "text-[#5e6975]"}`}
          >
            Материалы
          </button>
        </div>

        {tab === "sources" && (
          <>
            <p className="text-xs font-semibold tracking-[0.6px] text-[#7d8796]">ИСПОЛЬЗОВАНО В ОТВЕТЕ</p>
            {usedSources.length === 0 && <p className="text-xs text-[var(--content-tertiary)]">Пока нет источников для этого ответа.</p>}
            {usedSources.map((source) => (
              <div key={source.document_id} className="flex h-16 items-center gap-2.5 rounded-xl border border-[#f5c7cc] bg-[#fff6f7] px-3 py-2.5">
                <FileTextIcon className="size-[18px] shrink-0" />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-xs font-semibold text-[var(--content-primary)]">{source.title}</p>
                  <p className="text-xs text-[#7d8796]">{source.page ? `стр. ${source.page}` : source.version}</p>
                </div>
                <a href={`#source-${source.document_id}`} onClick={(event) => event.preventDefault()}>
                  <ExternalLinkIcon className="size-4" />
                </a>
              </div>
            ))}
          </>
        )}

        {tab === "materials" && (
          <p className="text-xs text-[var(--content-tertiary)]">
            Библиотека материалов появится здесь, когда backend будет отдавать соответствующий список (вне текущего контракта web-api-v0).
          </p>
        )}
      </div>
    </aside>
  );
}
