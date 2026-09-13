import type { ApiClient } from "../../api/client";
import type { Applicability, AnswerSource, CaseSnapshot, SourceDetail, TimelineItem } from "../../api/types";
import {
  AssistantResponse,
  ClarificationNotice,
  ConversationClosedNotice,
  ModerationWarningNotice,
  NoConfirmedAnswerNotice,
  OutOfScopeNotice,
  TechnicalErrorNotice,
  TurnStageNotice,
  UserMessage
} from "./Messages";
import { ApplicabilityCard } from "./ApplicabilityCard";
import { SourceCitation } from "./SourceCitation";
import { HandoffCard } from "./HandoffCard";
import { ResolutionFeedback } from "./ResolutionFeedback";

type MessageListProps = {
  snapshot: CaseSnapshot;
  optimisticMessage?: string | null;
  api: ApiClient;
  onOpenSource: (source: SourceDetail) => void;
  onPrepareHandoff: () => Promise<void>;
  onConfirmHandoff: (summary: string) => Promise<void>;
  onRetryHandoff: (summary: string) => Promise<void>;
  onComplete: (solved: boolean | null) => Promise<void>;
  onSubmitFeedback: Parameters<typeof ResolutionFeedback>[0]["onSubmitFeedback"];
};

function isProcessing(snapshot: CaseSnapshot): boolean {
  return snapshot.active_turn?.status === "QUEUED" || snapshot.active_turn?.status === "RUNNING";
}

export function shouldShowFeedback(snapshot: CaseSnapshot): boolean {
  if (isProcessing(snapshot)) return false;
  const completedWithoutModeration = snapshot.completed_at !== null && snapshot.completion_reason !== "MODERATION";
  return snapshot.last_decision === "ANSWER" || completedWithoutModeration;
}

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
      const applicability = payload.applicability as Applicability | undefined;
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
          {applicability && <ApplicabilityCard applicability={applicability} api={api} onOpenSource={onOpenSource} />}
        </div>
      );
    }

    case "CLARIFICATION":
      return (
        <ClarificationNotice
          key={item.item_id}
          missingConditions={Array.isArray(payload.missing_conditions) ? (payload.missing_conditions as string[]) : []}
          questions={Array.isArray(payload.questions) ? (payload.questions as string[]) : undefined}
        />
      );

    case "OUT_OF_SCOPE":
      return <OutOfScopeNotice key={item.item_id} message={String(payload.message ?? "Я помогаю с вопросами по работе Портала поставщиков.")} />;

    case "NO_CONFIRMED_ANSWER":
      return <NoConfirmedAnswerNotice key={item.item_id} reason={typeof payload.reason === "string" ? payload.reason : undefined} />;

    case "MODERATION_WARNING":
      return <ModerationWarningNotice key={item.item_id} message={String(payload.message ?? "")} />;

    case "CONVERSATION_CLOSED":
      // A moderation close is shown by the ModerationBlockedNotice in the composer slot (ChatScreen),
      // not repeated in the timeline.
      if (snapshot.completion_reason === "MODERATION") return null;
      return <ConversationClosedNotice key={item.item_id} reason={snapshot.completion_reason === "SUPPORT" ? "support" : "user"} />;

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
  optimisticMessage,
  api,
  onOpenSource,
  onPrepareHandoff,
  onConfirmHandoff,
  onRetryHandoff,
  onComplete,
  onSubmitFeedback
}: MessageListProps) {
  const showHandoff = snapshot.last_decision === "HANDOFF_OFFER" || snapshot.last_decision === "ANSWER_AND_HANDOFF" || snapshot.handoff;
  // B5/architecture.md §5.8: a moderation close never asks for feedback — the server never
  // publishes FEEDBACK_REQUESTED for it, and the UI must not fabricate the ask on its own just
  // because completed_at is set.
  const showFeedback = shouldShowFeedback(snapshot);
  const hasPersistedInitialMessage = optimisticMessage
    ? snapshot.timeline.some((item) => item.type === "USER_MESSAGE" && item.payload.text === optimisticMessage)
    : false;

  return (
    <div className="flex w-full flex-col items-start gap-4">
      {optimisticMessage && !hasPersistedInitialMessage && <UserMessage text={optimisticMessage} />}
      {optimisticMessage && !hasPersistedInitialMessage && <TurnStageNotice stage="Отправляем запрос" />}
      {snapshot.timeline.map((item) => renderItem(item, snapshot, api, onOpenSource))}

      {showHandoff && (
        <HandoffCard handoff={snapshot.handoff} onPrepare={onPrepareHandoff} onConfirm={onConfirmHandoff} onRetry={onRetryHandoff} />
      )}

      {showFeedback && <ResolutionFeedback snapshot={snapshot} onComplete={onComplete} onSubmitFeedback={onSubmitFeedback} />}
    </div>
  );
}
