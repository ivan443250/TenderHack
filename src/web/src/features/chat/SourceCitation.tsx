import { useState } from "react";

import type { ApiClient } from "../../api/client";
import type { SourceDetail } from "../../api/types";
import { ExternalLinkIcon, FileTextIcon } from "../../design-system/icons";

type SourceCitationProps = {
  fragmentId: string;
  /** From the answer payload (web-api-v0.md §4.3) — no loading flicker before this renders. */
  title: string;
  page: number | null;
  api: ApiClient;
  onOpen: (source: SourceDetail) => void;
};

/**
 * Chat/Source Citation (Figma 199:321). Title/page now ride on the answer payload itself
 * (web-api-v0.md §4.3); the full `SourceDetail` (text/anchor/document_id) is still resolved via
 * `GET /sources/{fragment_id}` — but only lazily, on click, not eagerly for every citation shown.
 */
export function SourceCitation({ fragmentId, title, page, api, onOpen }: SourceCitationProps) {
  const [loading, setLoading] = useState(false);

  async function handleClick() {
    setLoading(true);
    try {
      const source = await api.getSource(fragmentId);
      onOpen(source);
    } catch {
      // Swallowed: the citation stays visible and clickable for a retry; there is nothing else to show here.
    } finally {
      setLoading(false);
    }
  }

  return (
    <button
      type="button"
      disabled={loading}
      onClick={handleClick}
      className="flex h-[60px] w-full max-w-[512px] items-center justify-between rounded-[18px] border border-[var(--border-default)] bg-white px-3 py-2.5 text-left disabled:opacity-60"
    >
      <div className="flex min-w-0 flex-1 items-center gap-2.5">
        <div className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-[#fff6f7]">
          <FileTextIcon className="size-5" />
        </div>
        <div className="min-w-0 flex-1">
          <p className="truncate text-xs font-medium text-[var(--content-primary)]">{title}</p>
          {page !== null && <p className="truncate text-[11px] text-[var(--content-tertiary)]">{`стр. ${page}`}</p>}
        </div>
      </div>
      <ExternalLinkIcon className="size-5 shrink-0 text-[var(--content-primary)]" />
    </button>
  );
}
