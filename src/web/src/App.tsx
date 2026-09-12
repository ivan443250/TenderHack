import { useEffect } from "react";
import { BrowserRouter, Route, Routes } from "react-router-dom";

import { createApiClient, type ApiClient } from "./api/client";
import { AppShell } from "./app/AppShell";
import { ChatScreen } from "./features/chat/ChatScreen";
import { HomeScreen } from "./features/home/HomeScreen";

const defaultApi = createApiClient();

export function App({ apiClient = defaultApi }: { apiClient?: ApiClient } = {}) {
  useEffect(() => {
    // web-api-v0.md §13: idempotent, issues the owner_id cookie on first load so the sidebar's
    // case list can resolve before the user sends a first message.
    void apiClient.ensureSession();
  }, [apiClient]);

  return (
    <BrowserRouter>
      <Routes>
        <Route element={<AppShell api={apiClient} />}>
          <Route index element={<HomeScreen api={apiClient} />} />
          <Route path="cases/:caseId" element={<ChatScreen api={apiClient} />} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
