import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { CaseListItem } from "../../api/types";
import { Sidebar } from "./Sidebar";

afterEach(cleanup);

function item(caseId: string, title = "Как создать СТЕ для оферты?"): CaseListItem {
  return {
    case_id: caseId,
    title,
    conversation_status: "ACTIVE",
    resolution_status: "UNKNOWN",
    last_activity_at: new Date().toISOString(),
    unread_notifications: 0
  };
}

function renderSidebar(onHideCase: (caseId: string) => void, extra: Partial<Parameters<typeof Sidebar>[0]> = {}) {
  return render(
    <MemoryRouter>
      <Sidebar
        recentCases={[item("case-12345678")]}
        archivedCases={[]}
        collapsed={false}
        onToggleCollapsed={() => {}}
        onHideCase={onHideCase}
        {...extra}
      />
    </MemoryRouter>
  );
}

describe("Sidebar hide-case control (E1)", () => {
  it("requires confirmation before calling onHideCase", () => {
    const onHideCase = vi.fn();
    renderSidebar(onHideCase);

    fireEvent.click(screen.getByLabelText("Удалить чат"));

    expect(onHideCase).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("Да"));
    expect(onHideCase).toHaveBeenCalledWith("case-12345678");
  });

  it("cancelling the confirmation does not call onHideCase", () => {
    const onHideCase = vi.fn();
    renderSidebar(onHideCase);

    fireEvent.click(screen.getByLabelText("Удалить чат"));
    fireEvent.click(screen.getByText("Нет"));

    expect(onHideCase).not.toHaveBeenCalled();
    expect(screen.getByText("Как создать СТЕ для оферты?")).toBeInTheDocument();
  });

  it("shows the handoff-in-progress error under the matching item", () => {
    renderSidebar(vi.fn(), {
      hideErrorCaseId: "case-12345678",
      hideErrorMessage: "Сначала дождитесь завершения обращения у специалиста."
    });

    expect(screen.getByText("Сначала дождитесь завершения обращения у специалиста.")).toBeInTheDocument();
  });

  it("shows a question preview instead of any case id", () => {
    renderSidebar(vi.fn());

    expect(screen.getByText("Как создать СТЕ для оферты?")).toBeInTheDocument();
    expect(screen.queryByText(/case-123/)).not.toBeInTheDocument();
  });

  it("truncates an overlong title to 36 characters with an ellipsis", () => {
    const longTitle = "Что делать при ошибке отправки документа исполнения и повторной ошибке";
    renderSidebar(vi.fn(), { recentCases: [item("case-12345678", longTitle)] });

    const link = screen.getByRole("link", { name: /Что делать при ошибке отправки/ });
    expect(link.textContent).toHaveLength(36);
    expect(link.textContent).toMatch(/…$/);
    expect(link).toHaveClass("truncate");
  });

  it("uses 'Новый чат' when an old case has no title", () => {
    renderSidebar(vi.fn(), { recentCases: [item("case-12345678", "")] });

    expect(screen.getByTitle("Новый чат")).toBeInTheDocument();
  });
});
