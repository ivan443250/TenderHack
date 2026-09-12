import type { CaseEvent, Notification } from "./types";

// EventSource cannot set a Last-Event-ID header on its first connection, so every (re)connect
// starts from event 0; the server replays the whole history and duplicates are expected —
// callers dedupe by event_id/notification_id, per docs/contracts/web-api-v0.md §6/§12.

export function openCaseEventStream(caseId: string, onEvent: (event: CaseEvent) => void): EventSource {
  const baseUrl = import.meta.env.VITE_API_BASE_URL ?? "/api";
  const stream = new EventSource(`${baseUrl}/v0/cases/${encodeURIComponent(caseId)}/events/stream`, {
    withCredentials: true
  });
  stream.addEventListener("case_event", (message) => {
    onEvent(JSON.parse((message as MessageEvent).data) as CaseEvent);
  });
  return stream;
}

export function openNotificationStream(onNotification: (notification: Notification) => void): EventSource {
  const baseUrl = import.meta.env.VITE_API_BASE_URL ?? "/api";
  const stream = new EventSource(`${baseUrl}/v0/notifications/stream`, { withCredentials: true });
  stream.addEventListener("notification", (message) => {
    onNotification(JSON.parse((message as MessageEvent).data) as Notification);
  });
  return stream;
}
