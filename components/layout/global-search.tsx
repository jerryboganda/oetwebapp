'use client';

import { useEffect, useId, useMemo, useRef, useState, useSyncExternalStore, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import { Bell, CornerDownLeft, FileText, HelpCircle, Loader2, Search, Settings, Trophy, X } from 'lucide-react';
import { Modal } from '@/components/ui/modal';
import { useLearnerNavVisibility } from '@/hooks/use-learner-nav-visibility';
import { searchContent } from '@/lib/api';
import { triggerImpactHaptic } from '@/lib/mobile/haptics';
import type { UserRole } from '@/lib/types/auth';
import { cn } from '@/lib/utils';
import { getWorkspaceSettingsHref, type NavGroup } from './sidebar';
import { HEADER_CHIP, HEADER_CHIP_HOVER } from './header-chrome';

interface ContentSearchItem {
  id: string;
  title: string;
  subtestCode?: string | null;
  contentType?: string | null;
  difficulty?: string | null;
  estimatedDurationMinutes?: number | null;
}

interface ContentSearchResponse {
  items?: ContentSearchItem[];
  total?: number;
}

interface PaletteLink {
  href: string;
  label: string;
  sidebarLabel?: string;
  icon?: ReactNode;
}

interface ResultRow {
  key: string;
  label: string;
  hint?: string;
  href: string;
  icon?: ReactNode;
}

/**
 * Learner account pages listed beside the nav. Real routes only:
 * `__tests__/learner-breadcrumb-routability.test.ts` fails if one loses its page.
 */
export const LEARNER_ACCOUNT_DESTINATIONS: readonly PaletteLink[] = [
  { label: 'Settings', href: getWorkspaceSettingsHref('learner'), icon: <Settings /> },
  { label: 'Notification settings', href: '/settings/notifications', icon: <Bell /> },
  { label: 'Achievements', href: '/achievements', icon: <Trophy /> },
  { label: 'Help & Support', href: '/support', icon: <HelpCircle /> },
];

/** Pages outside each role's nav. Admin settings are already a permission-gated nav item. */
const ACCOUNT_DESTINATIONS: Partial<Record<UserRole, readonly PaletteLink[]>> = {
  learner: LEARNER_ACCOUNT_DESTINATIONS,
  expert: [{ label: 'Settings', href: getWorkspaceSettingsHref('expert'), icon: <Settings /> }],
};

/** Content rows point at the module that owns them; there is no per-item route. */
function hrefForContent(item: ContentSearchItem): string {
  const subtest = (item.subtestCode ?? '').toLowerCase();
  if (['listening', 'reading', 'writing', 'speaking'].includes(subtest)) return `/${subtest}`;
  return '/materials';
}

/** Radix dialogs carry no aria-modal, so match on role and skip hidden ones. */
function hasVisibleDialog() {
  return Array.from(document.querySelectorAll<HTMLElement>('[role="dialog"], [role="alertdialog"]'))
    .some((dialog) => dialog.checkVisibility?.() ?? true);
}

export interface GlobalSearchProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The shell's resolved nav, already filtered by permission and flags. */
  sections: readonly NavGroup[];
  workspaceRole: UserRole;
}

/**
 * Ctrl/⌘K command palette, mounted once per AppShell. Rows come only from the
 * shell's own nav, the learner account pages and (learners only) /v1/search.
 */
export function GlobalSearch({ open, onOpenChange, sections, workspaceRole }: GlobalSearchProps) {
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.repeat) return;
      if (!(event.metaKey || event.ctrlKey) || event.key?.toLowerCase() !== 'k') return;
      // Never stack over another dialog: its focus trap would own the keyboard.
      if (!open && hasVisibleDialog()) return;
      event.preventDefault();
      onOpenChange(!open);
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [open, onOpenChange]);

  const close = () => onOpenChange(false);

  // Modal owns Escape, the focus trap, scroll lock and focus restore. The panel
  // mounts only while open, so its state resets on every open.
  return (
    <Modal
      open={open}
      onClose={close}
      ariaLabel="Search"
      size="lg"
      className="sm:mt-[12vh] sm:max-w-xl sm:self-start"
      bodyClassName="flex flex-col overflow-hidden p-0 sm:p-0"
    >
      <SearchPanel sections={sections} workspaceRole={workspaceRole} onClose={close} />
    </Modal>
  );
}

