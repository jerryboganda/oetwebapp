'use client';

import { Search, Command as CommandIcon, ArrowRight, Clock } from 'lucide-react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { AnimatePresence, motion, useReducedMotion } from 'motion/react';
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { cn } from '@/lib/utils';
import { motionTokens, prefersReducedMotion } from '@/lib/motion';

// ─────────────────────────────────────────────────────────────────────────────
// CommandPalette — ⌘K / Ctrl+K overlay for admin navigation.
//
// Design contract: DESIGN.md §5.17 & §16. Registered globally by
// AdminDashboardShell. Built on Modal-style overlay + focusable Input.
// ─────────────────────────────────────────────────────────────────────────────

export interface CommandItem {
  id: string;
  label: string;
  section?: string;
  description?: string;
  keywords?: string;
  href?: string;
  icon?: ReactNode;
  onSelect?: () => void;
  /** Rendered on the right-hand side of the row (e.g. shortcut chip). */
  shortcut?: string;
}

interface CommandPaletteContextValue {
  open: () => void;
  close: () => void;
  isOpen: boolean;
}

const CommandPaletteContext = createContext<CommandPaletteContextValue | null>(null);

export function useCommandPalette() {
  const ctx = useContext(CommandPaletteContext);
  return ctx;
}

interface CommandPaletteProps {
  items: CommandItem[];
  /** Storage key for recent items. Defaults to `command-palette.recents`. */
  recentsKey?: string;
  placeholder?: string;
  children?: ReactNode;
}

function fuzzyScore(query: string, target: string): number {
  if (!query) return 1;
  const q = query.toLowerCase();
  const t = target.toLowerCase();
  if (t === q) return 1000;
  if (t.startsWith(q)) return 500;
  if (t.includes(q)) return 300;
  // Character-sequence match.
  let qi = 0;
  for (let i = 0; i < t.length && qi < q.length; i += 1) {
    if (t[i] === q[qi]) qi += 1;
  }
  return qi === q.length ? 100 - (t.length - q.length) : 0;
}

function scoreItem(query: string, item: CommandItem): number {
  if (!query) return 1;
  const label = fuzzyScore(query, item.label);
  const section = item.section ? fuzzyScore(query, item.section) * 0.3 : 0;
  const keywords = item.keywords ? fuzzyScore(query, item.keywords) * 0.5 : 0;
  return Math.max(label, section, keywords);
}

