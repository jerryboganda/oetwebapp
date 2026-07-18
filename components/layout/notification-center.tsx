'use client';

import * as Popover from '@radix-ui/react-popover';
import { forwardRef, useEffect, useMemo, useState, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import {
  AlertTriangle,
  Bell,
  BellOff,
  BookOpen,
  Calendar,
  CheckCheck,
  CheckCircle2,
  ChevronRight,
  CreditCard,
  FileText,
  GraduationCap,
  Inbox,
  Info,
  Megaphone,
  RefreshCw,
  Settings,
  Shield,
  Sparkles,
  Target,
  UserCog,
  WifiOff,
  X,
} from 'lucide-react';
import Link from 'next/link';
import { motion, useReducedMotion } from 'motion/react';
import { getMotionDelay, getSurfaceTransition, getSurfaceVariants, prefersReducedMotion } from '@/lib/motion';
import { useNotificationCenter, useNotificationState } from '@/contexts/notification-center-context';
import { Button } from '@/components/ui/button';
import { Drawer } from '@/components/ui/modal';
import { cn } from '@/lib/utils';
import type { NotificationFeedItem, NotificationSeverity } from '@/lib/types/notifications';

/* ────────────────────────────────────────────────────────────── */
/*  Helpers                                                       */
/* ────────────────────────────────────────────────────────────── */

function relativeTime(value: string): string {
  const now = Date.now();
  const date = new Date(value);
  const diffMs = now - date.getTime();
  const diffSec = Math.floor(diffMs / 1000);
  const diffMin = Math.floor(diffSec / 60);
  const diffHr = Math.floor(diffMin / 60);
  const diffDay = Math.floor(diffHr / 24);

  if (diffSec < 60) return 'Just now';
  if (diffMin < 60) return `${diffMin}m ago`;
  if (diffHr < 24) return `${diffHr}h ago`;
  if (diffDay === 1) return 'Yesterday';
  if (diffDay < 7) return `${diffDay}d ago`;
  return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

function humanizeCategory(raw: string): string {
  return raw.split('_').map((w) => w.charAt(0).toUpperCase() + w.slice(1)).join(' ');
}

/* ── Severity visuals ── */

const SEVERITY_STYLES: Record<NotificationSeverity, { bg: string; text: string; accent: string; icon: ReactNode }> = {
  info:     { bg: 'bg-blue-50 dark:bg-blue-950',       text: 'text-blue-600 dark:text-blue-400',       accent: 'bg-blue-500',    icon: <Info className="h-4 w-4" /> },
  success:  { bg: 'bg-emerald-50 dark:bg-emerald-950', text: 'text-emerald-600 dark:text-emerald-400', accent: 'bg-emerald-500', icon: <CheckCircle2 className="h-4 w-4" /> },
  warning:  { bg: 'bg-amber-50 dark:bg-amber-950',     text: 'text-amber-600 dark:text-amber-400',     accent: 'bg-amber-500',   icon: <AlertTriangle className="h-4 w-4" /> },
  critical: { bg: 'bg-red-50 dark:bg-red-950',         text: 'text-red-600 dark:text-red-400',         accent: 'bg-red-500',     icon: <Shield className="h-4 w-4" /> },
};

/* ── Category icons ── */

const CATEGORY_ICONS: Record<string, ReactNode> = {
  results:     <Target className="h-4 w-4" />,
  reviews:     <FileText className="h-4 w-4" />,
  study_plan:  <BookOpen className="h-4 w-4" />,
  reminders:   <Calendar className="h-4 w-4" />,
  readiness:   <Sparkles className="h-4 w-4" />,
  engagement:  <Sparkles className="h-4 w-4" />,
  billing:     <CreditCard className="h-4 w-4" />,
  account:     <UserCog className="h-4 w-4" />,
  freeze:      <AlertTriangle className="h-4 w-4" />,
  review_ops:  <FileText className="h-4 w-4" />,
  calibration: <Target className="h-4 w-4" />,
  schedule:    <Calendar className="h-4 w-4" />,
  operations:  <Settings className="h-4 w-4" />,
};

function itemIcon(item: NotificationFeedItem): ReactNode {
  const catIcon = CATEGORY_ICONS[item.category];
  if (catIcon) return catIcon;
  return SEVERITY_STYLES[item.severity]?.icon ?? <Megaphone className="h-4 w-4" />;
}

/* ── Date grouping ── */

function dateGroupLabel(value: string): string {
  const date = new Date(value);
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const yesterday = new Date(today.getTime() - 86_400_000);
  const itemDate = new Date(date.getFullYear(), date.getMonth(), date.getDate());

  if (itemDate.getTime() >= today.getTime()) return 'Today';
  if (itemDate.getTime() >= yesterday.getTime()) return 'Yesterday';
  return 'Earlier';
}

type DateGroup = { label: string; items: NotificationFeedItem[] };

function groupByDate(items: NotificationFeedItem[]): DateGroup[] {
  const groups: DateGroup[] = [];
  let currentLabel = '';
  for (const item of items) {
    const label = dateGroupLabel(item.createdAt);
    if (label !== currentLabel) {
      currentLabel = label;
      groups.push({ label, items: [item] });
    } else {
      groups[groups.length - 1]!.items.push(item);
    }
  }
  return groups;
}

type CategoryFacet = { key: string; label: string; total: number; unread: number };

function buildCategoryFacets(items: NotificationFeedItem[]): CategoryFacet[] {
  const map = new Map<string, { total: number; unread: number }>();
  for (const item of items) {
    const entry = map.get(item.category) ?? { total: 0, unread: 0 };
    entry.total += 1;
    if (!item.isRead) entry.unread += 1;
    map.set(item.category, entry);
  }
  return Array.from(map.entries())
    .map(([key, value]) => ({ key, label: humanizeCategory(key), ...value }))
    .sort((left, right) => right.unread - left.unread || right.total - left.total || left.label.localeCompare(right.label));
}

/* ────────────────────────────────────────────────────────────── */
/*  Small building blocks                                         */
/* ────────────────────────────────────────────────────────────── */

function HeaderIconButton({
  label,
  onClick,
  href,
  children,
}: {
  label: string;
  onClick?: () => void;
  href?: string;
  children: ReactNode;
}) {
  const className =
    'inline-flex h-9 w-9 items-center justify-center rounded-lg text-muted/70 transition-colors hover:bg-background-light hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/50';
  if (href) {
    return (
      <Link href={href} onClick={onClick} aria-label={label} className={className}>
        {children}
      </Link>
    );
  }
  return (
    <button type="button" onClick={onClick} aria-label={label} className={className}>
      {children}
    </button>
  );
}

/* ────────────────────────────────────────────────────────────── */
/*  Notification Item                                             */
/* ────────────────────────────────────────────────────────────── */

function NotificationItem({
  item,
  index,
  reducedMotion,
  onOpen,
}: {
  item: NotificationFeedItem;
  index: number;
  reducedMotion: boolean;
  onOpen: (item: NotificationFeedItem) => void;
}) {
  const itemVariants = getSurfaceVariants('item', reducedMotion);
  const itemTransition = getSurfaceTransition('item', reducedMotion);
  const severity = SEVERITY_STYLES[item.severity] ?? SEVERITY_STYLES.info;

  return (
    <motion.button
      type="button"
      data-read={item.isRead ? 'true' : 'false'}
      variants={itemVariants}
      initial="hidden"
      animate="visible"
      transition={{ ...itemTransition, delay: getMotionDelay(index, reducedMotion) }}
      onClick={() => onOpen(item)}
      className={cn(
        'group relative flex w-full items-start gap-3 overflow-hidden rounded-xl px-3 py-3 text-left transition-colors duration-200',
        item.isRead
          ? 'hover:bg-background-light'
          : 'bg-primary/[0.04] hover:bg-primary/[0.07] dark:bg-primary/[0.10] dark:hover:bg-primary/[0.14]',
      )}
    >
      {/* ── Severity accent (unread only) ── */}
      {!item.isRead && <span className={cn('absolute inset-y-2 left-0 w-[3px] rounded-full', severity.accent)} />}

      {/* ── Avatar icon ── */}
      <div className={cn('mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-xl', severity.bg, severity.text)}>
        {itemIcon(item)}
      </div>

      {/* ── Content ── */}
      <div className="min-w-0 flex-1">
        <div className="flex items-start justify-between gap-2">
          <p className={cn('text-[13px] leading-snug', item.isRead ? 'font-medium text-navy/75' : 'font-semibold text-navy')}>
            {item.title}
          </p>
          <span className="mt-0.5 shrink-0 text-[11px] tabular-nums text-muted/70">{relativeTime(item.createdAt)}</span>
        </div>
        <p className="mt-0.5 line-clamp-2 text-[12.5px] leading-relaxed text-muted">{item.body}</p>
        <div className="mt-1.5 flex items-center gap-2">
          <span className="inline-flex items-center gap-1 rounded-md bg-background-light px-1.5 py-0.5 text-[10.5px] font-medium text-muted/70">
            <GraduationCap className="h-3 w-3" />
            {humanizeCategory(item.category)}
          </span>
          {item.actionUrl && (
            <span className="inline-flex items-center gap-0.5 text-[10.5px] font-medium text-primary/0 transition-colors group-hover:text-primary/70">
              View
              <ChevronRight className="h-3 w-3" />
            </span>
          )}
        </div>
      </div>

      {/* ── Unread dot ── */}
      {!item.isRead && (
        <span className="absolute right-3 top-3 h-2 w-2 rounded-full bg-primary shadow-sm shadow-primary/30" />
      )}
    </motion.button>
  );
}

/* ────────────────────────────────────────────────────────────── */
/*  Panel Content                                                 */
/* ────────────────────────────────────────────────────────────── */

function NotificationCenterContent({
  variant,
  onNavigate,
  onClose,
}: {
  variant: 'popover' | 'drawer';
  onNavigate?: () => void;
  onClose?: () => void;
}) {
  const {
    notifications,
    unreadCount,
    totalCount,
    hasMore,
    isLoading,
    isRefreshing,
    error,
    connectionStatus,
    refreshFeed,
    loadMore,
    markRead,
    markAllRead,
  } = useNotificationCenter();
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const [tab, setTab] = useState<'all' | 'unread'>('all');
  const [category, setCategory] = useState<string | null>(null);

  const facets = useMemo(() => buildCategoryFacets(notifications), [notifications]);

  // If the active category filter disappears (feed refreshed away), reset to All.
  useEffect(() => {
    if (category && !facets.some((facet) => facet.key === category)) {
      setCategory(null);
    }
  }, [category, facets]);

  const filtered = useMemo(() => {
    let list = notifications;
    if (tab === 'unread') list = list.filter((n) => !n.isRead);
    if (category) list = list.filter((n) => n.category === category);
    return list;
  }, [tab, category, notifications]);
  const dateGroups = useMemo(() => groupByDate(filtered), [filtered]);

  const handleOpen = async (item: NotificationFeedItem) => {
    if (!item.isRead) {
      try { await markRead(item.id); } catch { /* keep actionable */ }
    }
    onNavigate?.();
    if (item.actionUrl) window.location.assign(item.actionUrl);
  };

  const isDrawer = variant === 'drawer';
  let globalIdx = 0;

  return (
    <div
      className={cn(
        'flex flex-col',
        isDrawer ? 'h-full overflow-hidden' : 'max-h-[min(34rem,80vh)]',
      )}
    >

      {/* ━━ Header ━━ */}
      <div className="flex shrink-0 items-center justify-between gap-2 border-b border-border/70 pb-3">
        <div className="flex min-w-0 items-center gap-2.5">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <Bell className="h-[18px] w-[18px]" />
          </div>
          <div className="min-w-0">
            <h2 className="truncate text-[15px] font-semibold tracking-tight text-navy">Notifications</h2>
            <p className="mt-0.5 text-[11px] text-muted/70">
              {unreadCount > 0 ? `${unreadCount} unread · ${totalCount} total` : 'All caught up'}
            </p>
          </div>
        </div>
        <div className="flex shrink-0 items-center gap-0.5">
          <HeaderIconButton label="Refresh" onClick={() => void refreshFeed({ reset: true })}>
            <RefreshCw className={cn('h-4 w-4', isRefreshing && 'animate-spin')} />
          </HeaderIconButton>
          <HeaderIconButton label="Notification settings" href="/settings/notifications" onClick={() => onNavigate?.()}>
            <Settings className="h-4 w-4" />
          </HeaderIconButton>
          {isDrawer && (
            <HeaderIconButton label="Close" onClick={() => onClose?.()}>
              <X className="h-[18px] w-[18px]" />
            </HeaderIconButton>
          )}
        </div>
      </div>

      {/* ━━ Tabs + Mark all read ━━ */}
      <div className="flex shrink-0 items-center justify-between px-0.5 pt-3">
        <div className="flex rounded-lg bg-background-light p-0.5">
          {(['all', 'unread'] as const).map((value) => (
            <button
              key={value}
              type="button"
              onClick={() => setTab(value)}
              className={cn(
                'rounded-md px-3 py-1.5 text-[12px] font-semibold transition-[color,background-color,box-shadow] duration-150',
                tab === value
                  ? 'bg-surface text-navy shadow-sm'
                  : 'text-muted hover:text-navy',
              )}
            >
              {value === 'all' ? 'All' : `Unread${unreadCount > 0 ? ` (${unreadCount})` : ''}`}
            </button>
          ))}
        </div>
        <button
          type="button"
          onClick={() => void markAllRead()}
          disabled={unreadCount === 0}
          className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-[11.5px] font-medium text-primary transition-colors hover:bg-primary/5 disabled:cursor-default disabled:text-muted/40 disabled:hover:bg-transparent"
          aria-label="Mark all read"
        >
          <CheckCheck className="h-3.5 w-3.5" />
          Mark all read
        </button>
      </div>

      {/* ━━ Category filter chips ━━ */}
      {facets.length > 1 && (
        <div className="-mx-1 mt-2 flex shrink-0 gap-1.5 overflow-x-auto px-1 pb-0.5 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
          <button
            type="button"
            onClick={() => setCategory(null)}
            className={cn(
              'shrink-0 rounded-full px-3 py-1 text-[11.5px] font-medium transition-colors',
              category === null ? 'bg-primary text-white shadow-sm shadow-primary/20' : 'bg-background-light text-muted hover:text-navy',
            )}
          >
            All
          </button>
          {facets.map((facet) => (
            <button
              key={facet.key}
              type="button"
              onClick={() => setCategory(facet.key)}
              className={cn(
                'inline-flex shrink-0 items-center gap-1.5 rounded-full px-3 py-1 text-[11.5px] font-medium transition-colors',
                category === facet.key ? 'bg-primary text-white shadow-sm shadow-primary/20' : 'bg-background-light text-muted hover:text-navy',
              )}
            >
              {facet.label}
              {facet.unread > 0 && (
                <span
                  className={cn(
                    'inline-flex h-4 min-w-4 items-center justify-center rounded-full px-1 text-[10px] font-bold tabular-nums',
                    category === facet.key ? 'bg-white/25 text-white' : 'bg-primary/15 text-primary',
                  )}
                >
                  {facet.unread}
                </span>
              )}
            </button>
          ))}
        </div>
      )}

      {/* ━━ Connection warning ━━ */}
      {(connectionStatus === 'reconnecting' || connectionStatus === 'disconnected') && (
        <div className="mx-0.5 mt-2 flex shrink-0 items-center gap-2 rounded-lg bg-amber-50/80 px-3 py-2 text-[12px] text-amber-700 dark:bg-amber-950/40 dark:text-amber-300">
          <WifiOff className="h-3.5 w-3.5 shrink-0" />
          {connectionStatus === 'reconnecting' ? 'Reconnecting…' : 'Offline. Updates may be delayed.'}
        </div>
      )}

      {/* ━━ Error ━━ */}
      {error && (
        <div className="mx-0.5 mt-2 shrink-0 rounded-lg bg-red-50/80 px-3 py-2 text-[12px] text-red-700 dark:bg-red-950/40 dark:text-red-300">{error}</div>
      )}

      {/* ━━ Notification list ━━ */}
      <div className="mt-2 flex min-h-0 flex-1 flex-col overflow-y-auto overscroll-contain">
        {isLoading && filtered.length === 0 && (
          <div className="flex flex-1 items-center justify-center py-16">
            <div className="h-7 w-7 animate-spin rounded-full border-[2.5px] border-primary/20 border-t-primary" />
          </div>
        )}

        {!isLoading && filtered.length === 0 && (
          <div className="flex flex-1 flex-col items-center justify-center gap-4 px-6 py-16 text-center">
            <div className={cn(
              'flex h-16 w-16 items-center justify-center rounded-2xl',
              tab === 'unread' || category ? 'bg-emerald-50 dark:bg-emerald-950/40' : 'bg-primary/5',
            )}>
              {tab === 'unread' || category
                ? <BellOff className="h-8 w-8 text-emerald-500/70" />
                : <Inbox className="h-8 w-8 text-primary/30" />
              }
            </div>
            <div>
              <p className="text-sm font-semibold text-navy">
                {category
                  ? `No ${humanizeCategory(category)} notifications`
                  : tab === 'unread' ? "You're all caught up" : 'No notifications yet'}
              </p>
              <p className="mx-auto mt-1 max-w-[15rem] text-[12px] leading-relaxed text-muted">
                {category
                  ? 'Try a different category or clear the filter.'
                  : tab === 'unread'
                    ? 'Nice work! Check back later for new updates.'
                    : "When something needs your attention, it'll appear here."}
              </p>
            </div>
            {category && (
              <button
                type="button"
                onClick={() => setCategory(null)}
                className="rounded-full bg-background-light px-4 py-1.5 text-[12px] font-medium text-navy transition-colors hover:bg-border/60"
              >
                Clear filter
              </button>
            )}
          </div>
        )}

        {dateGroups.map((group) => (
          <div key={group.label} className="mb-1">
            <div className="sticky top-0 z-10 bg-surface/90 px-3 pb-1 pt-2 backdrop-blur-md">
              <p className="text-[10px] font-bold uppercase tracking-[0.1em] text-muted/50">{group.label}</p>
            </div>
            <div className="space-y-0.5 px-1">
              {group.items.map((item) => {
                const idx = globalIdx++;
                return (
                  <NotificationItem
                    key={item.id}
                    item={item}
                    index={idx}
                    reducedMotion={reducedMotion}
                    onOpen={(n) => void handleOpen(n)}
                  />
                );
              })}
            </div>
          </div>
        ))}
      </div>

      {/* ━━ Sticky footer ━━ */}
      <div className="shrink-0 border-t border-border/70 pt-2.5">
        {hasMore ? (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => void loadMore()}
            loading={isRefreshing}
            aria-label="Load older items"
            className="mb-1 w-full text-[12px]"
          >
            Load older notifications
          </Button>
        ) : filtered.length > 0 ? (
          <p className="pb-1 text-center text-[11px] text-muted/50">You&apos;re all caught up — end of notifications</p>
        ) : null}
        <div className="flex items-center justify-between px-1 pb-0.5">
          <span className="inline-flex items-center gap-1.5 text-[11px] text-muted/60">
            <span
              className={cn(
                'h-1.5 w-1.5 rounded-full',
                connectionStatus === 'connected' ? 'bg-emerald-500'
                  : connectionStatus === 'reconnecting' ? 'bg-amber-500'
                  : 'bg-muted/50',
              )}
            />
            {connectionStatus === 'connected' ? 'Live'
              : connectionStatus === 'reconnecting' ? 'Reconnecting'
              : connectionStatus === 'connecting' ? 'Connecting' : 'Offline'}
          </span>
          <Link
            href="/settings/notifications"
            onClick={() => onNavigate?.()}
            className="inline-flex items-center gap-1 text-[11px] font-medium text-muted/70 transition-colors hover:text-primary"
          >
            <Settings className="h-3 w-3" />
            Settings
          </Link>
        </div>
      </div>
    </div>
  );
}

/* ────────────────────────────────────────────────────────────── */
/*  Bell Button                                                   */
/* ────────────────────────────────────────────────────────────── */

type NotificationBellButtonProps = ComponentPropsWithoutRef<'button'> & { open?: boolean };

const NotificationBellButton = forwardRef<HTMLButtonElement, NotificationBellButtonProps>(
  function NotificationBellButton({ open, className, ...buttonProps }, ref) {
    // Only `unreadCount`/`connectionStatus` are needed here — subscribe to
    // state-only context so the bell button skips re-renders caused by action
    // reference changes.
    const { unreadCount, connectionStatus } = useNotificationState();
    const isDegraded = connectionStatus === 'reconnecting' || connectionStatus === 'disconnected';
    return (
      <button
        ref={ref}
        type="button"
        className={cn(
          'relative inline-flex h-11 w-11 items-center justify-center rounded-lg p-2.5 text-muted transition-colors hover:bg-primary/10 hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
          className,
        )}
        aria-label={buttonProps['aria-label'] ?? `Notifications${unreadCount > 0 ? ` (${unreadCount} unread)` : ''}${isDegraded ? ' — live updates paused' : ''}`}
        aria-expanded={open ?? buttonProps['aria-expanded']}
        {...buttonProps}
      >
        <Bell className="h-5 w-5" aria-hidden="true" />
        {unreadCount > 0 && (
          <span className="absolute -right-0.5 -top-0.5 inline-flex min-w-[18px] items-center justify-center rounded-full bg-primary px-1 py-px text-[10px] font-bold leading-none text-white shadow-sm shadow-primary/25 dark:bg-violet-700">
            {unreadCount > 99 ? '99+' : unreadCount}
          </span>
        )}
        {isDegraded && (
          <span
            className={cn(
              'absolute -bottom-0.5 -right-0.5 h-2.5 w-2.5 rounded-full border-2 border-surface',
              connectionStatus === 'reconnecting' ? 'bg-amber-500' : 'bg-muted/60',
            )}
            aria-hidden="true"
          />
        )}
      </button>
    );
  },
);

/* ────────────────────────────────────────────────────────────── */
/*  Notification Center (export)                                  */
/* ────────────────────────────────────────────────────────────── */

export function NotificationCenter() {
  const [desktopOpen, setDesktopOpen] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);

  return (
    <>
      {/* Desktop popover */}
      <div className="hidden md:block">
        <Popover.Root open={desktopOpen} onOpenChange={setDesktopOpen}>
          <Popover.Trigger asChild>
             <NotificationBellButton open={desktopOpen} />
          </Popover.Trigger>
          <Popover.Portal>
            <Popover.Content
              align="end"
              sideOffset={12}
              className="z-[100] w-[24rem] max-w-[calc(100vw-2rem)] origin-[var(--radix-popover-content-transform-origin)] rounded-2xl glass-panel p-4 shadow-2xl shadow-black/[0.08] data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-[0.97] data-[state=open]:slide-in-from-top-3 data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-[0.97] duration-200"
            >
              <NotificationCenterContent variant="popover" onNavigate={() => setDesktopOpen(false)} />
            </Popover.Content>
          </Popover.Portal>
        </Popover.Root>
      </div>

      {/* Mobile drawer — the content renders its own rich header (with a close
          button), so the Drawer chrome intentionally omits its `title` to avoid
          a duplicate "Notifications" bar. */}
      <div className="md:hidden">
        <NotificationBellButton onClick={() => setMobileOpen(true)} open={mobileOpen} />
        <Drawer
          open={mobileOpen}
          onClose={() => setMobileOpen(false)}
          className="max-w-full sm:max-w-md"
        >
          <NotificationCenterContent
            variant="drawer"
            onNavigate={() => setMobileOpen(false)}
            onClose={() => setMobileOpen(false)}
          />
        </Drawer>
      </div>
    </>
  );
}
