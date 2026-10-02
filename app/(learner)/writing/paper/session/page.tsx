'use client';

import { useEffect, useMemo, useState, type FormEvent } from 'react';
import Link from 'next/link';
import { ArrowRight, FileText, PenSquare } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { LearnerPageHero } from '@/components/domain/learner-surface';

function toSessionPath(id: string) {
  return `/writing/paper/session/${encodeURIComponent(id)}`;
}

export default function WritingPaperSessionIndexPage() {
  const router = useRouter();
  const searchParams = useSearchParams();

  const seededId = useMemo(() => (searchParams?.get('id') ?? '').trim(), [searchParams]);
  const [sessionId, setSessionId] = useState(seededId);

  useEffect(() => {
    if (!seededId) return;
    router.replace(toSessionPath(seededId));
  }, [router, seededId]);

  useEffect(() => {
    setSessionId(seededId);
  }, [seededId]);

  const trimmedId = sessionId.trim();

  function handleOpenSession(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!trimmedId) return;
    router.push(toSessionPath(trimmedId));
  }

  return (
    <>
      <LearnerPageHero
        icon={PenSquare}
        title="Paper-mode writing session"
        description="Open an existing paper-mode session by ID. This page is used for direct deep links and QA navigation."
      />

      <Card padding="lg">
        <form className="space-y-3" onSubmit={handleOpenSession}>
          <div>
            <label htmlFor="session-id" className="mb-1 block text-sm font-semibold text-navy">
              Session ID
            </label>
            <input
              id="session-id"
              value={sessionId}
              onChange={(event) => setSessionId(event.target.value)}
              placeholder="Paste a paper-mode session id"
              className="min-h-11 w-full rounded-control border border-border bg-background px-3 py-2 text-sm text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
              autoComplete="off"
            />
          </div>

          <Button type="submit" variant="primary" disabled={!trimmedId}>
            Open session
            <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
          </Button>
        </form>
      </Card>

      <Card padding="lg">
        <h2 className="flex items-center gap-2 text-base font-bold text-navy">
          <FileText className="h-4 w-4 text-primary" aria-hidden="true" />
          Need a new attempt?
        </h2>
        <div className="mt-3 flex flex-wrap gap-2">
          <Button asChild variant="secondary" size="sm">
            <Link href="/mocks">Go to mock exams</Link>
          </Button>
          <Button asChild variant="outline" size="sm">
            <Link href="/writing">Go to writing dashboard</Link>
          </Button>
        </div>
      </Card>
    </>
  );
}