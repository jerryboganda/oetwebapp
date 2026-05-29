'use client';

import { useEffect, useRef } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Loader2 } from 'lucide-react';
import { fetchMockSession, startMockSection } from '@/lib/api';

// WORK-STREAM 1 — the legacy `/listening/mocks/[sessionId]` route used to be a
// fake 45-minute timer + "Submit mock now" stub. The real strict Listening
// player now ships at `/listening/player/[paperId]`, and the Mocks V2 backend
// already emits a fully-formed `launchRoute` per section (BuildLaunchRoute →
// `/listening/player/{paperId}?mockAttemptId=...&mockSectionId=...&strictness=...
// &deliveryMode=...`). So this page is now a thin redirect that resolves the
// mock's Listening section and replaces itself with the real player route,
// exactly the way `/mocks/player/[id]` launches each section.
export default function ListeningMockSessionPage() {
  const params = useParams<{ sessionId: string }>();
  const sessionId = params?.sessionId ?? '';
  const router = useRouter();
  // A `[sessionId]` may legitimately be either a mock attempt id OR (legacy
  // links) a bare content-paper id. We only ever redirect once; the guard stops
  // a re-render from firing a second router.replace.
  const redirectedRef = useRef(false);

  useEffect(() => {
    if (!sessionId || redirectedRef.current) return;
    let cancelled = false;

    // Fallback: treat the route param as a content-paper id and hand off to the
    // real player in exam mode. Used when the id is not a resolvable mock
    // attempt (legacy deep links) or when section resolution fails.
    const redirectToPaperFallback = () => {
      if (cancelled || redirectedRef.current) return;
      redirectedRef.current = true;
      router.replace(`/listening/player/${encodeURIComponent(sessionId)}?mode=exam`);
    };

    (async () => {
      try {
        const session = await fetchMockSession(sessionId);
        if (cancelled || redirectedRef.current) return;
        const listeningSection = session.sectionStates.find(
          (section) => section.subtest === 'listening',
        );
        if (!listeningSection) {
          // A real mock attempt with no Listening section is not a Listening
          // mock — fall back to driving the paper id straight into the player.
          redirectToPaperFallback();
          return;
        }
        // Start the section so the backend stamps `mockSectionId` (and binds the
        // content attempt) onto the server-provided launchRoute, then replace.
        const started = await startMockSection(session.sessionId, listeningSection.id);
        if (cancelled || redirectedRef.current) return;
        redirectedRef.current = true;
        router.replace(started.launchRoute || listeningSection.launchRoute);
      } catch {
        // Not a resolvable mock attempt (e.g. a legacy content-paper id) — fall
        // back to the player in exam mode rather than dead-ending on an error.
        redirectToPaperFallback();
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [sessionId, router]);

  return (
    <main className="mx-auto flex min-h-[60vh] max-w-2xl flex-col items-center justify-center px-4 py-12 text-center">
      <Loader2 className="h-6 w-6 animate-spin text-primary motion-reduce:animate-none dark:text-violet-400" aria-hidden />
      <p className="mt-4 text-sm text-muted" role="status" aria-live="polite">
        Opening your strict Listening mock…
      </p>
    </main>
  );
}
