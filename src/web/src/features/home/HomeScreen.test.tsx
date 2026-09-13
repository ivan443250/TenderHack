import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { StrictMode } from "react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { ApiClient } from "../../api/client";
import type { CaseSnapshot } from "../../api/types";
import { ChatScreen } from "../chat/ChatScreen";
import { HomeScreen } from "./HomeScreen";
import { MORE_QUESTIONS, QUESTION_CHIPS } from "./popularQuestions";

vi.mock("../../api/sse", () => ({
  openCaseEventStream: () => ({ close: vi.fn(), onerror: null })
}));
vi.mock("./InteractiveGlow", () => ({ InteractiveGlow: () => null }));

afterEach(cleanup);

function stubApiClient(): ApiClient {
  const emptyCase: CaseSnapshot = {
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
    feedback: null
  };
  return {
    createCase: vi.fn().mockResolvedValue(emptyCase),
    getCase: vi.fn().mockResolvedValue(emptyCase),
    sendMessage: vi.fn().mockResolvedValue(undefined)
  } as unknown as ApiClient;
}

function renderHome(api: ApiClient) {
  return render(
    <StrictMode>
      <MemoryRouter>
        <Routes>
          <Route index element={<HomeScreen api={api} />} />
          <Route path="cases/:caseId" element={<ChatScreen api={api} />} />
        </Routes>
      </MemoryRouter>
    </StrictMode>
  );
}

describe("HomeScreen action chips (E2)", () => {
  it("expands the 'more questions' list without creating a case", async () => {
    const api = stubApiClient();
    renderHome(api);

    fireEvent.click(screen.getByText("Больше популярных вопросов"));

    expect(api.createCase).not.toHaveBeenCalled();
    expect(screen.getByText(MORE_QUESTIONS[0])).toBeInTheDocument();
  });

  it("focuses the composer without creating a case for 'ещё один вопрос'", async () => {
    const api = stubApiClient();
    renderHome(api);

    fireEvent.click(screen.getByText("Еще один вопрос к поддержке"));

    expect(api.createCase).not.toHaveBeenCalled();
    expect(screen.getByPlaceholderText("Введите свой вопрос")).toHaveFocus();
  });

  it("navigates immediately for a quick question and sends it exactly once", async () => {
    const api = stubApiClient();
    (api.sendMessage as ReturnType<typeof vi.fn>).mockImplementation(() => new Promise(() => {}));
    renderHome(api);

    fireEvent.click(screen.getByText(QUESTION_CHIPS[0]));

    expect(await screen.findByText(QUESTION_CHIPS[0], { selector: "p" })).toBeInTheDocument();
    await waitFor(() => expect(api.sendMessage).toHaveBeenCalled());
    expect(api.createCase).toHaveBeenCalledTimes(1);
    expect(api.sendMessage).toHaveBeenCalledTimes(1);
    expect(api.sendMessage).toHaveBeenCalledWith("case-1", QUESTION_CHIPS[0], expect.any(String));
  });

  it("navigates immediately after manual submit, before the answer request completes", async () => {
    const api = stubApiClient();
    (api.sendMessage as ReturnType<typeof vi.fn>).mockImplementation(() => new Promise(() => {}));
    renderHome(api);

    const input = screen.getByPlaceholderText("Введите свой вопрос");
    fireEvent.change(input, { target: { value: "Как создать СТЕ для оферты?" } });
    fireEvent.submit(input.closest("form")!);

    expect(await screen.findByText("Как создать СТЕ для оферты?", { selector: "p" })).toBeInTheDocument();
    await waitFor(() => expect(api.sendMessage).toHaveBeenCalledTimes(1));
  });

  it("removes every border, outline, ring and shadow from the inner input", () => {
    const api = stubApiClient();
    renderHome(api);

    const input = screen.getByPlaceholderText("Введите свой вопрос");
    expect(input).toHaveClass("border-0", "outline-none", "ring-0", "shadow-none", "focus-visible:outline-none");
  });

  it("uses the exact three certified demo questions", () => {
    expect(QUESTION_CHIPS).toEqual([
      "Как создать СТЕ для оферты?",
      "Как загрузить YML в каталог?",
      "Как добавить МЧД?"
    ]);
  });
});
