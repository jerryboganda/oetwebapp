'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ExternalLink, ShieldCheck, Video } from 'lucide-react';

import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Button, buttonClassName } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { ZoomMeetingEmbed } from '@/components/class/ZoomMeetingEmbed';
import { fetchLiveClassJoinToken, type LiveClassJoinToken } from '@/lib/api';
import { safeZoomUrl } from '@/lib/zoom-url';

export default function LiveClassJoinPage() {
  const params = useParams();
  const router = useRouter();
  const classId = typeof params?.id === 'string' ? params.id : null;
  const sessionId = typeof params?.sessionId === 'string' ? params.sessionId : null;
  const [token, setToken] = useState<LiveClassJoinToken | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [meeting, setMeeting] = useState(false);

  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    fetchLiveClassJoinToken(sessionId)
      .then((data) => {
        if (!cancelled) setToken(data);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not prepare Zoom join details.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [sessionId]);

  const handleJoin = () => {
    if (!token) return;
    if (token.sdkKey && token.signature) {
      setMeeting(true);
    } else {
      const joinUrl = safeZoomUrl(token.joinUrl);
      if (joinUrl) {
        window.open(joinUrl, '_blank', 'noopener,noreferrer');
      }
    }
  };

  const handleLeave = () => {
    setMeeting(false);
    if (classId) {
      router.push(`/classes/${classId}`);
    }
  };

  // Full-screen embedded meeting view
  if (meeting && token && token.sdkKey && token.signature) {
    return (
      <LearnerDashboardShell>
        <div className="flex flex-col gap-4">
          <div className="flex items-center justify-between">
            <h2 className="text-lg font-semibold text-navy">Live class — Zoom meeting {token.meetingNumber}</h2>
            <Button type="button" variant="outline" onClick={handleLeave}>
              Leave meeting
            </Button>
          </div>
          <ZoomMeetingEmbed joinToken={token} onLeave={handleLeave} />
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero title="Join live class" description="Use the secure Zoom link for this class. Embedded Meeting SDK support is enabled when SDK credentials are configured." icon={Video} />

        {loading ? <Skeleton className="h-56 rounded-xl" /> : null}

        {error ? <InlineAlert variant="warning">{error}</InlineAlert> : null}

        {token ? (
          (() => {
            const canEmbed = Boolean(token.sdkKey && token.signature);
            const joinUrl = safeZoomUrl(token.joinUrl);
            return (
          <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
            <div className="flex flex-col gap-5 lg:flex-row lg:items-center lg:justify-between">
              <div className="space-y-2">
                <div className="inline-flex items-center gap-2 rounded-full border border-success/30 bg-success/10 px-3 py-1 text-xs font-semibold text-success">
                  <ShieldCheck className="h-4 w-4" /> Server-signed join request
                </div>
                <h2 className="text-2xl font-semibold text-navy">Zoom meeting {token.meetingNumber}</h2>
                <p className="max-w-2xl text-sm leading-6 text-muted">
                  {token.sdkKey
                    && token.signature
                    ? 'Click "Join Class" to open the embedded Zoom meeting right here in the browser.'
                    : 'If the embedded room is not available in this browser, open Zoom directly. Mobile and desktop shells should use this same fallback link.'}
                </p>
              </div>
              <div className="flex flex-col gap-2 sm:flex-row">
                <Button type="button" variant="primary" onClick={handleJoin}>
                  {canEmbed ? (
                    <>
                      <Video className="h-4 w-4" /> Join class
                    </>
                  ) : (
                    <>
                      <ExternalLink className="h-4 w-4" /> Open Zoom
                    </>
                  )}
                </Button>
                {!canEmbed && joinUrl ? (
                  <a href={joinUrl} target="_blank" rel="noreferrer" className={buttonClassName({ variant: 'outline' })}>
                    <ExternalLink className="h-4 w-4" /> Open Zoom directly
                  </a>
                ) : null}
                {classId ? <Link href={`/classes/${classId}`} className={buttonClassName({ variant: 'outline' })}>Class details</Link> : null}
              </div>
            </div>

            {canEmbed ? (
              <InlineAlert variant="info" className="mt-5">
                Meeting SDK credentials are present. The browser client can now mount the SDK room component without exposing the secret.
              </InlineAlert>
            ) : (
              <InlineAlert variant="warning" className="mt-5">
                Embedded Meeting SDK credentials are not configured yet, so the safe external Zoom fallback is active.
              </InlineAlert>
            )}
          </section>
            );
          })()
        ) : null}

        <Button type="button" variant="outline" onClick={() => window.location.reload()}>
          Retry join preparation
        </Button>
      </div>
    </LearnerDashboardShell>
  );
}
