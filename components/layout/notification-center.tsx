'use client';

import * as Popover from '@radix-ui/react-popover';
import { forwardRef, useMemo, useState, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import {
  AlertTriangle,
  Bell,
  BellOff,
  BookOpen,
  Calendar,
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

const SEVERITY_STYLES: Record<NotificationSeverity, { bg: string; text: string; icon: ReactNode }> = {
  info:     { bg: 'bg-blue-50',    text: 'text-blue-600',    icon: <Info className="h-4 w-4" /> },
  success:  { bg: 'bg-emerald-50', text: 'text-emerald-600', icon: <CheckCircle2 className="h-4 w-4" /> },
  warning:  { bg: 'bg-amber-50',   text: 'text-amber-600',   icon: <AlertTriangle className="h-4 w-4" /> },
  critical: { bg: 'bg-red-50',     text: 'text-red-600',     icon: <Shield className="h-4 w-4" /> },
};

/* ── Category icons ── */

const CATEGORY_ICONS: Record<string, ReactNode> = {
  results:     <Target className="h-4 w-4" />,
  reviews:     <FileText className="h-4 w-4" />,
  study_plan:  <BookOpen className="h-4 w-4" />,
  reminders:   <Calendar className="h-4 w-4" />,
  readiness:   <Sparkles className="h-4 w-4" />,
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
        'group relative flex w-full items-start gap-3 rounded-xl px-3 py-3 text-left transition-all duration-200',
        item.isRead
          ? 'hover:bg-gray-50/80'
          : 'bg-primary/[0.03] hover:bg-primary/[0.05]',
      )}
    >
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
        <span className="mt-1.5 inline-flex items-center gap-1 text-[11px] font-medium text-muted/60">
          <GraduationCap className="h-3 w-3" />
          {humanizeCategory(item.category)}
        </span>
      </div>

      {/* ── Unread indicator ── */}
      {!item.isRead && (
        <span className="absolute right-3 top-3 h-2 w-2 rounded-full bg-primary shadow-sm shadow-primary/30" />
      )}

      {/* ── Arrow hint on hover ── */}
      {item.actionUrl && (
        <ChevronRight className="mt-1 h-4 w-4 shrink-0 text-muted/0 transition-colors group-hover:text-muted/40" />
      )}
    </motion.button>
  );
}

/* ────────────────────────────────────────────────────────────── */
/*  Panel Content                                                 */
/* ────────────────────────────────────────────────────────────── */

