import { Link, useNavigate } from "react-router-dom";

import { IconButton } from "../../design-system/IconButton";
import { PortalLogo, PortalMark } from "../../design-system/Brand";
import { EditIcon, HelpIcon, SearchIcon, SettingsIcon, SidebarToggleIcon, DotIcon } from "../../design-system/icons";
import type { CaseListItem } from "../../api/types";

type SidebarProps = {
  recentCases: CaseListItem[];
  archivedCases: CaseListItem[];
  activeCaseId?: string;
  collapsed: boolean;
  onToggleCollapsed: () => void;
};

function HistoryItem({ item, active }: { item: CaseListItem; active: boolean }) {
  const label = item.case_id.slice(0, 8);
  return (
    <Link
      to={`/cases/${item.case_id}`}
      className={`flex h-9 w-full items-center gap-1.5 rounded-[10px] py-2 pl-2.5 pr-2 text-xs ${
        active ? "bg-[var(--surface-subtle)] text-[var(--content-primary)]" : "text-[var(--content-secondary)] hover:bg-[var(--surface-subtle)]"
      }`}
    >
      <span className="min-w-0 flex-1 truncate">Чат {label}</span>
      {item.unread_notifications > 0 && <DotIcon className="size-6 shrink-0 text-[var(--action-primary)]" />}
    </Link>
  );
}

export function Sidebar({ recentCases, archivedCases, activeCaseId, collapsed, onToggleCollapsed }: SidebarProps) {
  const navigate = useNavigate();

  if (collapsed) {
    return (
      <div className="flex h-full w-[60px] shrink-0 flex-col items-center gap-3 rounded-r-xl bg-[var(--surface-default)] py-3">
        <Link to="/" aria-label="На главную">
          <PortalMark />
        </Link>
        <IconButton icon={<SidebarToggleIcon className="size-[18px]" />} label="Развернуть меню" onClick={onToggleCollapsed} />
        <IconButton icon={<EditIcon className="size-5" />} label="Новый чат" onClick={() => navigate("/")} />
      </div>
    );
  }

  return (
    <nav className="flex h-full w-[213px] shrink-0 flex-col gap-1.5 rounded-r-xl bg-[var(--surface-default)] px-2.5 py-3">
      <div className="flex h-[52px] items-center gap-1.5">
        <Link to="/" className="flex h-[46px] w-[137px] items-center" aria-label="На главную">
          <PortalLogo />
        </Link>
        <div className="flex-1" />
        <IconButton icon={<SidebarToggleIcon className="size-[18px]" />} label="Свернуть меню" onClick={onToggleCollapsed} />
      </div>

      <div className="flex flex-col gap-1">
        <Link to="/" className="flex h-10 items-center gap-2 rounded-xl bg-[var(--surface-subtle)] px-2.5 text-xs font-medium text-[var(--content-primary)]">
          <EditIcon className="size-5" />
          Новый чат
        </Link>
        <button
          type="button"
          className="flex h-10 items-center gap-2 rounded-xl px-2.5 text-left text-xs font-medium text-[var(--content-secondary)] hover:bg-[var(--surface-subtle)]"
        >
          <SearchIcon className="size-5" />
          Поиск по чатам
        </button>
      </div>

      {recentCases.length > 0 && (
        <>
          <p className="mt-2 text-[11px] font-medium text-[var(--content-secondary)]">НЕДАВНИЕ</p>
          <div className="flex flex-col gap-0.5">
            {recentCases.map((item) => (
              <HistoryItem key={item.case_id} item={item} active={item.case_id === activeCaseId} />
            ))}
          </div>
        </>
      )}

      {archivedCases.length > 0 && (
        <>
          <p className="mt-2 text-[11px] font-medium text-[var(--content-secondary)]">ЗАВЕРШЕННЫЕ</p>
          <div className="flex flex-col gap-0.5">
            {archivedCases.map((item) => (
              <HistoryItem key={item.case_id} item={item} active={item.case_id === activeCaseId} />
            ))}
          </div>
        </>
      )}

      <div className="flex-1" />
      <div className="h-px w-full bg-[var(--border-default)]" />
      <div className="flex flex-col gap-0.5 py-1">
        <button type="button" className="flex h-10 items-center gap-2 rounded-xl px-2.5 text-left text-xs font-medium text-[var(--content-secondary)] hover:bg-[var(--surface-subtle)]">
          <HelpIcon className="size-5" />
          Помощь
        </button>
        <button type="button" className="flex h-10 items-center gap-2 rounded-xl px-2.5 text-left text-xs font-medium text-[var(--content-secondary)] hover:bg-[var(--surface-subtle)]">
          <SettingsIcon className="size-5" />
          Настройки
        </button>
      </div>
    </nav>
  );
}
