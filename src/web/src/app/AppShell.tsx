import { useEffect, useState } from "react";
import { Outlet, useParams } from "react-router-dom";

import type { ApiClient } from "../api/client";
import type { CaseListItem } from "../api/types";
import { Sidebar } from "../features/navigation/Sidebar";

export function AppShell({ api }: { api: ApiClient }) {
  const { caseId } = useParams<{ caseId: string }>();
  const [recentCases, setRecentCases] = useState<CaseListItem[]>([]);
  const [archivedCases, setArchivedCases] = useState<CaseListItem[]>([]);
  const [collapsed, setCollapsed] = useState(false);

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

  return (
    <div className="flex h-screen w-full bg-[var(--app-background)]">
      <Sidebar
        recentCases={recentCases}
        archivedCases={archivedCases}
        activeCaseId={caseId}
        collapsed={collapsed}
        onToggleCollapsed={() => setCollapsed((value) => !value)}
      />
      <div className="min-w-0 flex-1">
        <Outlet />
      </div>
    </div>
  );
}