function SearchPanel({
  sections,
  workspaceRole,
  onClose,
}: {
  sections: readonly NavGroup[];
  workspaceRole: UserRole;
  onClose: () => void;
}) {
  const router = useRouter();
  const listboxId = useId();
  const [query, setQuery] = useState('');
  const [activeIndex, setActiveIndex] = useState(0);
  const [content, setContent] = useState<{ q: string; items: ContentSearchItem[]; failed: boolean } | null>(null);
  const requestIdRef = useRef(0);
  const isLearner = workspaceRole === 'learner';
  const navItems = useMemo(() => sections.flatMap((section) => section.items), [sections]);
  // Learners see exactly what their sidebar shows; staff nav arrives filtered.
  const isNavItemVisible = useLearnerNavVisibility(navItems, isLearner);
  const trimmed = query.trim();
  // /v1/search is learner-only (403 for staff), so staff never call it.
  const searchable = isLearner && trimmed.length >= 2;

  useEffect(() => {
    if (!searchable) return undefined;
    const requestId = ++requestIdRef.current;
    const timer = setTimeout(async () => {
      try {
        const response = (await searchContent({ q: trimmed, pageSize: 8 })) as ContentSearchResponse;
        if (requestId === requestIdRef.current) setContent({ q: trimmed, items: response?.items ?? [], failed: false });
      } catch {
        // Search is a convenience surface — degrade to navigation-only rather
        // than blocking the palette behind an error state.
        if (requestId === requestIdRef.current) setContent({ q: trimmed, items: [], failed: true });
      }
    }, 300);
    return () => clearTimeout(timer);
  }, [searchable, trimmed]);

  const loading = searchable && content?.q !== trimmed;
  const failed = searchable && Boolean(content?.failed);

  const needle = trimmed.toLowerCase();
  const seen = new Set<string>();
  // De-duplicated by href; matches the bottom-nav label and the sidebar's full name.
  const linkRows = (links: readonly PaletteLink[]): ResultRow[] =>
    links
      .filter((link) => {
        if (seen.has(link.href)) return false;
        seen.add(link.href);
        return !needle || [link.label, link.sidebarLabel].some((text) => text?.toLowerCase().includes(needle));
      })
      .map((link) => ({ key: `nav:${link.href}`, label: link.sidebarLabel ?? link.label, href: link.href, icon: link.icon }));

  const contentRows: ResultRow[] = (searchable ? content?.items ?? [] : []).map((item) => ({
    key: `content:${item.id}`,
    label: item.title,
    hint: [item.subtestCode, item.contentType, item.difficulty].filter(Boolean).join(' · ') || undefined,
    href: hrefForContent(item),
  }));
  // Same-named groups merge (the tutor nav has its own "Account" section).
  const rowsByGroup = new Map<string, ResultRow[]>();
  const addGroup = (label: string, groupRows: ResultRow[]) => rowsByGroup.set(label, [...(rowsByGroup.get(label) ?? []), ...groupRows]);
  sections.forEach((section) => addGroup(sections.length > 1 ? section.label : 'Go to', linkRows(section.items.filter(isNavItemVisible))));
  addGroup('Account', linkRows(ACCOUNT_DESTINATIONS[workspaceRole] ?? []));
  addGroup('Content', contentRows);
  const groups = Array.from(rowsByGroup, ([label, groupRows]) => ({ label, rows: groupRows })).filter((group) => group.rows.length > 0);
  const rows = groups.flatMap((group) => group.rows);
  const active = Math.min(activeIndex, rows.length - 1);
  const optionId = (index: number) => `${listboxId}-option-${index}`;

  const go = (href: string) => {
    onClose();
    router.push(href);
  };

  const onInputKeyDown = (event: ReactKeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      if (rows.length === 0) return;
      const next = (active + (event.key === 'ArrowDown' ? 1 : rows.length - 1)) % rows.length;
      setActiveIndex(next);
      document.getElementById(optionId(next))?.scrollIntoView?.({ block: 'nearest' });
    } else if (event.key === 'Enter' && !event.nativeEvent.isComposing) {
      event.preventDefault();
      const row = rows[active];
      if (row) go(row.href);
    }
  };

  return (
    <>
      <div className="flex shrink-0 items-center gap-2.5 border-b border-border px-4">
        <Search className="h-4 w-4 shrink-0 text-muted" aria-hidden="true" />
        <input
          role="combobox"
          aria-label="Search"
          aria-expanded={rows.length > 0}
          aria-controls={rows.length > 0 ? listboxId : undefined}
          aria-activedescendant={active >= 0 ? optionId(active) : undefined}
          aria-autocomplete="list"
          autoComplete="off"
          spellCheck={false}
          value={query}
          onChange={(event) => {
            setQuery(event.target.value);
            setActiveIndex(0);
          }}
          onKeyDown={onInputKeyDown}
          placeholder={isLearner ? 'Search content, pages and practice…' : 'Search pages…'}
          className="h-12 min-w-0 flex-1 bg-transparent text-base text-navy placeholder:text-muted focus:outline-none sm:text-sm"
        />
        {loading ? <Loader2 className="h-4 w-4 shrink-0 animate-spin text-muted" aria-hidden="true" /> : null}
        <button
          type="button"
          onClick={onClose}
          className="shrink-0 rounded-md p-1 text-muted transition-colors hover:bg-background-light hover:text-navy"
          aria-label="Close search"
        >
          <X className="h-4 w-4" aria-hidden="true" />
        </button>
      </div>

      {rows.length > 0 ? (
        // The listbox is the scroll container: axe exempts a combobox's popup
        // from scrollable-region-focusable only when it is the one scrolling.
        <ul id={listboxId} role="listbox" aria-label="Search results" className="max-h-[min(28rem,55dvh)] overflow-y-auto overscroll-contain p-2">
          {groups.map((group, groupIndex) => (
            <li key={group.label} role="none">
              <ul role="group" aria-labelledby={`${listboxId}-group-${groupIndex}`}>
                <li role="none" id={`${listboxId}-group-${groupIndex}`} className="px-3 pb-1 pt-2.5 text-2xs font-bold uppercase tracking-[0.16em] text-muted">
                  {group.label}
                </li>
                {group.rows.map((row) => {
                  const index = rows.indexOf(row);
                  const selected = index === active;
                  return (
                    <li
                      key={row.key}
                      id={optionId(index)}
                      role="option"
                      aria-selected={selected}
                      data-href={row.href}
                      onMouseMove={() => {
                        if (!selected) setActiveIndex(index);
                      }}
                      onClick={() => go(row.href)}
                      className={cn('flex cursor-pointer items-center gap-2.5 rounded-lg px-3 py-2 text-navy', selected && 'bg-primary/10')}
                    >
                      <span className="flex shrink-0 text-muted [&_svg]:h-4 [&_svg]:w-4" aria-hidden="true">
                        {row.icon ?? <FileText />}
                      </span>
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-sm font-medium">{row.label}</span>
                        {row.hint ? <span className="block truncate text-2xs text-muted">{row.hint}</span> : null}
                      </span>
                      {selected ? <CornerDownLeft className="h-3.5 w-3.5 shrink-0 text-muted" aria-hidden="true" /> : null}
                    </li>
                  );
                })}
              </ul>
            </li>
          ))}
        </ul>
      ) : (
        <p className="px-3 py-8 text-center text-sm text-muted">
          {loading ? 'Searching…' : isLearner && trimmed.length < 2 ? 'Type at least 2 characters to search content.' : 'No matches found.'}
        </p>
      )}
      {failed ? (
        <p className="px-5 pb-3 text-2xs text-muted">Content search is unavailable right now — showing pages only.</p>
      ) : null}
      <p className="sr-only" aria-live="polite">{`${rows.length} result${rows.length === 1 ? '' : 's'}`}</p>
    </>
  );
}

