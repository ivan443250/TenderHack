import { useMemo, useState } from "react";

import { ChevronRightIcon, FileTextIcon } from "../../design-system/icons";
import type { ApiClient } from "../../api/client";
import { ApiError } from "../../api/client";
import type { AnswerSource, Material, MaterialSection, SourceDetail } from "../../api/types";
import { SourceModal } from "./SourceModal";

type ContextPanelProps = {
  api: ApiClient;
  usedSources: SourceDetail[];
  sourceRefs?: AnswerSource[];
  onClose: () => void;
};

type SourceItem = {
  key: string;
  title: string;
  page: number | null;
  version: string | null;
  fragmentId: string | null;
  detail: SourceDetail | null;
};

type MaterialsState =
  | { status: "idle" }
  | { status: "loading" }
  | { status: "error"; message: string }
  | { status: "loaded"; materials: Material[] };

type SectionsState =
  | { status: "loading" }
  | { status: "error"; message: string }
  | { status: "loaded"; sections: MaterialSection[] };

function MaterialsUnavailableMessage(): string {
  // web-api-v0.md §15: any failed knowledge call surfaces as 503 KNOWLEDGE_UNAVAILABLE.
  return "Библиотека временно недоступна.";
}

/** Chat/Context Panel (Figma 258:743). "Материалы" (E3, docs/plans/active/2026-09-demo-readiness.md)
 * lists the current normative snapshot's documents, expands to each one's table of contents, and
 * opens a section through the same `GET /sources/{fragment_id}` drawer citations already use. */
