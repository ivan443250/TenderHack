import type {
  CaseEvent,
  CaseListItem,
  CaseSnapshot,
  Notification,
  SendMessageResponse,
  SourceDetail,
  SubmitFeedbackRequest
} from "./types";

export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    public code?: string,
    public fieldErrors?: Record<string, string[]>
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export type ApiClientOptions = {
  baseUrl?: string;
  fetchImpl?: typeof fetch;
};

function idempotencyKey(): string {
  return crypto.randomUUID();
}

export function createApiClient(options: ApiClientOptions = {}) {
  const baseUrl = options.baseUrl ?? import.meta.env.VITE_API_BASE_URL ?? "/api";
  const fetchImpl = options.fetchImpl ?? fetch;

  async function request<T>(
    path: string,
    init: RequestInit & { idempotent?: boolean } = {}
  ): Promise<T> {
    const headers = new Headers(init.headers);
    if (init.body && !headers.has("Content-Type")) {
      headers.set("Content-Type", "application/json");
    }
    if (init.idempotent) {
      headers.set("Idempotency-Key", idempotencyKey());
    }

    const response = await fetchImpl(`${baseUrl}${path}`, {
      ...init,
      headers,
      credentials: "include"
    });

    if (!response.ok) {
      let body: { code?: string; title?: string; detail?: string; errors?: Record<string, string[]> } = {};
      try {
        body = await response.json();
      } catch {
        // non-JSON error body; fall through with defaults below
      }
      throw new ApiError(
        body.detail ?? body.title ?? `API request failed: ${response.status}`,
        response.status,
        body.code,
        body.errors
      );
    }

    if (response.status === 204) {
      return undefined as T;
    }
    return response.json() as Promise<T>;
  }

  return {
    async get<T>(path: string): Promise<T> {
      return request<T>(path, { method: "GET" });
    },

    async ensureSession(): Promise<void> {
      await request<{ status: string }>("/v0/session", { method: "POST" });
    },

    async listCases(status?: "active" | "archived"): Promise<CaseListItem[]> {
      const qs = status ? `?status=${status}` : "";
      return request<CaseListItem[]>(`/v0/cases${qs}`, { method: "GET" });
    },

    async createCase(): Promise<CaseSnapshot> {
      return request<CaseSnapshot>("/v0/cases", { method: "POST", body: "{}", idempotent: true });
    },

    async getCase(caseId: string): Promise<CaseSnapshot> {
      return request<CaseSnapshot>(`/v0/cases/${encodeURIComponent(caseId)}`, { method: "GET" });
    },

    async sendMessage(caseId: string, text: string, clientMessageId: string): Promise<SendMessageResponse> {
      return request<SendMessageResponse>(`/v0/cases/${encodeURIComponent(caseId)}/messages`, {
        method: "POST",
        body: JSON.stringify({ text, client_message_id: clientMessageId })
      });
    },

    async getCaseEvents(caseId: string, after = 0): Promise<CaseEvent[]> {
      return request<CaseEvent[]>(`/v0/cases/${encodeURIComponent(caseId)}/events?after=${after}`, {
        method: "GET"
      });
    },

    async getSource(fragmentId: string): Promise<SourceDetail> {
      return request<SourceDetail>(`/v0/sources/${encodeURIComponent(fragmentId)}`, { method: "GET" });
    },

    async prepareHandoff(caseId: string): Promise<CaseSnapshot> {
      return request<CaseSnapshot>(`/v0/cases/${encodeURIComponent(caseId)}/handoff/prepare`, {
        method: "POST"
      });
    },

    async confirmHandoff(caseId: string, summary: string): Promise<CaseSnapshot> {
      return request<CaseSnapshot>(`/v0/cases/${encodeURIComponent(caseId)}/handoff/confirm`, {
        method: "POST",
        body: JSON.stringify({ summary }),
        idempotent: true
      });
    },

    async retryHandoff(caseId: string, summary: string): Promise<CaseSnapshot> {
      return request<CaseSnapshot>(`/v0/cases/${encodeURIComponent(caseId)}/handoff/retry`, {
        method: "POST",
        body: JSON.stringify({ summary }),
        idempotent: true
      });
    },

    async completeCase(caseId: string, solved: boolean | null): Promise<CaseSnapshot> {
      return request<CaseSnapshot>(`/v0/cases/${encodeURIComponent(caseId)}/complete`, {
        method: "POST",
        body: JSON.stringify({ solved })
      });
    },

    async submitFeedback(caseId: string, feedback: SubmitFeedbackRequest): Promise<void> {
      await request(`/v0/cases/${encodeURIComponent(caseId)}/feedback`, {
        method: "POST",
        body: JSON.stringify(feedback),
        idempotent: true
      });
    },

    async listNotifications(after = 0, unread = false): Promise<Notification[]> {
      return request<Notification[]>(`/v0/notifications?after=${after}&unread=${unread}`, { method: "GET" });
    },

    async ackNotifications(ids: string[]): Promise<void> {
      await request(`/v0/notifications/ack`, { method: "POST", body: JSON.stringify({ ids }) });
    }
  };
}

export type ApiClient = ReturnType<typeof createApiClient>;
