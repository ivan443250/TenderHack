import { describe, expect, it } from "vitest";

import type { CaseSnapshot } from "../../api/types";
import { deriveRequestStage, FAILED_STAGE } from "./RequestStatusStepper";

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
  it("returns the FAILED sentinel for a failed turn instead of step 1 (B6)", () => {
    const stage = deriveRequestStage(snapshot({ active_turn: { turn_id: "t1", revision: 1, status: "FAILED" } }));

    expect(stage).toBe(FAILED_STAGE);
    expect(stage).not.toBe(1);
  });

  it("still returns step 1 for a freshly queued turn", () => {
    const stage = deriveRequestStage(snapshot({ active_turn: { turn_id: "t1", revision: 1, status: "QUEUED" } }));

    expect(stage).toBe(1);
  });
});
