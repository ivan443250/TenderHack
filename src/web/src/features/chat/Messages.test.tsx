import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";

import { AssistantResponse, ClarificationNotice, ModerationWarningNotice, NoConfirmedAnswerNotice, OutOfScopeNotice } from "./Messages";

afterEach(cleanup);

describe("ClarificationNotice", () => {
  it("renders mapped questions when provided (B1)", () => {
    render(<ClarificationNotice missingConditions={["role", "provider"]} questions={["Вы поставщик или заказчик?", "Через какого оператора ЭДО вы работаете?"]} />);

    expect(screen.getByText("Вы поставщик или заказчик?")).toBeInTheDocument();
    expect(screen.queryByText("role")).not.toBeInTheDocument();
  });

  it("maps slot names to human-readable copy when questions are absent (B1 fallback)", () => {
    render(<ClarificationNotice missingConditions={["role", "provider"]} questions={[]} />);

    expect(screen.getByText("Вы работаете на Портале как поставщик или как заказчик?")).toBeInTheDocument();
    expect(screen.getByText("Какой оператор ЭДО вы используете?")).toBeInTheDocument();
    expect(screen.queryByText("role")).not.toBeInTheDocument();
    expect(screen.queryByText("provider")).not.toBeInTheDocument();
  });
});

describe("AssistantResponse", () => {
  it("renders safe Markdown without exposing markup, ids, or think blocks", () => {
    render(<AssistantResponse markdown={'# Как создать СТЕ?\n\n1. **Откройте** раздел `Оферты`.\n2. Выберите пункт.\n\nfrag_abc123\nclaim_id: claim-1\n{"claim_id":"claim-2","fragment_ids":["frag-2"]}\nJSON schema\n<think>internal reasoning</think>'} />);

    expect(screen.getByRole("heading", { name: "Как создать СТЕ?" })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "" })).toBeInTheDocument();
    expect(screen.getByText("Откройте")).toBeInTheDocument();
    expect(screen.getByText("Оферты")).toBeInTheDocument();
    expect(screen.queryByText("# Как создать СТЕ?")).not.toBeInTheDocument();
    expect(screen.queryByText("frag_abc123")).not.toBeInTheDocument();
    expect(screen.queryByText("internal reasoning")).not.toBeInTheDocument();
    expect(screen.queryByText("claim-1")).not.toBeInTheDocument();
    expect(screen.queryByText("claim-2")).not.toBeInTheDocument();
    expect(screen.queryByText(/claim_id/)).not.toBeInTheDocument();
    expect(screen.queryByText("JSON schema")).not.toBeInTheDocument();
  });
});

describe("OutOfScopeNotice", () => {
  it("renders server-authored scope guidance without a handoff CTA", () => {
    render(<OutOfScopeNotice message="Я помогаю с вопросами по работе Портала поставщиков." />);

    expect(screen.getByText(/Портала поставщиков/)).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
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
