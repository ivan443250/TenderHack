import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { ApiClient } from "../../api/client";
import { HomeScreen } from "./HomeScreen";
import { MORE_QUESTIONS, QUESTION_CHIPS } from "./popularQuestions";

afterEach(cleanup);

function stubApiClient(): ApiClient {
  return {
    createCase: vi.fn().mockResolvedValue({ case_id: "case-1" }),
    sendMessage: vi.fn().mockResolvedValue(undefined)
  } as unknown as ApiClient;
}

function renderHome(api: ApiClient) {
  return render(
    <MemoryRouter>
      <HomeScreen api={api} />
    </MemoryRouter>
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

  it("still sends a question chip's exact text as a new case", async () => {
    const api = stubApiClient();
    renderHome(api);

    fireEvent.click(screen.getByText(QUESTION_CHIPS[0]));

    await waitFor(() => expect(api.sendMessage).toHaveBeenCalled());
    expect(api.createCase).toHaveBeenCalledTimes(1);
    expect(api.sendMessage).toHaveBeenCalledWith("case-1", QUESTION_CHIPS[0], expect.any(String));
  });
});
