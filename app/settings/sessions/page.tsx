'use client';

import { useEffect, useState, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import {
  ArrowLeft,
  MonitorSmartphone,
  Smartphone,
  Monitor,
  Globe,
  Loader2,
  ShieldAlert,
  Trash2,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import {
  fetchActiveSessions,
  revokeSession,
  revokeAllOtherSessions,
  type ActiveSession,
} from '@/lib/api';
import { cn } from '@/lib/utils';

function maskIpAddress(ip: string | null): string {
  if (!ip) return 'Unknown';
  // IPv4: mask last two octets
  const ipv4Match = ip.match(/^(\d{1,3}\.\d{1,3})\.\d{1,3}\.\d{1,3}$/);
  if (ipv4Match) return `${ipv4Match[1]}.*.*`;
  // IPv6: mask last half
  const parts = ip.split(':');
  if (parts.length > 4) {
    return parts.slice(0, 4).join(':') + ':*:*:*:*';
  }
  return ip;
}

function parseDeviceLabel(userAgent: string | null): { label: string; icon: typeof Monitor } {
  if (!userAgent) return { label: 'Unknown Device', icon: Globe };
  const ua = userAgent.toLowerCase();
  if (ua.includes('mobile') || ua.includes('android') || ua.includes('iphone')) {
    return { label: 'Mobile Device', icon: Smartphone };
  }
  if (ua.includes('electron') || ua.includes('oet-prep')) {
    return { label: 'Desktop App', icon: Monitor };
  }
  if (ua.includes('chrome')) return { label: 'Chrome Browser', icon: Monitor };
  if (ua.includes('firefox')) return { label: 'Firefox Browser', icon: Monitor };
  if (ua.includes('safari')) return { label: 'Safari Browser', icon: Monitor };
  if (ua.includes('edge')) return { label: 'Edge Browser', icon: Monitor };
  return { label: 'Browser', icon: Monitor };
}

function formatRelativeTime(dateStr: string | null): string {
  if (!dateStr) return 'Never';
  const date = new Date(dateStr);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffMin = Math.floor(diffMs / 60000);
  if (diffMin < 1) return 'Just now';
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHrs = Math.floor(diffMin / 60);
  if (diffHrs < 24) return `${diffHrs}h ago`;
  const diffDays = Math.floor(diffHrs / 24);
  if (diffDays < 30) return `${diffDays}d ago`;
  return date.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
}

export default function SessionsPage() {
  const router = useRouter();
  const [sessions, setSessions] = useState<ActiveSession[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [revokingId, setRevokingId] = useState<string | null>(null);
  const [revokingAll, setRevokingAll] = useState(false);
  const [confirmId, setConfirmId] = useState<string | null>(null);
  const [confirmAll, setConfirmAll] = useState(false);

  const loadSessions = useCallback(async () => {
    try {
      setError(null);
      const data = await fetchActiveSessions();
      setSessions(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load sessions.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    analytics.track('content_view', { page: 'settings_sessions' });
    loadSessions();
  }, [loadSessions]);

  const handleRevoke = async (sessionId: string) => {
    if (confirmId !== sessionId) {
      setConfirmId(sessionId);
      return;
    }
    setConfirmId(null);
    setRevokingId(sessionId);
    try {
      await revokeSession(sessionId);
      setSessions((prev) => prev.filter((s) => s.id !== sessionId));
      analytics.track('content_view', { page: 'settings_sessions', action: 'revoke_session' });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to revoke session.');
    } finally {
      setRevokingId(null);
    }
  };

  const handleRevokeAll = async () => {
    if (!confirmAll) {
      setConfirmAll(true);
      return;
    }
    setConfirmAll(false);
    setRevokingAll(true);
    try {
      await revokeAllOtherSessions();
      setSessions((prev) => prev.filter((s) => s.isCurrent));
      analytics.track('content_view', { page: 'settings_sessions', action: 'revoke_all' });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to revoke sessions.');
    } finally {
      setRevokingAll(false);
    }
  };

  const otherSessionCount = sessions.filter((s) => !s.isCurrent).length;

  return (
    <LearnerDashboardShell
      pageTitle="Active Sessions"
      subtitle="View and manage devices signed into your account"
      backHref="/settings"
    >
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Security"
          icon={MonitorSmartphone}
          accent="slate"
          title="Keep track of where you're signed in"
          description="Review all active sessions and remove any you don't recognise. Your current session is clearly marked and protected."
          highlights={[
            { icon: MonitorSmartphone, label: 'Active sessions', value: loading ? '...' : String(sessions.length) },
            { icon: ShieldAlert, label: 'Current session', value: 'Protected' },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {otherSessionCount > 0 && !loading ? (
          <div className="flex justify-end">
            <Button
              variant={confirmAll ? 'destructive' : 'outline'}
              size="sm"
              onClick={handleRevokeAll}
              disabled={revokingAll}
            >
              {revokingAll ? (
                <Loader2 className="w-4 h-4 animate-spin mr-2" />
              ) : (
                <Trash2 className="w-4 h-4 mr-2" />
              )}
              {confirmAll ? 'Confirm — Revoke All Other Sessions' : 'Revoke All Other Sessions'}
            </Button>
          </div>
        ) : null}

        <div className="bg-surface rounded-2xl border border-border shadow-sm overflow-hidden">
          {loading ? (
            <div className="divide-y divide-border">
              {[1, 2, 3].map((i) => (
                <div key={i} className="flex items-center gap-4 p-5">
                  <Skeleton className="w-10 h-10 rounded-xl" />
                  <div className="flex-1 space-y-2">
                    <Skeleton className="h-4 w-40" />
                    <Skeleton className="h-3 w-56" />
                  </div>
                  <Skeleton className="h-8 w-20 rounded-lg" />
                </div>
              ))}
            </div>
          ) : sessions.length === 0 ? (
            <div className="p-8 text-center text-muted">No active sessions found.</div>
          ) : (
            <div className="divide-y divide-border">
              {sessions.map((session) => {
                const { label: deviceLabel, icon: DeviceIcon } = parseDeviceLabel(session.deviceInfo);
                const masked = maskIpAddress(session.ipAddress);
                const lastActive = session.isCurrent ? 'This device' : formatRelativeTime(session.lastUsedAt);
                const isConfirming = confirmId === session.id;

                return (
                  <div
                    key={session.id}
                    className={`flex items-center justify-between p-4 sm:p-5 ${session.isCurrent ? 'bg-navy/[0.03]' : ''}`}
                  >
                    <div className="flex items-center gap-4 pr-4 min-w-0">
                      <div className={`w-10 h-10 rounded-xl flex items-center justify-center shrink-0 border ${session.isCurrent ? 'bg-navy/10 border-navy/20' : 'bg-background-light border-border'}`}>
                        <DeviceIcon className={`w-5 h-5 ${session.isCurrent ? 'text-navy' : 'text-muted'}`} />
                      </div>
                      <div className="min-w-0">
                        <div className="flex items-center gap-2 flex-wrap">
                          <h3 className="text-sm font-bold text-navy truncate">{deviceLabel}</h3>
                          {session.isCurrent ? (
                            <span className="inline-flex items-center rounded-full bg-navy/10 border border-navy/20 px-2 py-0.5 text-[11px] font-semibold text-navy">
                              Current Session
                            </span>
                          ) : null}
                        </div>
                        <p className="text-xs text-muted mt-0.5 truncate">
                          IP: {masked} · {lastActive}
                        </p>
                        <p className="text-[11px] text-muted/60 mt-0.5">
                          Created {formatRelativeTime(session.createdAt)}
                        </p>
                      </div>
                    </div>

                    <div className="shrink-0">
                      {session.isCurrent ? (
                        <span className="text-xs text-muted font-medium">Active</span>
                      ) : (
                        <Button
                          variant={isConfirming ? 'destructive' : 'outline'}
                          size="sm"
                          onClick={() => handleRevoke(session.id)}
                          disabled={revokingId === session.id}
                        >
                          {revokingId === session.id ? (
                            <Loader2 className="w-3.5 h-3.5 animate-spin" />
                          ) : isConfirming ? (
                            'Confirm'
                          ) : (
                            'Revoke'
                          )}
                        </Button>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>

        <button
          onClick={() => router.push('/settings')}
          className="inline-flex items-center gap-1.5 text-sm text-muted hover:text-navy transition-colors"
        >
          <ArrowLeft className="w-4 h-4" />
          Back to Settings
        </button>
      </div>
    </LearnerDashboardShell>
  );
}
