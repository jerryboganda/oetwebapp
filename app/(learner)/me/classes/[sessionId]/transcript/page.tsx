'use client';

import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import { useParams } from 'next/navigation';
import { ArrowLeft, FileText, Search } from 'lucide-react';

import { LearnerPageHero } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { Input } from '@/components/ui/form-controls';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchClassTranscript, type LiveClassTranscript } from '@/lib/api';

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function highlight(text: string, query: string): React.ReactNode {
  if (!query.trim()) return text;
  const re = new RegExp(`(${escapeRegExp(query.trim())})`, 'gi');
  const parts = text.split(re);
  return parts.map((part, i) =>
    i % 2 === 1 ? (
      <mark key={i} className="rounded bg-gold/30 px-0.5 text-navy">
        {part}
      </mark>
    ) : (
      <span key={i}>{part}</span>
    ),
  );
}

export default function ClassTranscriptPage() {
  const params = useParams();
  const sessionId = typeof params?.sessionId === 'string' ? params.sessionId : null;

  const [transcript, setTranscript] = useState<LiveClassTranscript | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');

  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    setLoading(true);
    fetchClassTranscript(sessionId)
      .then((data) => {
        if (!cancelled) setTranscript(data);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load transcript.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [sessionId]);

  const text = transcript?.transcriptText ?? '';
  const matches = useMemo(() => {
    if (!query.trim()) return 0;
    const re = new RegExp(escapeRegExp(query.trim()), 'gi');
    return text.match(re)?.length ?? 0;
  }, [query, text]);

  return (
    <>
      <LearnerPageHero
        title="Class transcript"
        description="Full transcript of the live class. Use the search box to jump to a specific phrase."
        icon={FileText}
        aside={(
          <Button asChild variant="outline" size="sm">
            <Link href={sessionId ? `/me/classes/recordings/${sessionId}` : '/me/classes/past'}>
              <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" /> Back
            </Link>
          </Button>
        )}
      />

      {!sessionId ? (
        <InlineAlert variant="warning">Invalid session id.</InlineAlert>
      ) : loading ? (
        <div className="space-y-3">
          <Skeleton className="h-12 rounded-2xl" />
          <Skeleton className="h-96 rounded-2xl" />
        </div>
      ) : error ? (
        <ErrorState message={error} />
      ) : !text ? (
        <EmptyState
          icon={<FileText className="h-8 w-8" />}
          title="No transcript available yet."
          description="Transcripts appear once the recording is processed."
        />
      ) : (
        <>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
            <div className="flex-1">
              <Input
                type="search"
                aria-label="Search the transcript"
                placeholder="Search the transcript..."
                value={query}
                onChange={(e) => setQuery(e.target.value)}
              />
            </div>
            <span className="flex items-center gap-2 text-sm tabular-nums text-muted" aria-live="polite">
              <Search className="h-4 w-4" aria-hidden="true" />
              {query.trim() ? `${matches} match${matches === 1 ? '' : 'es'}` : 'Type to search'}
            </span>
          </div>

          <MotionSection>
            <Card padding="lg">
              <article className="max-w-prose whitespace-pre-wrap text-sm leading-7 text-navy">
                {highlight(text, query)}
              </article>
            </Card>
          </MotionSection>

          {transcript?.processedAt ? (
            <p className="text-xs tabular-nums text-muted">
              Processed {new Date(transcript.processedAt).toLocaleString()}
            </p>
          ) : null}
        </>
      )}
    </>
  );
}
