'use client';

import { useEffect, useState, useCallback } from 'react';
import Link from 'next/link';
import {
  ArrowLeft,
  MonitorSmartphone,
  Smartphone,
  Monitor,
  Globe,
  Loader2,
  ShieldCheck,
  Trash2,
} from 'lucide-react';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import {
  fetchActiveSessions,
  fetchTrustedDevice,
  revokeSession,
  revokeAllOtherSessions,
  type ActiveSession,
  type TrustedDeviceSelf,
} from '@/lib/api';

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
  if (ua.includes('oet-prep')) {
    return { label: 'Desktop App', icon: Monitor };
  }
  if (ua.includes('chrome')) return { label: 'Chrome Browser', icon: Monitor };
  if (ua.includes('firefox')) return { label: 'Firefox Browser', icon: Monitor };
  if (ua.includes('safari')) return { label: 'Safari Browser', icon: Monitor };
  if (ua.includes('edge')) return { label: 'Edge Browser', icon: Monitor };
  return { label: 'Browser', icon: Monitor };
}

function platformLabel(platform: string | null | undefined): string | null {
  if (!platform) return null;
  switch (platform) {
    case 'web': return 'Web';
    case 'desktop': return 'Desktop App';
    case 'tauri': return 'Desktop App';
    case 'capacitor': return 'Mobile App';
    case 'capacitor-android': return 'Android App';
    case 'capacitor-ios': return 'iOS App';
    default: return platform;
  }
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
  const [sessions, setSessions] = useState<ActiveSession[]>([]);
  const [trustedDevice, setTrustedDevice] = useState<TrustedDeviceSelf | null>(null);
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
      // Trusted device is informational — never block the sessions list on it.
      try {
        setTrustedDevice(await fetchTrustedDevice());
      } catch {
        setTrustedDevice(null);
      }
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


  // The list itself failed to load: an empty list below would read as "no sessions".
  const loadFailed = Boolean(error) && !loading && sessions.length === 0;

  return (
    <>
      <LearnerPageHero
        eyebrow="Security"
        icon={MonitorSmartphone}
        accent="slate"
        title="Keep track of where you're signed in"
        description="Review all active sessions and remove any you don't recognise. Your current session is clearly marked and protected."
        highlights={[
          { icon: MonitorSmartphone, label: 'Active sessions', value: loading ? '...' : String(sessions.length) },
        ]}
        aside={(
          <Button asChild variant="outline" size="sm">
            <Link href="/settings">
              <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
              Back to Settings
            </Link>
          </Button>
        )}
      />

      {error && !loadFailed ? (
        <InlineAlert
          variant="error"
          action={(
            <Button type="button" variant="outline" size="sm" onClick={() => void loadSessions()}>
              Retry
            </Button>
          )}
        >
          {error}
        </InlineAlert>
      ) : null}

      {trustedDevice ? (
        <MotionSection>
          <Card className="flex items-center gap-4">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-success/20 bg-success/10">
              <ShieldCheck className="h-5 w-5 text-success-strong" aria-hidden="true" />
            </div>
            <div className="min-w-0">
              <div className="flex flex-wrap items-center gap-2">
                <h2 className="min-w-0 break-words text-sm font-bold text-navy">
                  Trusted device: {trustedDevice.deviceName || platformLabel(trustedDevice.platform) || 'Unknown device'}
                </h2>
                {trustedDevice.isCurrentDevice ? (
                  <Badge variant="success">This device</Badge>
                ) : null}
              </div>
              <p className="mt-0.5 text-xs text-muted">
                {platformLabel(trustedDevice.platform) ? `${platformLabel(trustedDevice.platform)} · ` : ''}
                Trusted {formatRelativeTime(trustedDevice.trustedAt)} · Last seen {formatRelativeTime(trustedDevice.lastSeenAt)}
              </p>
              <p className="mt-1 text-2xs tabular-nums text-muted">
                Approved client identities: {trustedDevice.activeDeviceCount ?? 1}/{trustedDevice.maxDevices ?? 1}
              </p>
              <p className="mt-0.5 text-2xs text-muted">
                Browser profiles and official app installations count separately, even on the same physical hardware. A new approved identity signs out any identity it replaces; an admin override can retain more identities but only one live session remains active.
              </p>
            </div>
          </Card>
        </MotionSection>
      ) : null}

      {loadFailed ? (
        <ErrorState message={error ?? undefined} onRetry={() => void loadSessions()} retryLabel="Retry" />
      ) : (
        <MotionSection delayIndex={1} className="space-y-4">
          <LearnerSurfaceSectionHeader
            title="Active sessions"
            action={otherSessionCount > 0 && !loading ? (
              <Button
                variant={confirmAll ? 'destructive' : 'outline'}
                size="sm"
                onClick={handleRevokeAll}
                disabled={revokingAll}
                className="self-start sm:self-auto"
              >
                {revokingAll ? (
                  <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
                ) : (
                  <Trash2 className="h-4 w-4" aria-hidden="true" />
                )}
                {confirmAll ? 'Confirm: Revoke All Other Sessions' : 'Revoke All Other Sessions'}
              </Button>
            ) : undefined}
          />

          {!loading && sessions.length === 0 ? (
            <EmptyState
              icon={<MonitorSmartphone className="h-7 w-7" />}
              title="No active sessions found."
            />
          ) : (
            <Card padding="none" className="divide-y divide-border overflow-hidden">
              {loading ? (
                [1, 2, 3].map((i) => (
                  <div key={i} className="flex items-center gap-4 p-5">
                    <Skeleton className="h-10 w-10 rounded-xl" />
                    <div className="flex-1 space-y-2">
                      <Skeleton className="h-4 w-40" />
                      <Skeleton className="h-3 w-56" />
                    </div>
                    <Skeleton className="h-8 w-20 rounded-lg" />
                  </div>
                ))
              ) : (
                sessions.map((session) => {
                  const { label: deviceLabel, icon: DeviceIcon } = parseDeviceLabel(session.deviceInfo);
                  const masked = maskIpAddress(session.ipAddress);
                  const lastActive = session.isCurrent ? 'This device' : formatRelativeTime(session.lastUsedAt);
                  const isConfirming = confirmId === session.id;

                  return (
                    <div
                      key={session.id}
                      className={`flex items-center justify-between p-4 sm:p-5 ${session.isCurrent ? 'bg-background-light' : ''}`}
                    >
                      <div className="flex min-w-0 items-center gap-4 pe-4">
                        <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border ${session.isCurrent ? 'border-navy/20 bg-navy/10' : 'border-border bg-background-light'}`}>
                          <DeviceIcon className={`h-5 w-5 ${session.isCurrent ? 'text-navy' : 'text-muted'}`} aria-hidden="true" />
                        </div>
                        <div className="min-w-0">
                          <div className="flex flex-wrap items-center gap-2">
                            <h3 className="truncate text-sm font-bold text-navy">{deviceLabel}</h3>
                            {session.isCurrent ? (
                              <Badge variant="outline">Current Session</Badge>
                            ) : null}
                          </div>
                          <p className="mt-0.5 truncate text-xs text-muted">
                            IP: {masked}
                            {session.countryCode ? ` (${session.countryCode})` : ''}
                            {platformLabel(session.platform) ? ` · ${platformLabel(session.platform)}` : ''}
                            {` · ${lastActive}`}
                          </p>
                          <p className="mt-0.5 text-2xs text-muted">
                            Created {formatRelativeTime(session.createdAt)}
                          </p>
                        </div>
                      </div>

                      <div className="shrink-0">
                        {session.isCurrent ? (
                          <span className="text-xs font-medium text-muted">Active</span>
                        ) : (
                          <Button
                            variant={isConfirming ? 'destructive' : 'outline'}
                            size="sm"
                            onClick={() => handleRevoke(session.id)}
                            disabled={revokingId === session.id}
                          >
                            {revokingId === session.id ? (
                              <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
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
                })
              )}
            </Card>
          )}
        </MotionSection>
      )}
    </>
  );
}
