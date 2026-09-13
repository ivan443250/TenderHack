import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { CaseSnapshot } from "../../api/types";
import { deriveRequestStage, RequestStatusStepper } from "./RequestStatusStepper";

function snapshot(overrides: Partial<CaseSnapshot>): CaseSnapshot {
  return {
    case_id: "case-1",
    conversation_status: "ACTIVE",
    resolution_status: "UNKNOWN",
    last_decision: null,
    moderation_warning_count: 0,
    active_turn: null,
    handoff: null,
    timeline: [],
    last_event_id: "0",
    completed_at: null,
    completion_reason: null,
    feedback: null,
    ...overrides
  };
}

describe("deriveRequestStage", () => {
  it("is visible only while a turn is being processed", () => {
    const processing = snapshot({ active_turn: { turn_id: "t1", revision: 1, status: "QUEUED" } });

    render(<RequestStatusStepper snapshot={processing} />);
    expect(screen.getByText("Этап 1 из 4")).toBeInTheDocument();
    expect(deriveRequestStage(processing)).toBe(1);
  });

  it("is hidden after an ANSWER", () => {
    const answered = snapshot({
      last_decision: "ANSWER",
      active_turn: { turn_id: "t1", revision: 1, status: "COMPLETED" }
    });

    const { container } = render(<RequestStatusStepper snapshot={answered} />);
    expect(container).toBeEmptyDOMElement();
    expect(deriveRequestStage(answered)).toBeNull();
  });

  it("is hidden after a CLARIFY", () => {
    const clarified = snapshot({
      last_decision: "CLARIFY",
      active_turn: { turn_id: "t1", revision: 1, status: "COMPLETED" }
    });

    const { container } = render(<RequestStatusStepper snapshot={clarified} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("is hidden for a failed turn rather than presenting it as progress", () => {
    const stage = deriveRequestStage(snapshot({ active_turn: { turn_id: "t1", revision: 1, status: "FAILED" } }));

    expect(stage).toBeNull();
  });
});
