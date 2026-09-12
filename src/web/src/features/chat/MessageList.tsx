import type { ApiClient } from "../../api/client";
import type { AnswerSource, CaseSnapshot, SourceDetail, TimelineItem } from "../../api/types";
import {
  AssistantResponse,
  ClarificationNotice,
  ConversationClosedNotice,
  ModerationWarningNotice,
  NoConfirmedAnswerNotice,
  TechnicalErrorNotice,
  TurnStageNotice,
  UserMessage
} from "./Messages";
import { SourceCitation } from "./SourceCitation";
import { HandoffCard } from "./HandoffCard";
import { ResolutionFeedback } from "./ResolutionFeedback";

type MessageListProps = {
  snapshot: CaseSnapshot;
  api: ApiClient;
  onOpenSource: (source: SourceDetail) => void;
  onPrepareHandoff: () => Promise<void>;
  onConfirmHandoff: (summary: string) => Promise<void>;
  onRetryHandoff: (summary: string) => Promise<void>;
  onComplete: (solved: boolean | null) => Promise<void>;
  onSubmitFeedback: Parameters<typeof ResolutionFeedback>[0]["onSubmitFeedback"];
};

function renderItem(item: TimelineItem, snapshot: CaseSnapshot, api: ApiClient, onOpenSource: (s: SourceDetail) => void) {
  const payload = item.payload as Record<string, unknown>;

  switch (item.type) {
    case "USER_MESSAGE":
      return <UserMessage key={item.item_id} text={String(payload.text ?? "")} />;

    case "TURN_STAGE":
      return <TurnStageNotice key={item.item_id} stage={String(payload.stage ?? "")} />;

    case "AI_ANSWER": {
      // web-api-v0.md §4.3: sources carry fragment_id/title/page/label.
      const sources = Array.isArray(payload.sources) ? (payload.sources as AnswerSource[]) : [];
      return (
        <div key={item.item_id} className="flex w-full flex-col items-start gap-3">
          <AssistantResponse markdown={String(payload.markdown ?? "")} />
          {sources.map((source) => (
            <SourceCitation
              key={source.fragment_id}
              fragmentId={source.fragment_id}
              title={source.title}
              page={source.page}
              api={api}
              onOpen={onOpenSource}
            />
          ))}
        </div>
      );
    }

    case "CLARIFICATION":
      return (
        <ClarificationNotice
          key={item.item_id}
          missingConditions={Array.isArray(payload.missing_conditions) ? (payload.missing_conditions as string[]) : []}
        />
      );

    case "NO_CONFIRMED_ANSWER":
      return <NoConfirmedAnswerNotice key={item.item_id} />;

    case "MODERATION_WARNING":
      return <ModerationWarningNotice key={item.item_id} count={Number(payload.moderation_warning_count ?? snapshot.moderation_warning_count)} />;

    case "CONVERSATION_CLOSED":
      return (
        <ConversationClosedNotice
          key={item.item_id}
          reason={snapshot.completion_reason === "MODERATION" ? "moderation" : snapshot.completion_reason === "SUPPORT" ? "support" : "user"}
        />
      );

    case "TECHNICAL_ERROR":
      return <TechnicalErrorNotice key={item.item_id} />;

    default:
      // HANDOFF_OFFER / HANDOFF_STATUS / CASE_RESOLUTION_CHANGED / CASE_COMPLETED / FEEDBACK_REQUESTED /
      // FEEDBACK_SUBMITTED render through the always-on HandoffCard/ResolutionFeedback widgets below,
      // driven off `snapshot` directly rather than the individual event — see MessageList.
      return null;
  }
}

export function MessageList({
  snapshot,
  api,
  onOpenSource,
  onPrepareHandoff,
  onConfirmHandoff,
  onRetryHandoff,
  onComplete,
  onSubmitFeedback
}: MessageListProps) {
  const showHandoff = snapshot.last_decision === "HANDOFF_OFFER" || snapshot.last_decision === "ANSWER_AND_HANDOFF" || snapshot.handoff;
  const showFeedback = snapshot.completed_at !== null;

  return (
    <div className="flex w-full flex-col items-start gap-4">
      {snapshot.timeline.map((item) => renderItem(item, snapshot, api, onOpenSource))}

      {showHandoff && (
        <HandoffCard handoff={snapshot.handoff} onPrepare={onPrepareHandoff} onConfirm={onConfirmHandoff} onRetry={onRetryHandoff} />
      )}

      {showFeedback && <ResolutionFeedback snapshot={snapshot} onComplete={onComplete} onSubmitFeedback={onSubmitFeedback} />}
    </div>
  );
}
