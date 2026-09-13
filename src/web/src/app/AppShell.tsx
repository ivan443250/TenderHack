import { useEffect, useState } from "react";
import { Outlet, useNavigate, useParams } from "react-router-dom";

import { ApiError, type ApiClient } from "../api/client";
import type { CaseListItem } from "../api/types";
import { Sidebar } from "../features/navigation/Sidebar";

export function AppShell({ api }: { api: ApiClient }) {
  const { caseId } = useParams<{ caseId: string }>();
  const navigate = useNavigate();
  const [recentCases, setRecentCases] = useState<CaseListItem[]>([]);
  const [archivedCases, setArchivedCases] = useState<CaseListItem[]>([]);
  const [collapsed, setCollapsed] = useState(false);
  const [hideError, setHideError] = useState<{ caseId: string; message: string } | null>(null);

  async function refetchLists() {
    const [active, archived] = await Promise.all([api.listCases("active"), api.listCases("archived")]);
    setRecentCases(active);
    setArchivedCases(archived);
  }

  useEffect(() => {
    let cancelled = false;
    async function load() {
      const [active, archived] = await Promise.all([api.listCases("active"), api.listCases("archived")]);
      if (!cancelled) {
        setRecentCases(active);
        setArchivedCases(archived);
      }
    }
    void load();
    return () => {
      cancelled = true;
    };
    // Re-list whenever the open case changes, so a freshly created/completed case shows up.
  }, [api, caseId]);

  async function handleHideCase(hiddenCaseId: string) {
    setHideError(null);
    try {
      await api.hideCase(hiddenCaseId);
      await refetchLists();
      if (hiddenCaseId === caseId) {
        navigate("/");
      }
    } catch (error) {
      const message =
        error instanceof ApiError && error.code === "HANDOFF_IN_PROGRESS"
          ? "Сначала дождитесь завершения обращения у специалиста."
          : "Не удалось удалить чат — попробуйте ещё раз.";
      setHideError({ caseId: hiddenCaseId, message });
    }
  }

  return (
    <div className="flex h-screen w-full bg-[var(--app-background)]">
      <Sidebar
        recentCases={recentCases}
        archivedCases={archivedCases}
        activeCaseId={caseId}
        collapsed={collapsed}
        onToggleCollapsed={() => setCollapsed((value) => !value)}
        onHideCase={handleHideCase}
        hideErrorCaseId={hideError?.caseId}
        hideErrorMessage={hideError?.message}
      />
      <div className="min-w-0 flex-1">
        <Outlet />
      </div>
    </div>
  );
}
