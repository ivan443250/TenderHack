// Wire types 1:1 with docs/contracts/web-api-v0.md and
// src/support-core/src/TenderHack.Api/Contracts/CaseContracts.cs (snake_case on the wire,
// enums as SCREAMING_SNAKE_CASE strings). Frontend must not invent fields or states beyond this.

export type ConversationStatus = "ACTIVE" | "CLOSED_USER" | "CLOSED_SUPPORT" | "CLOSED_MODERATION";
export type ResolutionStatus = "UNKNOWN" | "RESOLVED" | "UNRESOLVED";
export type TurnStatus = "QUEUED" | "RUNNING" | "COMPLETED" | "FAILED" | "SUPERSEDED";
export type HandoffStatus = "NOT_REQUESTED" | "PENDING" | "ACCEPTED" | "SIMULATED_ACCEPTED" | "FAILED";
export type IntegrationMode = "REAL" | "SIMULATED";
export type Decision =
  | "ANSWER"
  | "CLARIFY"
  | "HANDOFF_OFFER"
  | "ANSWER_AND_HANDOFF"
  | "MODERATION_WARNING"
  | "MODERATION_CLOSE"
  | "TECHNICAL_ERROR";
export type CompletionReason = "USER" | "SUPPORT" | "MODERATION";
export type FeedbackRating = "POSITIVE" | "NEGATIVE";

export type TimelineItemType =
  | "USER_MESSAGE"
  | "TURN_STAGE"
  | "AI_ANSWER"
  | "CLARIFICATION"
  | "NO_CONFIRMED_ANSWER"
  | "MODERATION_WARNING"
  | "CONVERSATION_CLOSED"
  | "HANDOFF_OFFER"
  | "HANDOFF_STATUS"
  | "CASE_RESOLUTION_CHANGED"
  | "CASE_COMPLETED"
  | "TECHNICAL_ERROR"
  | "FEEDBACK_REQUESTED"
  | "FEEDBACK_SUBMITTED";

export type ActiveTurnView = {
  turn_id: string;
  revision: number;
  status: TurnStatus;
};

export type HandoffSpecialistView = {
  ref: string;
  display_name: string | null;
};

export type HandoffStageView = {
  code: string;
  display_name: string | null;
};

export type HandoffView = {
  status: HandoffStatus;
  integration_mode: IntegrationMode | null;
  external_case_id: string | null;
  assigned_specialist: HandoffSpecialistView | null;
  stage: HandoffStageView | null;
  terminal: unknown | null;
  stale: boolean;
  updated_at: string | null;
};

export type FeedbackView = {
  specialist_rating: FeedbackRating | null;
  information_quality_rating: FeedbackRating | null;
  solved: boolean | null;
  comment_text: string | null;
  submitted_at: string;
};

export type AnswerSource = {
  fragment_id: string;
  title: string;
  page: number | null;
  label: string;
};

export type AnswerPayload = {
  markdown: string;
  sources: AnswerSource[];
};

export type ContextSlotProvenance = "user_explicit" | "trusted_portal_context" | "inferred" | "unknown";

export type ApplicabilityEntity = {
  type: string;
  value: string;
  provenance: ContextSlotProvenance;
};

/** `AI_ANSWER.applicability` (web-api-v0.md §4.3, additive 2026-09-13) — facts that already passed
 * the Answerability Gate, never a re-derived confidence score. */
export type Applicability = {
  entities: ApplicabilityEntity[];
  missing_conditions: string[];
  questions: string[];
  risk_flags: string[];
  evidence_fragment_ids: string[];
};

export type TimelineItem = {
  item_id: string;
  type: TimelineItemType;
  occurred_at: string;
  turn_id: string | null;
  payload: Record<string, unknown>;
};

export type CaseSnapshot = {
  case_id: string;
  conversation_status: ConversationStatus;
  resolution_status: ResolutionStatus;
  last_decision: Decision | null;
  moderation_warning_count: number;
  active_turn: ActiveTurnView | null;
  handoff: HandoffView | null;
  timeline: TimelineItem[];
  last_event_id: string;
  completed_at: string | null;
  completion_reason: CompletionReason | null;
  feedback: FeedbackView | null;
};

export type CaseListItem = {
  case_id: string;
  conversation_status: ConversationStatus;
  resolution_status: ResolutionStatus;
  last_activity_at: string;
  unread_notifications: number;
};

export type CaseEvent = {
  event_id: string;
  case_id: string;
  turn_id: string | null;
  revision: number | null;
  type: TimelineItemType;
  occurred_at: string;
  payload: Record<string, unknown>;
};

export type SendMessageResponse = {
  case_id: string;
  turn_id: string;
  revision: number;
  status: TurnStatus;
  decision: Decision;
  answer: AnswerPayload | null;
  missing_conditions: string[] | null;
  failure_category: string | null;
};

export type SourceDetail = {
  document_id: string;
  title: string;
  version: string;
  page: number | null;
  anchor: string | null;
  text: string;
  snapshot_id: string;
};

export type SubmitFeedbackRequest = {
  specialist_rating: FeedbackRating | null;
  information_quality_rating: FeedbackRating | null;
  solved: boolean | null;
  comment_text: string | null;
};

export type NotificationType = "HANDOFF_UPDATED" | "CASE_COMPLETED" | "FEEDBACK_REQUESTED";

export type Notification = {
  notification_id: string;
  case_id: string;
  type: NotificationType;
  occurred_at: string;
  read_at: string | null;
  payload: { title: string; body: string; integration_mode: IntegrationMode | null };
};

export type Material = {
  document_id: string;
  title: string;
  declared_version: string | null;
  declared_date: string | null;
  page_count: number;
  fragment_count: number;
};

export type MaterialsResponse = {
  snapshot_id: string;
  materials: Material[];
};

export type MaterialSection = {
  section: string | null;
  page_start: number;
  page_end: number;
  first_fragment_id: string;
};

export type MaterialSectionsResponse = {
  snapshot_id: string;
  document_id: string;
  sections: MaterialSection[];
};

export type ApiErrorBody = {
  code?: string;
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
};
