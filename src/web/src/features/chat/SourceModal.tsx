import type { SourceDetail } from "../../api/types";

/** Shared source-drawer modal (web-api-v0.md §9's "Открыть источник"): used both from an
 * `AI_ANSWER`'s citations (ChatScreen) and from the "Материалы" tab's table of contents
 * (ContextPanel, E3) — one modal, one place that renders a resolved `SourceDetail`. */
export function SourceModal({ source, onClose }: { source: SourceDetail; onClose: () => void }) {
  return (
    <div className="fixed inset-0 z-50 flex animate-fade-in items-center justify-center bg-black/30 p-6 backdrop-blur-[2px]" onClick={onClose}>
      <div
        className="max-h-[80vh] w-full max-w-xl animate-pop-in overflow-y-auto rounded-2xl bg-white p-6 shadow-2xl"
        onClick={(event) => event.stopPropagation()}
      >
        <p className="text-sm font-semibold text-[var(--content-primary)]">{source.title}</p>
        <p className="mt-1 text-xs text-[var(--content-tertiary)]">
          {source.version}
          {source.page ? ` · стр. ${source.page}` : ""}
        </p>
        <p className="mt-3 whitespace-pre-wrap text-sm text-[var(--content-primary)]">{source.text}</p>
        <button type="button" onClick={onClose} className="mt-5 text-sm font-medium text-[var(--action-primary)] transition-opacity hover:opacity-80">
          Закрыть
        </button>
      </div>
    </div>
  );
}
