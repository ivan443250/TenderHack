import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { ClarificationNotice, ModerationWarningNotice, NoConfirmedAnswerNotice } from "./Messages";

describe("ClarificationNotice", () => {
  it("renders mapped questions when provided (B1)", () => {
    render(<ClarificationNotice missingConditions={["role", "provider"]} questions={["Вы поставщик или заказчик?", "Через какого оператора ЭДО вы работаете?"]} />);

    expect(screen.getByText("Вы поставщик или заказчик?")).toBeInTheDocument();
    expect(screen.queryByText("role")).not.toBeInTheDocument();
  });

  it("falls back to raw missing_conditions when questions is empty (B1 fallback)", () => {
    render(<ClarificationNotice missingConditions={["role", "provider"]} questions={[]} />);

    expect(screen.getByText("role")).toBeInTheDocument();
    expect(screen.getByText("provider")).toBeInTheDocument();
  });
});

describe("NoConfirmedAnswerNotice", () => {
  it("does not claim 'no answer found' on an explicit human request (B2)", () => {
    render(<NoConfirmedAnswerNotice reason="EXPLICIT_HUMAN_REQUEST" />);

    expect(screen.queryByText(/не нашлось подтверждённого ответа/)).not.toBeInTheDocument();
    expect(screen.getByText(/Соединяю вас со специалистом/)).toBeInTheDocument();
  });

  it("keeps the 'no answer found' copy for every other reason", () => {
    render(<NoConfirmedAnswerNotice reason="INSUFFICIENT_EVIDENCE" />);

    expect(screen.getByText(/не нашлось подтверждённого ответа/)).toBeInTheDocument();
  });
});

describe("ModerationWarningNotice", () => {
  it("renders the server-provided message verbatim (B3)", () => {
    render(<ModerationWarningNotice message="Предупреждение 1 из 2. При повторном нарушении чат будет закрыт." />);

    expect(screen.getByText("Предупреждение 1 из 2. При повторном нарушении чат будет закрыт.")).toBeInTheDocument();
  });
});
