import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { App } from "./App";
import type { ApiClient } from "./api/client";

function stubApiClient(): ApiClient {
  return {
    ensureSession: vi.fn().mockResolvedValue(undefined),
    listCases: vi.fn().mockResolvedValue([]),
    createCase: vi.fn(),
    getCase: vi.fn(),
    sendMessage: vi.fn(),
    getCaseEvents: vi.fn(),
    getSource: vi.fn(),
    prepareHandoff: vi.fn(),
    confirmHandoff: vi.fn(),
    retryHandoff: vi.fn(),
    completeCase: vi.fn(),
    submitFeedback: vi.fn(),
    listNotifications: vi.fn(),
    ackNotifications: vi.fn(),
    get: vi.fn()
  } as unknown as ApiClient;
}

describe("web shell", () => {
  it("renders the home screen behind the sidebar", async () => {
    render(<App apiClient={stubApiClient()} />);
    expect(await screen.findByRole("heading", { name: "Вопрос по Порталу?" })).toBeInTheDocument();
  });
});