export function ContextPanel({ api, usedSources, sourceRefs = [], onClose }: ContextPanelProps) {
  const [tab, setTab] = useState<"sources" | "materials">("sources");
  const [materials, setMaterials] = useState<MaterialsState>({ status: "idle" });
  const [expanded, setExpanded] = useState<Record<string, SectionsState>>({});
  const [openedSource, setOpenedSource] = useState<SourceDetail | null>(null);
  const sourceItems = useMemo<SourceItem[]>(() => {
    if (sourceRefs.length > 0) {
      return sourceRefs.map((source) => ({
        key: source.fragment_id,
        title: source.title || "Источник",
        page: source.page,
        version: null,
        fragmentId: source.fragment_id,
        detail: null
      }));
    }
    return usedSources.map((source, index) => ({
      key: `${source.document_id}-${source.page ?? "unknown"}-${index}`,
      title: source.title,
      page: source.page,
      version: source.version,
      fragmentId: null,
      detail: source
    }));
  }, [sourceRefs, usedSources]);

  async function openMaterialsTab() {
    setTab("materials");
    if (materials.status !== "idle") return;
    setMaterials({ status: "loading" });
    try {
      const response = await api.listMaterials();
      setMaterials({ status: "loaded", materials: response.materials });
    } catch (error) {
      const message = error instanceof ApiError && error.status === 503 ? MaterialsUnavailableMessage() : "Не удалось загрузить материалы.";
      setMaterials({ status: "error", message });
    }
  }

  async function toggleDocument(documentId: string) {
    if (documentId in expanded) {
      setExpanded((prev) => {
        const next = { ...prev };
        delete next[documentId];
        return next;
      });
      return;
    }
    setExpanded((prev) => ({ ...prev, [documentId]: { status: "loading" } }));
    try {
      const response = await api.listMaterialSections(documentId);
      setExpanded((prev) => ({ ...prev, [documentId]: { status: "loaded", sections: response.sections } }));
    } catch (error) {
      const message = error instanceof ApiError && error.status === 503 ? MaterialsUnavailableMessage() : "Не удалось загрузить оглавление.";
      setExpanded((prev) => ({ ...prev, [documentId]: { status: "error", message } }));
    }
  }

  async function openSection(section: MaterialSection) {
    try {
      const source = await api.getSource(section.first_fragment_id);
      setOpenedSource(source);
    } catch {
      // Swallowed: the section stays visible and clickable for a retry.
    }
  }

  async function openAnswerSource(fragmentId: string) {
    try {
      setOpenedSource(await api.getSource(fragmentId));
    } catch {
      // Keep the citation visible so the user can retry after a transient source error.
    }
  }

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
            Источники · {sourceItems.length}
          </button>
          <button
            type="button"
            onClick={() => void openMaterialsTab()}
            className={`flex-1 rounded-[9px] text-xs font-medium transition-[background-color,color,box-shadow] duration-200 ${tab === "materials" ? "bg-white text-[var(--content-primary)] shadow-sm" : "text-[#5e6975]"}`}
          >
            Материалы
          </button>
        </div>

        {tab === "sources" && (
          <>
            <p className="text-xs font-semibold tracking-[0.6px] text-[#7d8796]">ИСПОЛЬЗОВАНО В ОТВЕТЕ</p>
            {sourceItems.length === 0 && <p className="text-xs text-[var(--content-tertiary)]">Пока нет источников для этого ответа.</p>}
            {sourceItems.map((source) => (
              <button
                key={source.key}
                type="button"
                onClick={() => source.fragmentId ? void openAnswerSource(source.fragmentId) : source.detail && setOpenedSource(source.detail)}
                className="flex min-h-16 w-full items-center gap-2.5 rounded-xl border border-[#f5c7cc] bg-[#fff6f7] px-3 py-2.5 text-left transition-[background-color] duration-150 hover:bg-[#fff0f1]"
              >
                <FileTextIcon className="size-[18px] shrink-0" />
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-xs font-semibold text-[var(--content-primary)]">{source.title}</span>
                  <span className="block text-xs text-[#7d8796]">{source.page ? `стр. ${source.page}` : source.version ?? "Источник"}</span>
                </span>
              </button>
            ))}
          </>
        )}

        {tab === "materials" && materials.status === "loading" && (
          <p className="loading-dots text-xs text-[var(--content-tertiary)]">Загружаем материалы</p>
        )}
        {tab === "materials" && materials.status === "error" && (
          <p className="text-xs text-[var(--action-primary)]">{materials.message}</p>
        )}
        {tab === "materials" && materials.status === "loaded" && materials.materials.length === 0 && (
          <p className="text-xs text-[var(--content-tertiary)]">В текущем снапшоте нет документов.</p>
        )}
        {tab === "materials" && materials.status === "loaded" && (
          <div className="flex flex-col gap-1.5">
            {materials.materials.map((material) => {
              const sectionsState = expanded[material.document_id];
              return (
                <div key={material.document_id} className="rounded-xl border border-[var(--border-default)]">
                  <button
                    type="button"
                    onClick={() => void toggleDocument(material.document_id)}
                    className="flex w-full items-center gap-2.5 px-3 py-2.5 text-left transition-[background-color] duration-150 hover:bg-[var(--surface-subtle)]"
                  >
                    <FileTextIcon className="size-[18px] shrink-0" />
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-xs font-semibold text-[var(--content-primary)]">{material.title}</p>
                      <p className="text-xs text-[#7d8796]">
                        {material.declared_version ? `v${material.declared_version} · ` : ""}
                        {material.page_count} стр.
                      </p>
                    </div>
                    <ChevronRightIcon className={`size-4 shrink-0 transition-transform duration-150 ${sectionsState ? "rotate-90" : ""}`} />
                  </button>

                  {sectionsState?.status === "loading" && (
                    <p className="loading-dots px-3 pb-2.5 text-xs text-[var(--content-tertiary)]">Загружаем оглавление</p>
                  )}
                  {sectionsState?.status === "error" && <p className="px-3 pb-2.5 text-xs text-[var(--action-primary)]">{sectionsState.message}</p>}
                  {sectionsState?.status === "loaded" && (
                    <div className="flex flex-col gap-0.5 px-2 pb-2">
                      {sectionsState.sections.map((section) => (
                        <button
                          key={section.first_fragment_id}
                          type="button"
                          onClick={() => void openSection(section)}
                          className="flex items-center justify-between rounded-lg px-2 py-1.5 text-left text-xs text-[var(--content-secondary)] transition-[background-color] duration-150 hover:bg-[var(--surface-subtle)]"
                        >
                          <span className="min-w-0 flex-1 truncate">{section.section ?? "Без раздела"}</span>
                          <span className="shrink-0 text-[var(--content-tertiary)]">{`стр. ${section.page_start}`}</span>
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>

      {openedSource && <SourceModal source={openedSource} onClose={() => setOpenedSource(null)} />}
    </aside>
  );
}
