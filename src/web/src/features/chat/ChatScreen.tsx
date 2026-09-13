import { useEffect, useMemo, useRef, useState } from "react";
import { useLocation, useNavigate, useParams } from "react-router-dom";

import type { ApiClient } from "../../api/client";
import type { AnswerSource, SourceDetail } from "../../api/types";
import { useCaseSession, type InitialCaseMessage } from "../../state/caseStore";
import { Composer } from "./Composer";
import { ModerationBlockedNotice } from "./Messages";
import { MessageList, shouldShowFeedback } from "./MessageList";
import { ContextPanel } from "./ContextPanel";
import { deriveRequestStage, RequestStatusStepper } from "./RequestStatusStepper";
import { SourceModal } from "./SourceModal";
import { IconButton } from "../../design-system/IconButton";
import { BookOpenIcon } from "../../design-system/icons";

export function ChatScreen({ api }: { api: ApiClient }) {
  const { caseId } = useParams<{ caseId: string }>();
  const location = useLocation();
  const navigate = useNavigate();
  const navigationState = useRef(
    location.state as { initialMessage?: InitialCaseMessage; initialSnapshot?: import("../../api/types").CaseSnapshot } | null
  ).current;
  const session = useCaseSession(caseId, api, navigationState?.initialMessage, navigationState?.initialSnapshot);
  const [panelOpen, setPanelOpen] = useState(true);
  const [openedSource, setOpenedSource] = useState<SourceDetail | null>(null);
  const [usedSources, setUsedSources] = useState<Record<string, SourceDetail>>({});

  const usedSourcesList = useMemo(() => Object.values(usedSources), [usedSources]);
  const answerSources = useMemo<AnswerSource[]>(() => {
    const byFragment = new Map<string, AnswerSource>();
    for (const item of session.snapshot?.timeline ?? []) {
      if (item.type !== "AI_ANSWER") continue;
      const sources = item.payload.sources;
      if (!Array.isArray(sources)) continue;
      for (const candidate of sources) {
        if (!candidate || typeof candidate !== "object") continue;
        const source = candidate as Partial<AnswerSource>;
        if (typeof source.fragment_id !== "string" || byFragment.has(source.fragment_id)) continue;
        byFragment.set(source.fragment_id, {
          fragment_id: source.fragment_id,
          title: typeof source.title === "string" && source.title.length > 0 ? source.title : "Источник",
          page: typeof source.page === "number" ? source.page : null,
          label: typeof source.label === "string" ? source.label : "Источник"
        });
      }
    }
    return [...byFragment.values()];
  }, [session.snapshot?.timeline]);
  const scrollRef = useRef<HTMLDivElement>(null);
  const timelineLength = session.snapshot?.timeline.length ?? 0;
  const feedbackDone = Boolean(session.snapshot?.feedback);
  const feedbackVisible = session.snapshot ? shouldShowFeedback(session.snapshot) : false;

  useEffect(() => {
    if (navigationState?.initialMessage) {
      navigate(location.pathname, { replace: true, state: null });
    }
  }, [location.pathname, navigate, navigationState]);

  useEffect(() => {
    const viewport = scrollRef.current;
    if (viewport && typeof viewport.scrollTo === "function") {
      viewport.scrollTo({ top: viewport.scrollHeight, behavior: "smooth" });
    }
  }, [timelineLength, feedbackDone, feedbackVisible, session.pendingInitialMessage]);

  if (session.loading || !session.snapshot) {
    return <div className="loading-dots flex flex-1 animate-fade-in items-center justify-center text-sm text-[var(--content-tertiary)]">Загрузка обращения</div>;
  }

  if (session.error) {
    return <div className="flex flex-1 items-center justify-center text-sm text-[var(--action-primary)]">{session.error}</div>;
  }

  const snapshot = session.snapshot;
  const requestStage = deriveRequestStage(snapshot);

  return (
    <div className="flex h-full flex-1">
      <div className="flex h-full flex-1 flex-col">
        {(requestStage !== null || !panelOpen) && <header className="flex h-16 shrink-0 items-center justify-between border-b border-[var(--border-default)] px-6">
          {requestStage !== null && <RequestStatusStepper snapshot={snapshot} />}
          {!panelOpen && (
            <IconButton icon={<BookOpenIcon className="size-[18px]" />} label="Источники и материалы" onClick={() => setPanelOpen(true)} />
          )}
        </header>}

        <div ref={scrollRef} className="flex-1 overflow-y-auto px-8 py-6 scroll-smooth">
          <MessageList
            snapshot={snapshot}
            optimisticMessage={session.pendingInitialMessage}
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
        {snapshot.conversation_status === "CLOSED_MODERATION" && (
          <div className="flex shrink-0 justify-center border-t border-[var(--border-default)] px-8 py-4">
            <ModerationBlockedNotice />
          </div>
        )}
      </div>

      {panelOpen && <ContextPanel api={api} usedSources={usedSourcesList} sourceRefs={answerSources} onClose={() => setPanelOpen(false)} />}
      {openedSource && <SourceModal source={openedSource} onClose={() => setOpenedSource(null)} />}
    </div>
  );
}
