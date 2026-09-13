import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { ApiClient } from "../../api/client";
import type { CaseSnapshot, Decision, TurnStatus } from "../../api/types";
import { MessageList } from "./MessageList";

afterEach(cleanup);

function snapshot(decision: Decision, status: TurnStatus): CaseSnapshot {
  const resultType = decision === "ANSWER" ? "AI_ANSWER" : "CLARIFICATION";
  return {
    case_id: "case-1",
    conversation_status: "ACTIVE",
    resolution_status: "UNKNOWN",
    last_decision: decision,
    moderation_warning_count: 0,
    active_turn: { turn_id: "turn-1", revision: 1, status },
    handoff: null,
    timeline: [
      {
        item_id: "event-1",
        type: resultType,
        occurred_at: new Date().toISOString(),
        turn_id: "turn-1",
        payload: resultType === "AI_ANSWER" ? { markdown: "Готовый ответ", sources: [] } : { questions: ["Уточните роль"] }
      }
    ],
    last_event_id: "1",
    completed_at: null,
    completion_reason: null,
    feedback: null
  };
}

function renderMessages(value: CaseSnapshot, onComplete = vi.fn(), onSubmitFeedback = vi.fn()) {
  render(
    <MessageList
      snapshot={value}
      api={{} as ApiClient}
      onOpenSource={vi.fn()}
      onPrepareHandoff={vi.fn()}
      onConfirmHandoff={vi.fn()}
      onRetryHandoff={vi.fn()}
      onComplete={onComplete}
      onSubmitFeedback={onSubmitFeedback}
    />
  );
}

describe("answer feedback placement", () => {
  it("shows feedback immediately after a completed ANSWER", () => {
    renderMessages(snapshot("ANSWER", "COMPLETED"));
    expect(screen.getByText("Вопрос решён?")).toBeInTheDocument();
  });

  it("does not show feedback while a new turn is processing", () => {
    renderMessages(snapshot("ANSWER", "QUEUED"));
    expect(screen.queryByText("Вопрос решён?")).not.toBeInTheDocument();
  });

  it("does not show feedback after CLARIFY", () => {
    renderMessages(snapshot("CLARIFY", "COMPLETED"));
    expect(screen.queryByText("Вопрос решён?")).not.toBeInTheDocument();
  });

  it("completes an unresolved case before submitting feedback", async () => {
    const calls: string[] = [];
    const onComplete = vi.fn(async () => {
      calls.push("complete");
    });
    const onSubmitFeedback = vi.fn(async () => {
      calls.push("feedback");
    });
    renderMessages(snapshot("ANSWER", "COMPLETED"), onComplete, onSubmitFeedback);

    fireEvent.click(screen.getByRole("button", { name: "Решён" }));
    fireEvent.click(screen.getByRole("button", { name: "Полезно" }));
    fireEvent.click(screen.getByRole("button", { name: "Отправить" }));

    await waitFor(() => expect(onSubmitFeedback).toHaveBeenCalledTimes(1));
    expect(onComplete).toHaveBeenCalledWith(true);
    expect(calls).toEqual(["complete", "feedback"]);
  });
});
