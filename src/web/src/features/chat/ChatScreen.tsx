import { useMemo, useState } from "react";
import { useParams } from "react-router-dom";

import type { ApiClient } from "../../api/client";
import type { SourceDetail } from "../../api/types";
import { useCaseSession } from "../../state/caseStore";
import { Composer } from "./Composer";
import { MessageList } from "./MessageList";
import { ContextPanel } from "./ContextPanel";
import { RequestStatusStepper } from "./RequestStatusStepper";
import { IconButton } from "../../design-system/IconButton";
import { BookOpenIcon } from "../../design-system/icons";

function SourceModal({ source, onClose }: { source: SourceDetail; onClose: () => void }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 p-6" onClick={onClose}>
      <div
        className="max-h-[80vh] w-full max-w-lg overflow-y-auto rounded-2xl bg-white p-5"
        onClick={(event) => event.stopPropagation()}
      >
        <p className="text-sm font-semibold text-[var(--content-primary)]">{source.title}</p>
        <p className="mt-1 text-xs text-[var(--content-tertiary)]">
          {source.version}
          {source.page ? ` · стр. ${source.page}` : ""}
        </p>
        <p className="mt-3 whitespace-pre-wrap text-sm text-[var(--content-primary)]">{source.text}</p>
        <button type="button" onClick={onClose} className="mt-4 text-xs font-medium text-[var(--action-primary)]">
          Закрыть
        </button>
      </div>
    </div>
  );
}

export function ChatScreen({ api }: { api: ApiClient }) {
  const { caseId } = useParams<{ caseId: string }>();
  const session = useCaseSession(caseId, api);
  const [panelOpen, setPanelOpen] = useState(true);
  const [openedSource, setOpenedSource] = useState<SourceDetail | null>(null);
  const [usedSources, setUsedSources] = useState<Record<string, SourceDetail>>({});

  const usedSourcesList = useMemo(() => Object.values(usedSources), [usedSources]);

  if (session.loading || !session.snapshot) {
    return <div className="flex flex-1 items-center justify-center text-sm text-[var(--content-tertiary)]">Загрузка обращения…</div>;
  }

  if (session.error) {
    return <div className="flex flex-1 items-center justify-center text-sm text-[var(--action-primary)]">{session.error}</div>;
  }

  const snapshot = session.snapshot;

  return (
    <div className="flex h-full flex-1">
      <div className="flex h-full flex-1 flex-col">
        <header className="flex h-14 shrink-0 items-center justify-between border-b border-[var(--border-default)] px-6">
          <RequestStatusStepper snapshot={snapshot} />
          {!panelOpen && (
            <IconButton icon={<BookOpenIcon className="size-[18px]" />} label="Источники и материалы" onClick={() => setPanelOpen(true)} />
          )}
        </header>

        <div className="flex-1 overflow-y-auto px-8 py-6">
          <MessageList
            snapshot={snapshot}
            api={api}
            onOpenSource={(source) => {
              setOpenedSource(source);
              setUsedSources((prev) => ({ ...prev, [source.document_id]: source }));
            }}
            onPrepareHandoff={session.prepareHandoff}
            onConfirmHandoff={session.confirmHandoff}
            onRetryHandoff={session.retryHandoff}
            onComplete={session.complete}
            onSubmitFeedback={session.submitFeedback}
          />
        </div>

        {snapshot.conversation_status === "ACTIVE" && (
          <div className="flex shrink-0 justify-center border-t border-[var(--border-default)] px-8 py-4">
            <Composer context="conversation" onSubmit={session.sendMessage} />
          </div>
        )}
      </div>

      {panelOpen && <ContextPanel usedSources={usedSourcesList} onClose={() => setPanelOpen(false)} />}
      {openedSource && <SourceModal source={openedSource} onClose={() => setOpenedSource(null)} />}
    </div>
  );
}
