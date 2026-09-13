import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { ApiClient } from "../../api/client";
import type { Applicability, SourceDetail } from "../../api/types";
import { ApplicabilityCard } from "./ApplicabilityCard";

afterEach(cleanup);

function base(overrides: Partial<Applicability> = {}): Applicability {
  return {
    entities: [],
    missing_conditions: [],
    questions: [],
    risk_flags: [],
    evidence_fragment_ids: [],
    ...overrides
  };
}

function stubApi(overrides: Partial<ApiClient> = {}): ApiClient {
  return { getSource: vi.fn(), ...overrides } as unknown as ApiClient;
}

describe("ApplicabilityCard (F1)", () => {
  it("renders nothing when there are no entities and nothing missing", () => {
    const { container } = render(<ApplicabilityCard applicability={base()} api={stubApi()} onOpenSource={() => {}} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("marks user_explicit and trusted_portal_context as confirmed, inferred as not", () => {
    const applicability = base({
      entities: [
        { type: "role", value: "поставщик", provenance: "user_explicit" },
        { type: "process", value: "contract_execution", provenance: "trusted_portal_context" },
        { type: "status", value: "draft", provenance: "inferred" }
      ]
    });
    render(<ApplicabilityCard applicability={applicability} api={stubApi()} onOpenSource={() => {}} />);
    fireEvent.click(screen.getByText("Почему этот ответ применим"));

    expect(screen.getByText("вы указали", { exact: false })).toBeInTheDocument();
    expect(screen.getByText("из Портала", { exact: false })).toBeInTheDocument();
    expect(screen.getByText("предположение", { exact: false })).toBeInTheDocument();
  });

  it("renders questions for missing_conditions instead of raw slot ids", () => {
    const applicability = base({ missing_conditions: ["role"], questions: ["Вы поставщик или заказчик?"] });
    render(<ApplicabilityCard applicability={applicability} api={stubApi()} onOpenSource={() => {}} />);
    fireEvent.click(screen.getByText("Почему этот ответ применим"));

    expect(screen.getByText("Вы поставщик или заказчик?")).toBeInTheDocument();
    expect(screen.queryByText("role")).not.toBeInTheDocument();
  });

  it("opens the first evidence fragment as a source", async () => {
    const source: SourceDetail = { document_id: "doc-1", title: "Title", version: "v1", page: 1, anchor: null, text: "text", snapshot_id: "snap-1" };
    const api = stubApi({ getSource: vi.fn().mockResolvedValue(source) });
    const onOpenSource = vi.fn();
    const applicability = base({ entities: [{ type: "role", value: "поставщик", provenance: "user_explicit" }], evidence_fragment_ids: ["frag-1"] });
    render(<ApplicabilityCard applicability={applicability} api={api} onOpenSource={onOpenSource} />);
    fireEvent.click(screen.getByText("Почему этот ответ применим"));

    fireEvent.click(screen.getByText("Открыть источник"));

    await waitFor(() => expect(onOpenSource).toHaveBeenCalledWith(source));
    expect(api.getSource).toHaveBeenCalledWith("frag-1");
  });
});
