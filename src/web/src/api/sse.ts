export type CaseEvent = {
  event_id: string;
  case_id: string;
  type: string;
  payload: unknown;
};

export function openCaseEventStream(caseId: string, onEvent: (event: CaseEvent) => void): EventSource {
  const baseUrl = import.meta.env.VITE_API_BASE_URL ?? "/api";
  const stream = new EventSource(`${baseUrl}/v0/cases/${encodeURIComponent(caseId)}/events/stream`, { withCredentials: true });
  stream.addEventListener("case_event", (message) => onEvent(JSON.parse((message as MessageEvent).data) as CaseEvent));
  return stream;
}
