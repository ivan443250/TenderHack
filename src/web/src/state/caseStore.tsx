import { useCallback, useEffect, useRef, useState } from "react";

import type { ApiClient } from "../api/client";
import { openCaseEventStream } from "../api/sse";
import type { CaseSnapshot, FeedbackRating } from "../api/types";

export type CaseSessionState = {
  snapshot: CaseSnapshot | null;
  loading: boolean;
  error: string | null;
  sendMessage: (text: string) => Promise<void>;
  prepareHandoff: () => Promise<void>;
  confirmHandoff: (summary: string) => Promise<void>;
  retryHandoff: (summary: string) => Promise<void>;
  complete: (solved: boolean | null) => Promise<void>;
  submitFeedback: (feedback: {
    specialist_rating: FeedbackRating | null;
    information_quality_rating: FeedbackRating | null;
    solved: boolean | null;
    comment_text: string | null;
  }) => Promise<void>;
};

/**
 * Server-driven case session: the snapshot (incl. full timeline) is the single source of truth.
 * On every SSE event we simply refetch the snapshot rather than hand-merging event payloads —
 * `GET /cases/{id}` already returns the full authoritative state (docs/contracts/web-api-v0.md §4),
 * so this keeps the frontend from inventing any derived state (AGENTS.md §3).
 */
export function useCaseSession(caseId: string | undefined, api: ApiClient): CaseSessionState {
  const [snapshot, setSnapshot] = useState<CaseSnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const refreshing = useRef(false);

  const refresh = useCallback(async () => {
    if (!caseId || refreshing.current) return;
    refreshing.current = true;
    try {
      const next = await api.getCase(caseId);
      setSnapshot(next);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Не удалось загрузить обращение");
    } finally {
      refreshing.current = false;
    }
  }, [api, caseId]);

  useEffect(() => {
    if (!caseId) {
      setSnapshot(null);
      setLoading(false);
      return;
    }

    setLoading(true);
    refresh().finally(() => setLoading(false));

    const stream = openCaseEventStream(caseId, () => {
      void refresh();
    });
    stream.onerror = () => {
      // Browser auto-reconnects EventSource; nothing to do beyond letting the next event trigger a refresh.
    };
    return () => stream.close();
  }, [caseId, refresh]);

  return {
    snapshot,
    loading,
    error,
    async sendMessage(text) {
      if (!caseId) return;
      await api.sendMessage(caseId, text, crypto.randomUUID());
      await refresh();
    },
    async prepareHandoff() {
      if (!caseId) return;
      await api.prepareHandoff(caseId);
      await refresh();
    },
    async confirmHandoff(summary) {
      if (!caseId) return;
      await api.confirmHandoff(caseId, summary);
      await refresh();
    },
    async retryHandoff(summary) {
      if (!caseId) return;
      await api.retryHandoff(caseId, summary);
      await refresh();
    },
    async complete(solved) {
      if (!caseId) return;
      await api.completeCase(caseId, solved);
      await refresh();
    },
    async submitFeedback(feedback) {
      if (!caseId) return;
      await api.submitFeedback(caseId, feedback);
      await refresh();
    }
  };
}
