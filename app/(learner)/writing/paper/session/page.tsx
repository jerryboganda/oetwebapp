'use client';

import { useEffect, useMemo, useState, type FormEvent } from 'react';
import Link from 'next/link';
import { ArrowRight, FileText, PenSquare } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';

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
    <LearnerDashboardShell>
      <div className="mx-auto flex w-full max-w-4xl flex-col gap-4 px-4 py-6 sm:px-6 lg:px-8">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-xl">
              <PenSquare className="h-5 w-5 text-primary" />
              Paper-mode writing session
            </CardTitle>
            <CardDescription>
              Open an existing paper-mode session by ID. This page is used for direct deep links and QA navigation.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <form className="space-y-3" onSubmit={handleOpenSession}>
              <div>
                <label htmlFor="session-id" className="mb-1 block text-sm font-medium text-foreground">
                  Session ID
                </label>
                <input
                  id="session-id"
                  value={sessionId}
                  onChange={(event) => setSessionId(event.target.value)}
                  placeholder="Paste a paper-mode session id"
                  className="w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
                  autoComplete="off"
                />
              </div>

              <Button type="submit" variant="primary" disabled={!trimmedId}>
                Open session
                <ArrowRight className="h-4 w-4" />
              </Button>
            </form>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <FileText className="h-4 w-4 text-primary" />
              Need a new attempt?
            </CardTitle>
          </CardHeader>
          <CardContent className="flex flex-wrap gap-2">
            <Button asChild variant="secondary" size="sm">
              <Link href="/mocks">Go to mock exams</Link>
            </Button>
            <Button asChild variant="outline" size="sm">
              <Link href="/writing">Go to writing dashboard</Link>
            </Button>
          </CardContent>
        </Card>
      </div>
    </LearnerDashboardShell>
  );
}