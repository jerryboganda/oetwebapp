'use client';

import * as Popover from '@radix-ui/react-popover';
import { forwardRef, useMemo, useState, type ComponentPropsWithoutRef } from 'react';
import { useRouter } from 'next/navigation';
import { Bell, CheckCheck, Clock3, Filter, RefreshCw } from 'lucide-react';
import { motion, useReducedMotion } from 'motion/react';
import { getMotionDelay, getSurfaceTransition, getSurfaceVariants, prefersReducedMotion } from '@/lib/motion';
import { useNotificationCenter } from '@/contexts/notification-center-context';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Drawer } from '@/components/ui/modal';
import { cn } from '@/lib/utils';
import type { NotificationChannel, NotificationFeedItem } from '@/lib/types/notifications';
import { NotificationPreferencesPanel } from './notification-preferences-panel';

function formatTimestamp(value: string): string {
  return new Date(value).toLocaleString();
}

function connectionBadgeVariant(status: string): 'success' | 'warning' | 'muted' {
  switch (status) {
    case 'connected':
      return 'success';
    case 'reconnecting':
      return 'warning';
    default:
      return 'muted';
  }
}

function severityBadgeVariant(severity: NotificationFeedItem['severity']): 'info' | 'success' | 'warning' | 'danger' {
  switch (severity) {
    case 'success':
      return 'success';
    case 'warning':
      return 'warning';
    case 'critical':
      return 'danger';
    default:
      return 'info';
  }
}

function channelLabel(channel: NotificationChannel): string {
  switch (channel) {
    case 'in_app':
      return 'In-app';
    case 'email':
      return 'Email';
    case 'push':
      return 'Push';
    default:
      return channel;
  }
}

function NotificationCenterContent({ onNavigate }: { onNavigate?: () => void }) {
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
  const router = useRouter();
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const itemVariants = getSurfaceVariants('item', reducedMotion);
  const itemTransition = getSurfaceTransition('item', reducedMotion);
  const [categoryFilter, setCategoryFilter] = useState('all');
  const [channelFilter, setChannelFilter] = useState<'all' | NotificationChannel>('all');
  const [unreadOnly, setUnreadOnly] = useState(false);

  const categories = useMemo(
    () => Array.from(new Set(notifications.map((item) => item.category))).sort(),
    [notifications],
  );

  const filteredNotifications = useMemo(
    () =>
      notifications.filter((item) => {
        if (unreadOnly && item.isRead) {
          return false;
        }
        if (categoryFilter !== 'all' && item.category !== categoryFilter) {
          return false;
        }
        const matchesChannel = channelFilter === 'all' || item.channels.includes(channelFilter);
        return matchesChannel;
      }),
    [categoryFilter, channelFilter, notifications, unreadOnly],
  );

  const handleOpen = async (item: NotificationFeedItem) => {
    if (!item.isRead) {
      try {
        await markRead(item.id);
      } catch {
        // Keep the notification actionable even if the read update fails.
      }
    }

    onNavigate?.();

    if (!item.actionUrl) {
      return;
    }

    window.location.assign(item.actionUrl);
  };

  return (
    <div className="flex h-full flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant="info">Unread {unreadCount}</Badge>
            <Badge variant="muted">Total {totalCount}</Badge>
            <Badge variant={connectionBadgeVariant(connectionStatus)}>
              {connectionStatus === 'connected' ? 'Realtime Live' : connectionStatus}
            </Badge>
          </div>
          <p className="text-sm text-muted">
            Notifications sync live over SignalR and fall back to inbox polling if the hub disconnects.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => void refreshFeed({ reset: true })}
            loading={isRefreshing}
            className="gap-2"
          >
            <RefreshCw className="h-4 w-4" />
            Refresh
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => void markAllRead()}
            disabled={unreadCount === 0}
            className="gap-2"
          >
            <CheckCheck className="h-4 w-4" />
            Mark All Read
          </Button>
        </div>
      </div>

      <div className="grid gap-2 sm:grid-cols-3">
        <label className="space-y-1 text-sm font-medium text-navy">
          <span className="inline-flex items-center gap-2 text-xs uppercase tracking-[0.12em] text-muted">
            <Filter className="h-3.5 w-3.5" />
            Category
          </span>
          <select
            value={categoryFilter}
            onChange={(event) => setCategoryFilter(event.target.value)}
            className="w-full rounded-xl border border-gray-200 bg-white px-3 py-2 text-sm text-navy"
          >
            <option value="all">All categories</option>
            {categories.map((category) => (
              <option key={category} value={category}>
                {category}
              </option>
            ))}
          </select>
        </label>

        <label className="space-y-1 text-sm font-medium text-navy">
          <span className="inline-flex items-center gap-2 text-xs uppercase tracking-[0.12em] text-muted">
            <Bell className="h-3.5 w-3.5" />
            Channel
          </span>
          <select
            value={channelFilter}
            onChange={(event) => setChannelFilter(event.target.value as 'all' | NotificationChannel)}
            className="w-full rounded-xl border border-gray-200 bg-white px-3 py-2 text-sm text-navy"
          >
            <option value="all">All channels</option>
            <option value="in_app">In-app</option>
            <option value="email">Email</option>
            <option value="push">Push</option>
          </select>
        </label>

        <button
          type="button"
          onClick={() => setUnreadOnly((current) => !current)}
          className={cn(
            'rounded-xl border px-3 py-2 text-left text-sm font-medium transition-colors',
            unreadOnly ? 'border-primary/25 bg-primary/5 text-primary' : 'border-gray-200 bg-white text-navy',
          )}
        >
          <span className="inline-flex items-center gap-2 text-xs uppercase tracking-[0.12em] text-muted">
            <Clock3 className="h-3.5 w-3.5" />
            Visibility
          </span>
          <span className="mt-1 block">{unreadOnly ? 'Unread only' : 'All items'}</span>
        </button>
      </div>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
      {isLoading ? <p className="text-sm text-muted">Loading notifications...</p> : null}

      <div className="flex-1 space-y-3 overflow-y-auto pr-1">
        {!isLoading && filteredNotifications.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-gray-200 bg-gray-50 px-4 py-8 text-center text-sm text-muted">
            No notifications match the current filters yet.
          </div>
        ) : null}

        {filteredNotifications.map((item, idx) => (
          <motion.button
            key={item.id}
            type="button"
            variants={itemVariants}
            initial="hidden"
            animate="visible"
            transition={{ ...itemTransition, delay: getMotionDelay(idx, reducedMotion) }}
            onClick={() => void handleOpen(item)}
            className={cn(
              'w-full rounded-2xl border px-4 py-3 text-left transition-colors',
              item.isRead ? 'border-gray-200 bg-white hover:border-gray-300' : 'border-primary/20 bg-primary/5 hover:border-primary/35',
            )}
          >
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="space-y-2">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge variant={severityBadgeVariant(item.severity)}>{item.severity}</Badge>
                  <Badge variant={item.isRead ? 'muted' : 'info'}>{item.isRead ? 'Read' : 'Unread'}</Badge>
                  {item.channels.map((channel) => (
                    <Badge key={`${item.id}-${channel}`} variant="muted">
                      {channelLabel(channel)}
                    </Badge>
                  ))}
                </div>
                <div>
                  <p className="text-sm font-semibold text-navy">{item.title}</p>
                  <p className="mt-1 text-sm text-muted">{item.body}</p>
                </div>
              </div>
              <div className="space-y-1 text-right text-xs text-muted">
                <p>{item.category}</p>
                <p>{formatTimestamp(item.createdAt)}</p>
              </div>
            </div>
          </motion.button>
        ))}
      </div>

      {hasMore ? (
        <div className="flex justify-center border-t border-gray-100 pt-3">
          <Button
            type="button"
            variant="outline"
            onClick={() => void loadMore()}
            loading={isRefreshing}
            aria-label="Load older items"
          >
            Load Older Items
          </Button>
        </div>
      ) : null}

      <details className="rounded-2xl border border-gray-200 bg-white px-4 py-3">
        <summary className="cursor-pointer list-none text-sm font-semibold text-navy">
          Notification Preferences
        </summary>
        <div className="mt-4">
          <NotificationPreferencesPanel compact showCard={false} />
        </div>
      </details>
    </div>
  );
}

