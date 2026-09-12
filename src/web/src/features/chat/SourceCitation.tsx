import { useEffect, useState } from "react";

import type { ApiClient } from "../../api/client";
import type { SourceDetail } from "../../api/types";
import { ExternalLinkIcon, FileTextIcon } from "../../design-system/icons";

type SourceCitationProps = {
  fragmentId: string;
  api: ApiClient;
  onOpen: (source: SourceDetail) => void;
};

/** Chat/Source Citation (Figma 199:321). Title/page are not on the answer payload — resolved via GET /sources/{fragment_id}. */
export function SourceCitation({ fragmentId, api, onOpen }: SourceCitationProps) {
  const [source, setSource] = useState<SourceDetail | null>(null);

  useEffect(() => {
    let cancelled = false;
    api.getSource(fragmentId).then((result) => {
      if (!cancelled) setSource(result);
    }).catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [api, fragmentId]);

  return (
    <button
      type="button"
      disabled={!source}
      onClick={() => source && onOpen(source)}
      className="flex h-[60px] w-full max-w-[512px] items-center justify-between rounded-[18px] border border-[var(--border-default)] bg-white px-3 py-2.5 text-left disabled:opacity-60"
    >
      <div className="flex min-w-0 flex-1 items-center gap-2.5">
        <div className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-[#fff6f7]">
          <FileTextIcon className="size-5" />
        </div>
        <div className="min-w-0 flex-1">
          <p className="truncate text-xs font-medium text-[var(--content-primary)]">{source?.title ?? "Загрузка источника…"}</p>
          {source && (
            <p className="truncate text-[11px] text-[var(--content-tertiary)]">
              {source.page ? `стр. ${source.page}` : source.version}
            </p>
          )}
        </div>
      </div>
      <ExternalLinkIcon className="size-5 shrink-0 text-[var(--content-primary)]" />
    </button>
  );
}
