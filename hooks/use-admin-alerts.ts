'use client';

import { useCallback, useSyncExternalStore } from 'react';
import { useAuth } from '@/contexts/auth-context';
import { fetchAdminAlerts } from '@/lib/api';

export interface AdminAlertItem {
  alertType: string;
  severity: 'critical' | 'warning' | 'info';
  title: string;
  description: string;
  actionRoute: string;
  detectedAt: string;
}

export interface AdminAlertsState {
  status: 'idle' | 'loading' | 'ready' | 'error';
  alerts: AdminAlertItem[];
  /** Sum of the counts embedded in alert descriptions (orders + proofs). */
  totalAlertCount: number;
}

export const ADMIN_FULFILMENT_ALERT_TYPES = ['pending_fulfilment', 'pending_payment_proofs'] as const;

const POLL_INTERVAL_MS = 30_000;

/** Whole-number counts are embedded in the alert descriptions ("3 paid order(s)…"). */
export function parseCountFromDescription(description: string): number {
  const match = typeof description === 'string' ? description.match(/\d+/) : null;
  if (!match) return 0;
  const parsed = Number.parseInt(match[0], 10);
  return Number.isFinite(parsed) ? parsed : 0;
}

/**
 * Defensive parse of the GET /v1/admin/alerts envelope. The shape mirrors
 * app/admin/alerts/page.tsx — any contract drift means we surface zero alerts
 * rather than crashing the shell, exactly like FulfilmentAlertBanner's silent
 * catch in app/admin/page.tsx.
 */
export function normalizeAdminAlerts(payload: unknown): AdminAlertItem[] {
  if (!payload || typeof payload !== 'object') return [];
  const envelope = payload as { alerts?: unknown };
  if (!Array.isArray(envelope.alerts)) return [];
  return envelope.alerts.flatMap((entry) => {
    if (!isAdminAlertItem(entry)) return [];
    return [{
      alertType: entry.alertType,
      severity: entry.severity,
      title: typeof entry.title === 'string' ? entry.title : entry.alertType,
      description: typeof entry.description === 'string' ? entry.description : '',
      actionRoute: entry.actionRoute,
      detectedAt: typeof entry.detectedAt === 'string' ? entry.detectedAt : new Date(0).toISOString(),
    }];
  });
}

function isAdminAlertItem(value: unknown): value is AdminAlertItem {
  if (!value || typeof value !== 'object') return false;
  const candidate = value as Partial<AdminAlertItem>;
  if (typeof candidate.alertType !== 'string' || typeof candidate.actionRoute !== 'string') return false;
  return (
    candidate.severity === 'critical'
    || candidate.severity === 'warning'
    || candidate.severity === 'info'
  );
}

/* ──────────────────────────────────────────────────────────────────────
 * Ref-counted singleton store.
 *
 * The Billing Ops badge (app/admin/layout.tsx) and the bell integration
 * (components/layout/notification-center.tsx) both consume the same 30s
 * poll, so wherever this hook is mounted there is exactly ONE shared
 * interval and ONE underlying request per tick — regardless of how many
 * components subscribe. State identity is REPLACED on every update (never
 * mutated) so useSyncExternalStore change detection stays trivial.
 * ──────────────────────────────────────────────────────────────────── */

const IDLE_STATE: AdminAlertsState = { status: 'idle', alerts: [], totalAlertCount: 0 };

let state: AdminAlertsState = IDLE_STATE;
const listeners = new Set<() => void>();
let subscriberCount = 0;
let pollTimer: ReturnType<typeof setInterval> | null = null;
let inflight = false;

function emit() {
  for (const listener of listeners) listener();
}

async function refresh(): Promise<void> {
  if (inflight) return;
  inflight = true;
  if (state.status === 'idle') {
    state = { ...state, status: 'loading' };
    emit();
  }
  try {
    const payload = await fetchAdminAlerts();
    const relevant = normalizeAdminAlerts(payload).filter((alert) =>
      (ADMIN_FULFILMENT_ALERT_TYPES as readonly string[]).includes(alert.alertType),
    );
    const totalAlertCount = relevant.reduce(
      (sum, alert) => sum + parseCountFromDescription(alert.description),
      0,
    );
    state = { status: 'ready', alerts: relevant, totalAlertCount };
  } catch {
    // Silent degradation — the endpoint is admin-only (learners get 403) and
    // must NEVER break rendering. Matches FulfilmentAlertBanner's catch {}.
    state = { status: 'error', alerts: [], totalAlertCount: 0 };
  } finally {
    inflight = false;
    emit();
  }
}

function start() {
  subscriberCount += 1;
  if (pollTimer !== null) return;
  void refresh();
  pollTimer = setInterval(() => void refresh(), POLL_INTERVAL_MS);
}

function stop() {
  subscriberCount = Math.max(0, subscriberCount - 1);
  if (subscriberCount > 0 || pollTimer === null) return;
  clearInterval(pollTimer);
  pollTimer = null;
}

/**
 * Live admin fulfilment alerts (paid orders awaiting manual fulfilment +
 * payment proofs awaiting review), sourced from GET /v1/admin/alerts.
 *
 * Hard role guard: non-admins never subscribe, so the endpoint is never
 * fetched for them (it would 403 anyway). While unsubscribed everyone reads
 * the canonical IDLE_STATE sentinel, keeping referential equality stable.
 */
export function useAdminAlerts(): AdminAlertsState {
  const { user } = useAuth();
  const isAdmin = user?.role === 'admin';

  const subscribe = useCallback((onStoreChange: () => void) => {
    if (!isAdmin) return () => {};
    listeners.add(onStoreChange);
    start();
    return () => {
      listeners.delete(onStoreChange);
      stop();
    };
  }, [isAdmin]);

  return useSyncExternalStore(
    subscribe,
    isAdmin ? getSnapshot : getIdleSnapshot,
    getIdleSnapshot,
  );
}

function getSnapshot(): AdminAlertsState {
  return state;
}

function getIdleSnapshot(): AdminAlertsState {
  return IDLE_STATE;
}