type NotificationBellButtonProps = ComponentPropsWithoutRef<'button'> & {
  open?: boolean;
};

const NotificationBellButton = forwardRef<HTMLButtonElement, NotificationBellButtonProps>(function NotificationBellButton(
  {
    open,
    className,
    ...buttonProps
  },
  ref,
) {
  const { unreadCount } = useNotificationCenter();

  return (
    <button
      ref={ref}
      type="button"
      className={cn(
        'relative touch-target rounded-2xl p-2 text-muted transition-colors hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
        className,
      )}
      aria-label={buttonProps['aria-label'] ?? `Notifications${unreadCount > 0 ? ` (${unreadCount} unread)` : ''}`}
      aria-expanded={open ?? buttonProps['aria-expanded']}
      {...buttonProps}
    >
      <Bell className="h-5 w-5" aria-hidden="true" />
      {unreadCount > 0 ? (
        <span className="absolute -right-1 -top-1 inline-flex min-w-5 items-center justify-center rounded-full bg-primary px-1.5 py-0.5 text-[10px] font-bold text-white">
          {unreadCount > 99 ? '99+' : unreadCount}
        </span>
      ) : null}
    </button>
  );
});

export function NotificationCenter() {
  const [desktopOpen, setDesktopOpen] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);

  return (
    <>
      <div className="hidden md:block">
        <Popover.Root open={desktopOpen} onOpenChange={setDesktopOpen}>
          <Popover.Trigger asChild>
            <NotificationBellButton open={desktopOpen} />
          </Popover.Trigger>
          <Popover.Portal>
            <Popover.Content
              align="end"
              sideOffset={12}
              className="z-50 w-[28rem] rounded-[24px] border border-gray-200 bg-surface p-4 shadow-2xl data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-[0.98] data-[state=open]:slide-in-from-top-2 data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-[0.98] duration-200"
            >
              <NotificationCenterContent onNavigate={() => setDesktopOpen(false)} />
            </Popover.Content>
          </Popover.Portal>
        </Popover.Root>
      </div>

      <div className="md:hidden">
        <NotificationBellButton onClick={() => setMobileOpen(true)} open={mobileOpen} />
        <Drawer
          open={mobileOpen}
          onClose={() => setMobileOpen(false)}
          title="Notification Center"
          className="max-w-full sm:max-w-md"
        >
          <NotificationCenterContent onNavigate={() => setMobileOpen(false)} />
        </Drawer>
      </div>
    </>
  );
}