function NotificationCenterContent({ onNavigate }: { onNavigate?: () => void }) {
  const {
    notifications,
    unreadCount,
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

  const filtered = useMemo(
    () => (tab === 'unread' ? notifications.filter((n) => !n.isRead) : notifications),
    [tab, notifications],
  );
  const dateGroups = useMemo(() => groupByDate(filtered), [filtered]);

  const handleOpen = async (item: NotificationFeedItem) => {
    if (!item.isRead) {
      try { await markRead(item.id); } catch { /* keep actionable */ }
    }
    onNavigate?.();
    if (item.actionUrl) window.location.assign(item.actionUrl);
  };

  let globalIdx = 0;

  return (
    <div className="flex h-full max-h-[min(34rem,80vh)] flex-col">

      {/* ━━ Header ━━ */}
      <div className="flex items-center justify-between border-b border-gray-100/80 pb-3">
        <div>
          <h2 className="text-[15px] font-semibold tracking-tight text-navy">Notifications</h2>
          <p className="mt-0.5 text-[11px] text-muted/70">
            {unreadCount > 0 ? `${unreadCount} new` : 'All caught up'}
          </p>
        </div>
        <div className="flex items-center gap-0.5">
          {/* Refresh */}
          <button
            type="button"
            onClick={() => void refreshFeed({ reset: true })}
            className="inline-flex h-10 w-10 items-center justify-center rounded-lg text-muted/60 transition-colors hover:bg-gray-100 hover:text-navy"
            aria-label="Refresh"
          >
            <RefreshCw className={cn('h-3.5 w-3.5', isRefreshing && 'animate-spin')} />
          </button>
          {/* Settings */}
          <Link
            href="/settings/notifications"
            onClick={() => { onNavigate?.(); }}
            className="inline-flex h-10 w-10 items-center justify-center rounded-lg text-muted/60 transition-colors hover:bg-gray-100 hover:text-navy"
            aria-label="Notification settings"
          >
            <Settings className="h-3.5 w-3.5" />
          </Link>
        </div>
      </div>

      {/* ━━ Tabs + Mark all read ━━ */}
      <div className="flex items-center justify-between px-1 py-2">
        <div className="flex rounded-lg bg-gray-100/70 p-0.5">
          {(['all', 'unread'] as const).map((value) => (
            <button
              key={value}
              type="button"
              onClick={() => setTab(value)}
              className={cn(
                'rounded-md px-3 py-2 text-[12px] font-semibold transition-all duration-150',
                tab === value
                  ? 'bg-white text-navy shadow-sm'
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
          className="text-[11px] font-medium text-primary transition-colors hover:text-primary/80 disabled:cursor-default disabled:text-muted/40"
          aria-label="Mark all read"
        >
          Mark all read
        </button>
      </div>

      {/* ━━ Connection warning ━━ */}
      {(connectionStatus === 'reconnecting' || connectionStatus === 'disconnected') && (
        <div className="mx-1 mb-2 flex items-center gap-2 rounded-lg bg-amber-50/80 px-3 py-2 text-[12px] text-amber-700">
          <WifiOff className="h-3.5 w-3.5 shrink-0" />
          {connectionStatus === 'reconnecting' ? 'Reconnecting…' : 'Offline — updates may be delayed'}
        </div>
      )}

      {/* ━━ Error ━━ */}
      {error && (
        <div className="mx-1 mb-2 rounded-lg bg-red-50/80 px-3 py-2 text-[12px] text-red-700">{error}</div>
      )}

      {/* ━━ Loading ━━ */}
      {isLoading && (
        <div className="flex flex-1 items-center justify-center py-16">
          <div className="h-7 w-7 animate-spin rounded-full border-[2.5px] border-primary/20 border-t-primary" />
        </div>
      )}

      {/* ━━ Notification list ━━ */}
      <div className="flex-1 overflow-y-auto overscroll-contain">
        {!isLoading && filtered.length === 0 && (
          <div className="flex flex-col items-center justify-center gap-4 py-16 text-center">
            <div className={cn(
              'flex h-14 w-14 items-center justify-center rounded-2xl',
              tab === 'unread' ? 'bg-emerald-50' : 'bg-primary/5',
            )}>
              {tab === 'unread'
                ? <BellOff className="h-7 w-7 text-emerald-500/70" />
                : <Inbox className="h-7 w-7 text-primary/30" />
              }
            </div>
            <div>
              <p className="text-sm font-semibold text-navy">
                {tab === 'unread' ? "You're all caught up" : 'No notifications yet'}
              </p>
              <p className="mx-auto mt-1 max-w-[14rem] text-[12px] leading-relaxed text-muted">
                {tab === 'unread'
                  ? 'Nice work! Check back later for new updates.'
                  : "When something needs your attention, it'll appear here."}
              </p>
            </div>
          </div>
        )}

        {dateGroups.map((group) => (
          <div key={group.label} className="mb-1">
            <div className="sticky top-0 z-10 bg-surface/90 px-3 pb-1 pt-2 backdrop-blur-sm">
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

      {/* ━━ Load more ━━ */}
      {hasMore && (
        <div className="flex justify-center border-t border-gray-100/80 py-2.5">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => void loadMore()}
            loading={isRefreshing}
            aria-label="Load older items"
            className="text-[12px]"
          >
            Load older notifications
          </Button>
        </div>
      )}
    </div>
  );
}

/* ────────────────────────────────────────────────────────────── */
/*  Bell Button                                                   */
/* ────────────────────────────────────────────────────────────── */

type NotificationBellButtonProps = ComponentPropsWithoutRef<'button'> & { open?: boolean };

const NotificationBellButton = forwardRef<HTMLButtonElement, NotificationBellButtonProps>(
  function NotificationBellButton({ open, className, ...buttonProps }, ref) {
    // Only `unreadCount` is needed here — subscribe to state-only context
    // so the bell button skips re-renders caused by action reference changes.
    const { unreadCount } = useNotificationState();
    return (
      <button
        ref={ref}
        type="button"
        className={cn(
          'relative inline-flex h-11 w-11 items-center justify-center rounded-lg p-2.5 text-muted transition-colors hover:bg-primary/10 hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
          className,
        )}
        aria-label={buttonProps['aria-label'] ?? `Notifications${unreadCount > 0 ? ` (${unreadCount} unread)` : ''}`}
        aria-expanded={open ?? buttonProps['aria-expanded']}
        {...buttonProps}
      >
        <Bell className="h-5 w-5" aria-hidden="true" />
        {unreadCount > 0 && (
          <span className="absolute -right-0.5 -top-0.5 inline-flex min-w-[18px] items-center justify-center rounded-full bg-primary px-1 py-px text-[10px] font-bold leading-none text-white shadow-sm shadow-primary/25">
            {unreadCount > 99 ? '99+' : unreadCount}
          </span>
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
              className="z-50 w-[24rem] rounded-2xl border border-gray-200/80 bg-surface p-4 shadow-2xl shadow-black/[0.08] data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-[0.97] data-[state=open]:slide-in-from-top-3 data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-[0.97] duration-200"
            >
              <NotificationCenterContent onNavigate={() => setDesktopOpen(false)} />
            </Popover.Content>
          </Popover.Portal>
        </Popover.Root>
      </div>

      {/* Mobile drawer */}
      <div className="md:hidden">
        <NotificationBellButton onClick={() => setMobileOpen(true)} open={mobileOpen} />
        <Drawer
          open={mobileOpen}
          onClose={() => setMobileOpen(false)}
          title="Notifications"
          className="max-w-full sm:max-w-md"
        >
          <NotificationCenterContent onNavigate={() => setMobileOpen(false)} />
        </Drawer>
      </div>
    </>
  );
}
