import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";

import { IconButton } from "../../design-system/IconButton";
import { PortalLogo, PortalMark } from "../../design-system/Brand";
import { EditIcon, SidebarToggleIcon, DotIcon, TrashIcon } from "../../design-system/icons";
import type { CaseListItem } from "../../api/types";

const MAX_CASE_TITLE_LENGTH = 36;

export function formatCaseTitle(title: string | null | undefined): string {
  const normalized = title?.trim().replace(/\s+/g, " ") ?? "";
  if (!normalized) return "Новый чат";
  if (normalized.length <= MAX_CASE_TITLE_LENGTH) return normalized;
  return `${normalized.slice(0, MAX_CASE_TITLE_LENGTH - 1).trimEnd()}…`;
}

type SidebarProps = {
  recentCases: CaseListItem[];
  archivedCases: CaseListItem[];
  activeCaseId?: string;
  collapsed: boolean;
  onToggleCollapsed: () => void;
  onHideCase: (caseId: string) => void;
  hideErrorCaseId?: string;
  hideErrorMessage?: string;
};

/** E1 (docs/plans/active/2026-09-demo-readiness.md): the trash control appears on hover/focus and
 * requires an inline "Удалить чат? Да / Нет" confirmation before calling `onHide` — no
 * `window.confirm`, which cannot be styled or tested the same way. */
function HistoryItem({
  item,
  active,
  onHide,
  errorMessage
}: {
  item: CaseListItem;
  active: boolean;
  onHide: (caseId: string) => void;
  errorMessage?: string;
}) {
  const [confirming, setConfirming] = useState(false);
  const label = formatCaseTitle(item.title);

  if (confirming) {
    return (
      <div className="flex h-10 w-full items-center gap-1.5 rounded-[10px] bg-[var(--surface-subtle)] py-2 pl-2.5 pr-2 text-xs">
        <span className="min-w-0 flex-1 truncate text-[var(--content-primary)]">Удалить чат?</span>
        <button
          type="button"
          onClick={() => {
            onHide(item.case_id);
            setConfirming(false);
          }}
          className="shrink-0 font-medium text-[var(--action-primary)] hover:opacity-80"
        >
          Да
        </button>
        <button type="button" onClick={() => setConfirming(false)} className="shrink-0 text-[var(--content-secondary)] hover:opacity-80">
          Нет
        </button>
      </div>
    );
  }

  return (
    <div className="flex flex-col">
      <div
        className={`group flex h-10 w-full items-center gap-1.5 rounded-[10px] py-2 pl-2.5 pr-2 text-xs transition-[background-color,transform,color] duration-150 hover:translate-x-0.5 ${
          active ? "bg-[var(--surface-subtle)] text-[var(--content-primary)]" : "text-[var(--content-secondary)] hover:bg-[var(--surface-subtle)]"
        }`}
      >
        <Link to={`/cases/${item.case_id}`} className="min-w-0 flex-1 truncate" title={label}>
          {label}
        </Link>
        {item.unread_notifications > 0 && <DotIcon className="size-6 shrink-0 text-[var(--action-primary)]" />}
        <button
          type="button"
          aria-label="Удалить чат"
          onClick={() => setConfirming(true)}
          className="shrink-0 rounded-md p-1 opacity-0 transition-opacity duration-150 hover:bg-[#eceff2] focus-visible:opacity-100 group-hover:opacity-100"
        >
          <TrashIcon className="size-4" />
        </button>
      </div>
      {errorMessage && <p className="pl-2.5 pt-1 text-[11px] text-[var(--action-primary)]">{errorMessage}</p>}
    </div>
  );
}

export function Sidebar({
  recentCases,
  archivedCases,
  activeCaseId,
  collapsed,
  onToggleCollapsed,
  onHideCase,
  hideErrorCaseId,
  hideErrorMessage
}: SidebarProps) {
  const navigate = useNavigate();

  if (collapsed) {
    return (
      <div className="flex h-full w-[60px] shrink-0 animate-fade-in flex-col items-center gap-3 rounded-r-xl bg-[var(--surface-default)] py-3">
        <Link to="/" aria-label="На главную">
          <PortalMark />
        </Link>
        <IconButton icon={<SidebarToggleIcon className="size-[18px]" />} label="Развернуть меню" onClick={onToggleCollapsed} />
        <IconButton icon={<EditIcon className="size-5" />} label="Новый чат" onClick={() => navigate("/")} />
      </div>
    );
  }

  return (
    <nav className="flex h-full w-[232px] shrink-0 animate-slide-in-left flex-col gap-1.5 rounded-r-xl bg-[var(--surface-default)] px-2.5 py-3">
      <div className="flex h-[52px] items-center gap-1.5">
        <Link to="/" className="flex h-[46px] w-[137px] items-center" aria-label="На главную">
          <PortalLogo />
        </Link>
        <div className="flex-1" />
        <IconButton icon={<SidebarToggleIcon className="size-[18px]" />} label="Свернуть меню" onClick={onToggleCollapsed} />
      </div>

      <div className="flex flex-col gap-1">
        <Link to="/" className="flex h-11 items-center gap-2 rounded-xl bg-[var(--surface-subtle)] px-2.5 text-xs font-medium text-[var(--content-primary)] transition-[background-color,transform] duration-150 hover:bg-[#f3f5f7] active:scale-[0.99]">
          <EditIcon className="size-5" />
          Новый чат
        </Link>
      </div>

      {recentCases.length > 0 && (
        <>
          <p className="mt-3 text-xs font-medium uppercase tracking-wide text-[var(--content-tertiary)]">НЕДАВНИЕ</p>
          <div className="flex flex-col gap-0.5">
            {recentCases.map((item) => (
              <HistoryItem
                key={item.case_id}
                item={item}
                active={item.case_id === activeCaseId}
                onHide={onHideCase}
                errorMessage={item.case_id === hideErrorCaseId ? hideErrorMessage : undefined}
              />
            ))}
          </div>
        </>
      )}

      {archivedCases.length > 0 && (
        <>
          <p className="mt-3 text-xs font-medium uppercase tracking-wide text-[var(--content-tertiary)]">ЗАВЕРШЕННЫЕ</p>
          <div className="flex flex-col gap-0.5">
            {archivedCases.map((item) => (
              <HistoryItem
                key={item.case_id}
                item={item}
                active={item.case_id === activeCaseId}
                onHide={onHideCase}
                errorMessage={item.case_id === hideErrorCaseId ? hideErrorMessage : undefined}
              />
            ))}
          </div>
        </>
      )}

      <div className="flex-1" />
    </nav>
  );
}