const subscribeToNothing = () => () => {};
const isApplePlatform = () => /Mac|iP(hone|ad|od)/.test(navigator.platform);

/** Opens the palette: a search bar, or an icon button for dense headers. */
export function SearchTrigger({
  variant = 'bar',
  onClick,
  className,
}: {
  variant?: 'bar' | 'icon';
  onClick: () => void;
  className?: string;
}) {
  // Server snapshot is false, so SSR and the first client render agree.
  const apple = useSyncExternalStore(subscribeToNothing, isApplePlatform, () => false);
  const handleClick = () => {
    void triggerImpactHaptic('LIGHT');
    onClick();
  };

  if (variant === 'icon') {
    return (
      <button
        type="button"
        onClick={handleClick}
        aria-label="Search"
        aria-keyshortcuts="Control+K Meta+K"
        title={apple ? 'Search (⌘K)' : 'Search (Ctrl+K)'}
        className={cn('inline-flex items-center justify-center rounded-lg p-2.5 text-muted transition-colors hover:bg-primary/10 hover:text-primary', className)}
      >
        <Search className="size-5" aria-hidden="true" />
      </button>
    );
  }

  return (
    <button
      type="button"
      onClick={handleClick}
      aria-label="Search anything"
      aria-keyshortcuts="Control+K Meta+K"
      className={cn(
        'group flex h-11 w-full items-center gap-2.5 rounded-xl px-3.5 text-left text-sm text-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40',
        HEADER_CHIP,
        HEADER_CHIP_HOVER,
        className,
      )}
    >
      <Search className="h-4 w-4 shrink-0 text-muted" aria-hidden="true" />
      <span className="flex-1 truncate">Search anything...</span>
      <kbd className="hidden shrink-0 items-center gap-0.5 rounded-md border border-border bg-background-light px-1.5 py-0.5 text-2xs font-semibold text-muted sm:inline-flex">
        {apple ? '⌘ K' : 'Ctrl K'}
      </kbd>
    </button>
  );
}
