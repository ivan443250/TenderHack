import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { ApiError, type ApiClient } from "../../api/client";
import type { AnswerSource, Material, MaterialSection, SourceDetail } from "../../api/types";
import { ContextPanel } from "./ContextPanel";

afterEach(cleanup);

function stubApiClient(overrides: Partial<ApiClient> = {}): ApiClient {
  return {
    getSource: vi.fn(),
    listMaterials: vi.fn(),
    listMaterialSections: vi.fn(),
    ...overrides
  } as unknown as ApiClient;
}

const MATERIAL: Material = {
  document_id: "doc-1",
  title: "Инструкция для поставщика.pdf",
  declared_version: "11",
  declared_date: null,
  page_count: 93,
  fragment_count: 1200
};

const SECTION: MaterialSection = { section: "1. Общие положения", page_start: 1, page_end: 5, first_fragment_id: "frag-1" };

describe("ContextPanel Materials tab (E3)", () => {
  it("shows answer sources in the count and opens the cited fragment", async () => {
    const sourceRef: AnswerSource = { fragment_id: "frag-hidden-id", title: "Инструкция по созданию оферты.pdf", page: 12, label: "стр. 12" };
    const source: SourceDetail = { document_id: "doc-1", title: sourceRef.title, version: "v1", page: 12, anchor: null, text: "Шаги создания", snapshot_id: "snap-1" };
    const api = stubApiClient({ getSource: vi.fn().mockResolvedValue(source) });
    render(<ContextPanel api={api} usedSources={[]} sourceRefs={[sourceRef]} onClose={() => {}} />);

    expect(screen.getByText("Источники · 1")).toBeInTheDocument();
    expect(screen.getByText(sourceRef.title)).toBeInTheDocument();
    expect(screen.queryByText(sourceRef.fragment_id)).not.toBeInTheDocument();

    fireEvent.click(screen.getByText(sourceRef.title));
    await waitFor(() => expect(api.getSource).toHaveBeenCalledWith(sourceRef.fragment_id));
    expect(await screen.findByText("Шаги создания")).toBeInTheDocument();
  });

  it("loads and shows the document list, then a document's sections on click", async () => {
    const api = stubApiClient({
      listMaterials: vi.fn().mockResolvedValue({ snapshot_id: "snap-1", materials: [MATERIAL] }),
      listMaterialSections: vi.fn().mockResolvedValue({ snapshot_id: "snap-1", document_id: "doc-1", sections: [SECTION] })
    });
    render(<ContextPanel api={api} usedSources={[]} onClose={() => {}} />);

    fireEvent.click(screen.getByText("Материалы"));
    await waitFor(() => expect(screen.getByText("Инструкция для поставщика.pdf")).toBeInTheDocument());

    fireEvent.click(screen.getByText("Инструкция для поставщика.pdf"));
    await waitFor(() => expect(screen.getByText("1. Общие положения")).toBeInTheDocument());
  });

  it("opens a section through GET /sources/{first_fragment_id}", async () => {
    const source: SourceDetail = { document_id: "doc-1", title: "Инструкция.pdf", version: "v11", page: 1, anchor: null, text: "Текст раздела", snapshot_id: "snap-1" };
    const api = stubApiClient({
      listMaterials: vi.fn().mockResolvedValue({ snapshot_id: "snap-1", materials: [MATERIAL] }),
      listMaterialSections: vi.fn().mockResolvedValue({ snapshot_id: "snap-1", document_id: "doc-1", sections: [SECTION] }),
      getSource: vi.fn().mockResolvedValue(source)
    });
    render(<ContextPanel api={api} usedSources={[]} onClose={() => {}} />);

    fireEvent.click(screen.getByText("Материалы"));
    await waitFor(() => screen.getByText("Инструкция для поставщика.pdf"));
    fireEvent.click(screen.getByText("Инструкция для поставщика.pdf"));
    await waitFor(() => screen.getByText("1. Общие положения"));

    fireEvent.click(screen.getByText("1. Общие положения"));

    await waitFor(() => expect(api.getSource).toHaveBeenCalledWith("frag-1"));
    expect(await screen.findByText("Текст раздела")).toBeInTheDocument();
  });

  it("shows the unavailable message on a 503 instead of an empty placeholder", async () => {
    const api = stubApiClient({
      listMaterials: vi.fn().mockRejectedValue(new ApiError("unavailable", 503, "KNOWLEDGE_UNAVAILABLE"))
    });
    render(<ContextPanel api={api} usedSources={[]} onClose={() => {}} />);

    fireEvent.click(screen.getByText("Материалы"));

    expect(await screen.findByText("Библиотека временно недоступна.")).toBeInTheDocument();
  });
});
