'use client';

/**
 * Speaking module rebuild (2026-06-11 spec) — RETIRED.
 *
 * This was the legacy two-role-play mock orchestrator with the 60-second
 * "bridge" handoff between role-plays. The bridge UX (and its backend
 * endpoints, now 410 Gone) has been removed: Speaking exams run as two
 * auto-advancing cards (Intro → Card A → Card B) with no bridge.
 *
 * The page now redirects to the new Speaking exam flow so no one lands on the
 * dead bridge stage.
 */
import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { Loader2 } from 'lucide-react';

export default function RetiredSpeakingMockOrchestratorPage() {
  const router = useRouter();

  useEffect(() => {
    const t = window.setTimeout(() => router.replace('/speaking/exam'), 1200);
    return () => window.clearTimeout(t);
  }, [router]);

  return (
    <div className="mx-auto flex min-h-[60vh] max-w-lg flex-col items-center justify-center px-4 text-center">
      <Loader2 className="h-6 w-6 animate-spin text-muted" />
      <h1 className="mt-4 text-lg font-semibold text-foreground">Speaking exams have moved</h1>
      <p className="mt-2 text-sm text-muted">
        The two-card Speaking exam now runs as Card A → Card B with no handoff step. Taking you to the
        new exam…
      </p>
      <Link href="/speaking/exam" className="mt-4 text-sm font-medium text-primary hover:underline">
        Go to Speaking exam now
      </Link>
    </div>
  );
}