export function CommandPalette({
  items,
  recentsKey = 'command-palette.recents',
  placeholder = 'Search admin workspace…',
  children,
}: CommandPaletteProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [activeIndex, setActiveIndex] = useState(0);
  const [recents, setRecents] = useState<string[]>([]);
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const router = useRouter();
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);

  const open = useCallback(() => setIsOpen(true), []);
  const close = useCallback(() => {
    setIsOpen(false);
    setQuery('');
    setActiveIndex(0);
  }, []);

  // Load recents on mount.
  useEffect(() => {
    if (typeof window === 'undefined') return;
    try {
      const raw = window.localStorage.getItem(recentsKey);
      if (raw) {
        const parsed = JSON.parse(raw);
        // eslint-disable-next-line react-hooks/set-state-in-effect -- hydrating recents from localStorage on mount
        if (Array.isArray(parsed)) setRecents(parsed.filter((x) => typeof x === 'string').slice(0, 5));
      }
    } catch {
      // Ignore corrupt storage.
    }
  }, [recentsKey]);

  // Global ⌘K / Ctrl+K listener.
  useEffect(() => {
    function handler(event: KeyboardEvent) {
      const meta = event.metaKey || event.ctrlKey;
      if (meta && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        setIsOpen((prev) => !prev);
      } else if (event.key === 'Escape' && isOpen) {
        event.preventDefault();
        close();
      }
    }
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [close, isOpen]);

  // Focus input when opened.
  useEffect(() => {
    if (isOpen) {
      const timeout = window.setTimeout(() => {
        inputRef.current?.focus();
      }, 60);
      return () => window.clearTimeout(timeout);
    }
  }, [isOpen]);

  const filtered = useMemo(() => {
    if (!query.trim()) {
      // Recents first, then everything else grouped by section.
      const recentSet = new Set(recents);
      const recentItems = recents
        .map((id) => items.find((x) => x.id === id))
        .filter((x): x is CommandItem => Boolean(x));
      const rest = items.filter((x) => !recentSet.has(x.id));
      return { recents: recentItems, results: rest };
    }
    const scored = items
      .map((item) => ({ item, score: scoreItem(query, item) }))
      .filter((x) => x.score > 0)
      .sort((a, b) => b.score - a.score)
      .map((x) => x.item);
    return { recents: [] as CommandItem[], results: scored };
  }, [items, query, recents]);

  const flat = useMemo(() => [...filtered.recents, ...filtered.results], [filtered]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- clamp active index when filtered list shrinks
    if (activeIndex >= flat.length) setActiveIndex(Math.max(0, flat.length - 1));
  }, [activeIndex, flat.length]);

  const recordRecent = useCallback(
    (id: string) => {
      setRecents((prev) => {
        const next = [id, ...prev.filter((x) => x !== id)].slice(0, 5);
        try {
          window.localStorage.setItem(recentsKey, JSON.stringify(next));
        } catch {
          /* ignore */
        }
        return next;
      });
    },
    [recentsKey],
  );

  const runItem = useCallback(
    (item: CommandItem) => {
      recordRecent(item.id);
      close();
      if (item.onSelect) {
        item.onSelect();
      } else if (item.href) {
        router.push(item.href);
      }
    },
    [close, recordRecent, router],
  );

  const handleKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActiveIndex((prev) => Math.min(flat.length - 1, prev + 1));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActiveIndex((prev) => Math.max(0, prev - 1));
    } else if (event.key === 'Enter') {
      event.preventDefault();
      const item = flat[activeIndex];
      if (item) runItem(item);
    } else if (event.key === 'Home') {
      event.preventDefault();
      setActiveIndex(0);
    } else if (event.key === 'End') {
      event.preventDefault();
      setActiveIndex(flat.length - 1);
    }
  };

  const renderRow = (item: CommandItem, index: number, section?: string) => {
    const active = index === activeIndex;
    const body = (
      <div
        className={cn(
          'group flex cursor-pointer items-center gap-3 rounded-xl px-3 py-2.5 text-sm transition-colors',
          active ? 'bg-primary/10 text-primary' : 'text-navy hover:bg-background-light',
        )}
      >
        {item.icon ? (
          <span
            className={cn(
              'flex h-8 w-8 shrink-0 items-center justify-center rounded-lg',
              active ? 'bg-primary/15 text-primary' : 'bg-background-light text-muted',
            )}
          >
            {item.icon}
          </span>
        ) : null}
        <span className="min-w-0 flex-1">
          <span className="flex flex-wrap items-center gap-2">
            <span className="truncate font-semibold">{item.label}</span>
            {section ? (
              <span className="text-[10px] font-semibold uppercase tracking-[0.14em] text-muted">
                {section}
              </span>
            ) : null}
          </span>
          {item.description ? (
            <span className="mt-0.5 block truncate text-xs text-muted">{item.description}</span>
          ) : null}
        </span>
        {item.shortcut ? (
          <kbd className="hidden rounded-md border border-border bg-background-light px-1.5 py-0.5 text-[10px] font-semibold text-muted sm:inline">
            {item.shortcut}
          </kbd>
        ) : null}
        <ArrowRight className={cn('h-4 w-4 shrink-0', active ? 'text-primary' : 'text-muted/70')} aria-hidden />
      </div>
    );

    const onMouseEnter = () => setActiveIndex(index);
    const onClick = () => runItem(item);

    if (item.href) {
      return (
        <li key={item.id} role="option" aria-selected={active}>
          <Link
            href={item.href}
            onClick={(event) => {
              // Let the runItem close-and-navigate handle this; prevent double-nav.
              event.preventDefault();
              onClick();
            }}
            onMouseEnter={onMouseEnter}
          >
            {body}
          </Link>
        </li>
      );
    }

    return (
      <li
        key={item.id}
        role="option"
        aria-selected={active}
        onMouseEnter={onMouseEnter}
        onClick={onClick}
      >
        {body}
      </li>
    );
  };

  const ctxValue = useMemo(() => ({ open, close, isOpen }), [open, close, isOpen]);

  return (
    <CommandPaletteContext.Provider value={ctxValue}>
      {children}
      <AnimatePresence>
        {isOpen && (
          <motion.div
            className="fixed inset-0 z-[110] flex items-start justify-center px-4 pt-[10vh] sm:pt-[12vh]"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={
              reducedMotion
                ? { duration: motionTokens.duration.instant }
                : { duration: motionTokens.duration.fast, ease: motionTokens.ease.entrance }
            }
          >
            <div
              className="absolute inset-0 bg-navy/50 backdrop-blur-sm"
              onClick={close}
              aria-hidden
            />
            <motion.div
              role="dialog"
              aria-label="Command palette"
              aria-modal="true"
              initial={reducedMotion ? { opacity: 0 } : { opacity: 0, y: -14, scale: 0.98 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              exit={reducedMotion ? { opacity: 0 } : { opacity: 0, y: -10, scale: 0.985 }}
              transition={reducedMotion ? { duration: motionTokens.duration.instant } : motionTokens.spring.overlay}
              className="relative z-10 flex w-full max-w-xl flex-col overflow-hidden rounded-[20px] border border-border bg-surface shadow-clinical"
            >
              <div className="flex items-center gap-3 border-b border-border px-4 py-3">
                <Search className="h-4 w-4 text-muted" aria-hidden />
                <input
                  ref={inputRef}
                  value={query}
                  onChange={(event) => {
                    setQuery(event.target.value);
                    setActiveIndex(0);
                  }}
                  onKeyDown={handleKeyDown}
                  placeholder={placeholder}
                  className="min-w-0 flex-1 bg-transparent text-sm text-navy placeholder:text-muted focus:outline-none"
                  aria-label="Search commands"
                  autoComplete="off"
                  spellCheck={false}
                />
                <kbd className="hidden rounded-md border border-border bg-background-light px-1.5 py-0.5 text-[10px] font-semibold text-muted sm:inline">
                  Esc
                </kbd>
              </div>

              <ul
                ref={listRef}
                role="listbox"
                className="max-h-[min(62vh,28rem)] flex-1 overflow-y-auto p-2"
              >
                {flat.length === 0 ? (
                  <li className="flex flex-col items-center gap-2 px-4 py-12 text-center">
                    <Search className="h-6 w-6 text-muted" aria-hidden />
                    <p className="text-sm font-semibold text-navy">No matches</p>
                    <p className="text-xs text-muted">
                      Try a different search term, section name, or keyword.
                    </p>
                  </li>
                ) : (
                  <>
                    {filtered.recents.length > 0 && (
                      <li className="px-2 pt-1 pb-1.5 text-[10px] font-semibold uppercase tracking-[0.18em] text-muted inline-flex items-center gap-1.5">
                        <Clock className="h-3 w-3" aria-hidden /> Recent
                      </li>
                    )}
                    {filtered.recents.map((item, index) => renderRow(item, index, item.section))}
                    {filtered.results.length > 0 && filtered.recents.length > 0 && (
                      <li className="px-2 pt-3 pb-1.5 text-[10px] font-semibold uppercase tracking-[0.18em] text-muted">
                        All destinations
                      </li>
                    )}
                    {filtered.results.map((item, index) =>
                      renderRow(item, index + filtered.recents.length, item.section),
                    )}
                  </>
                )}
              </ul>

              <div className="flex items-center justify-between gap-2 border-t border-border bg-background-light px-4 py-2 text-[11px] text-muted">
                <span className="inline-flex items-center gap-1.5">
                  <CommandIcon className="h-3 w-3" aria-hidden />
                  <span>Navigate with</span>
                  <kbd className="rounded border border-border bg-surface px-1 text-[10px]">↑</kbd>
                  <kbd className="rounded border border-border bg-surface px-1 text-[10px]">↓</kbd>
                </span>
                <span className="inline-flex items-center gap-1.5">
                  <span>Select</span>
                  <kbd className="rounded border border-border bg-surface px-1 text-[10px]">⏎</kbd>
                </span>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </CommandPaletteContext.Provider>
  );
}

// Tiny trigger button suitable for nav bars.
export function CommandPaletteTrigger({ className, label = 'Quick find', shortcut = '⌘K' }: { className?: string; label?: string; shortcut?: string }) {
  const ctx = useCommandPalette();
  if (!ctx) return null;
  return (
    <button
      type="button"
      onClick={ctx.open}
      className={cn(
        'inline-flex items-center gap-2 rounded-full border border-border bg-background-light px-3 py-1.5 text-xs font-semibold text-muted transition-colors hover:border-border-hover hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
        className,
      )}
    >
      <Search className="h-3.5 w-3.5" aria-hidden />
      <span>{label}</span>
      <kbd className="rounded border border-border bg-surface px-1 text-[10px] font-semibold">{shortcut}</kbd>
    </button>
  );
}
