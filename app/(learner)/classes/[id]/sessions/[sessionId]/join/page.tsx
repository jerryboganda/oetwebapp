'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ExternalLink, ShieldCheck, Video } from 'lucide-react';

import { LearnerPageHero } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
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
  const [retryNonce, setRetryNonce] = useState(0);

  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    setLoading(true);
    setError(null);
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
  }, [sessionId, retryNonce]);

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
      <>
        <div className="flex flex-col gap-4">
          <div className="flex items-center justify-between">
            <h2 className="text-lg font-semibold text-navy">Live class: Zoom meeting {token.meetingNumber}</h2>
            <Button type="button" variant="outline" onClick={handleLeave}>
              Leave meeting
            </Button>
          </div>
          <ZoomMeetingEmbed joinToken={token} onLeave={handleLeave} />
        </div>
      </>
    );
  }

  // Pre-join lobby only. Once the meeting opens (above) this route is a live room: no motion here.
  return (
    <>
      <LearnerPageHero title="Join live class" description="Use the secure Zoom link for this class. Embedded Meeting SDK support is enabled when SDK credentials are configured." icon={Video} />

      {loading ? <Skeleton className="h-56 rounded-2xl" /> : null}

      {error ? <InlineAlert variant="warning">{error}</InlineAlert> : null}

      {token ? (
        (() => {
          const canEmbed = Boolean(token.sdkKey && token.signature);
          const joinUrl = safeZoomUrl(token.joinUrl);
          return (
            <Card padding="lg">
              <div className="flex flex-col gap-5 lg:flex-row lg:items-center lg:justify-between">
                <div className="min-w-0 space-y-2">
                  <Badge variant="success" size="md" className="gap-1.5">
                    <ShieldCheck className="h-4 w-4" aria-hidden="true" /> Server-signed join request
                  </Badge>
                  <h2 className="break-words text-2xl font-semibold text-navy">Zoom meeting {token.meetingNumber}</h2>
                  <p className="max-w-2xl text-sm leading-6 text-muted">
                    {token.sdkKey
                      && token.signature
                      ? 'Click "Join Class" to open the embedded Zoom meeting right here in the browser.'
                      : 'If the embedded room is not available in this browser, open Zoom directly. Mobile and desktop shells should use this same fallback link.'}
                  </p>
                </div>
                <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
                  <Button type="button" variant="primary" onClick={handleJoin}>
                    {canEmbed ? (
                      <>
                        <Video className="h-4 w-4" aria-hidden="true" /> Join class
                      </>
                    ) : (
                      <>
                        <ExternalLink className="h-4 w-4" aria-hidden="true" /> Open Zoom
                      </>
                    )}
                  </Button>
                  {!canEmbed && joinUrl ? (
                    <Button asChild variant="outline">
                      <a href={joinUrl} target="_blank" rel="noreferrer">
                        <ExternalLink className="h-4 w-4" aria-hidden="true" /> Open Zoom directly
                      </a>
                    </Button>
                  ) : null}
                  {classId ? (
                    <Button asChild variant="outline">
                      <Link href={`/classes/${classId}`}>Class details</Link>
                    </Button>
                  ) : null}
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
            </Card>
          );
        })()
      ) : null}

      <div>
        <Button type="button" variant="outline" onClick={() => setRetryNonce((current) => current + 1)}>
          Retry join preparation
        </Button>
      </div>
    </>
  );
}
