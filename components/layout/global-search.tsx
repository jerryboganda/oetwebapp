'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { CornerDownLeft, FileText, Loader2, Search, X } from 'lucide-react';
import { searchContent } from '@/lib/api';
import { cn } from '@/lib/utils';
import { learnerMainNavItems, type NavItem } from './sidebar';

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

interface ResultRow {
  key: string;
  label: string;
  hint?: string;
  href: string;
  group: 'Go to' | 'Content';
}

const EXTRA_DESTINATIONS: { label: string; href: string }[] = [
  { label: 'Settings', href: '/settings' },
  { label: 'Notification settings', href: '/settings/notifications' },
  { label: 'Achievements', href: '/achievements' },
  { label: 'Help & Support', href: '/support' },
];

/** Content rows point at the module that owns them; there is no per-item route. */
function hrefForContent(item: ContentSearchItem): string {
  const subtest = (item.subtestCode ?? '').toLowerCase();
  if (['listening', 'reading', 'writing', 'speaking'].includes(subtest)) return `/${subtest}`;
  return '/materials';
}

export function GlobalSearch({ className }: { className?: string }) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<ContentSearchItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const requestIdRef = useRef(0);

  // ⌘K / Ctrl+K anywhere opens the palette.
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        setOpen((current) => !current);
      }
      if (event.key === 'Escape') setOpen(false);
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, []);

  useEffect(() => {
    if (open) {
      const id = window.setTimeout(() => inputRef.current?.focus(), 40);
      return () => window.clearTimeout(id);
    }
    setQuery('');
    setResults([]);
    setFailed(false);
    setActiveIndex(0);
    return undefined;
  }, [open]);

  useEffect(() => {
    if (!open) return undefined;
    const trimmed = query.trim();
    if (debounceRef.current) clearTimeout(debounceRef.current);
    if (trimmed.length < 2) {
      setResults([]);
      setLoading(false);
      setFailed(false);
      return undefined;
    }

    setLoading(true);
    const requestId = ++requestIdRef.current;
    debounceRef.current = setTimeout(async () => {
      try {
        const response = (await searchContent({ q: trimmed, pageSize: 8 })) as ContentSearchResponse;
        if (requestId !== requestIdRef.current) return;
        setResults(response?.items ?? []);
        setFailed(false);
      } catch {
        if (requestId !== requestIdRef.current) return;
        // Search is a convenience surface — degrade to navigation-only rather
        // than blocking the palette behind an error state.
        setResults([]);
        setFailed(true);
      } finally {
        if (requestId === requestIdRef.current) setLoading(false);
      }
    }, 300);

    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, [open, query]);

  const rows = useMemo<ResultRow[]>(() => {
    const trimmed = query.trim().toLowerCase();
    const destinations: { label: string; href: string }[] = [
      ...learnerMainNavItems.map((item: NavItem) => ({ label: item.label, href: item.href })),
      ...EXTRA_DESTINATIONS,
    ];
    const navRows: ResultRow[] = destinations
      .filter((item) => (trimmed ? item.label.toLowerCase().includes(trimmed) : true))
      .slice(0, trimmed ? 5 : 6)
      .map((item) => ({ key: `nav:${item.href}`, label: item.label, href: item.href, group: 'Go to' as const }));

    const contentRows: ResultRow[] = results.map((item) => ({
      key: `content:${item.id}`,
      label: item.title,
      hint: [item.subtestCode, item.contentType, item.difficulty].filter(Boolean).join(' · ') || undefined,
      href: hrefForContent(item),
      group: 'Content' as const,
    }));

    return [...navRows, ...contentRows];
  }, [query, results]);

  useEffect(() => {
    setActiveIndex((current) => (current >= rows.length ? 0 : current));
  }, [rows.length]);

  const go = useCallback(
    (href: string) => {
      setOpen(false);
      router.push(href);
    },
    [router],
  );

  const onInputKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActiveIndex((current) => (rows.length ? (current + 1) % rows.length : 0));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActiveIndex((current) => (rows.length ? (current - 1 + rows.length) % rows.length : 0));
    } else if (event.key === 'Enter') {
      event.preventDefault();
      const row = rows[activeIndex];
      if (row) go(row.href);
    }
  };

  let lastGroup: string | null = null;

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        className={cn(
          'group flex h-9 w-full items-center gap-2.5 rounded-full border border-border bg-surface px-3.5 text-left text-[13px] text-muted shadow-sm transition-colors hover:border-border-hover focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40',
          className,
        )}
        aria-label="Search anything"
      >
        <Search className="h-4 w-4 shrink-0 text-muted" aria-hidden="true" />
        <span className="flex-1 truncate">Search anything...</span>
        <kbd className="hidden shrink-0 items-center gap-0.5 rounded-md border border-border bg-background-light px-1.5 py-0.5 text-[10.5px] font-semibold text-muted sm:inline-flex">
          ⌘ K
        </kbd>
      </button>

      {open ? (
        <div className="fixed inset-0 z-[110] flex items-start justify-center overlay-safe-area px-4 pt-[12vh]" role="dialog" aria-modal="true" aria-label="Search">
          <button
            type="button"
            aria-label="Close search"
            onClick={() => setOpen(false)}
            className="absolute inset-0 bg-navy/30 backdrop-blur-[2px]"
          />
          <div className="relative z-10 w-full max-w-xl overflow-hidden rounded-2xl border border-border bg-surface shadow-2xl">
            <div className="flex items-center gap-2.5 border-b border-border px-4">
              <Search className="h-4 w-4 shrink-0 text-muted" aria-hidden="true" />
              <input
                ref={inputRef}
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                onKeyDown={onInputKeyDown}
                placeholder="Search content, pages and practice…"
                className="h-12 flex-1 bg-transparent text-[14px] text-navy placeholder:text-muted focus:outline-none"
              />
              {loading ? <Loader2 className="h-4 w-4 shrink-0 animate-spin text-muted" aria-hidden="true" /> : null}
              <button
                type="button"
                onClick={() => setOpen(false)}
                className="shrink-0 rounded-md p-1 text-muted transition-colors hover:bg-background-light hover:text-navy"
                aria-label="Close"
              >
                <X className="h-4 w-4" />
              </button>
            </div>

            <div className="max-h-[52vh] overflow-y-auto p-2">
              {rows.length === 0 ? (
                <p className="px-3 py-8 text-center text-[13px] text-muted">
                  {query.trim().length < 2 ? 'Type at least 2 characters to search.' : 'No matches found.'}
                </p>
              ) : (
                rows.map((row, index) => {
                  const showHeading = row.group !== lastGroup;
                  lastGroup = row.group;
                  const active = index === activeIndex;
                  return (
                    <div key={row.key}>
                      {showHeading ? (
                        <p className="px-3 pb-1 pt-2.5 text-[10.5px] font-bold uppercase tracking-[0.14em] text-muted">
                          {row.group}
                        </p>
                      ) : null}
                      <button
                        type="button"
                        onMouseEnter={() => setActiveIndex(index)}
                        onClick={() => go(row.href)}
                        className={cn(
                          'flex w-full items-center gap-2.5 rounded-lg px-3 py-2 text-left transition-colors',
                          active ? 'bg-primary/10 text-navy' : 'text-navy hover:bg-background-light',
                        )}
                      >
                        <FileText className="h-4 w-4 shrink-0 text-muted" aria-hidden="true" />
                        <span className="min-w-0 flex-1">
                          <span className="block truncate text-[13.5px] font-medium">{row.label}</span>
                          {row.hint ? <span className="block truncate text-[11.5px] text-muted">{row.hint}</span> : null}
                        </span>
                        {active ? <CornerDownLeft className="h-3.5 w-3.5 shrink-0 text-muted" aria-hidden="true" /> : null}
                      </button>
                    </div>
                  );
                })
              )}
              {failed && query.trim().length >= 2 ? (
                <p className="px-3 pb-2 pt-1 text-[11.5px] text-muted">
                  Content search is unavailable right now — showing pages only.
                </p>
              ) : null}
            </div>
          </div>
        </div>
      ) : null}
    </>
  );
}
