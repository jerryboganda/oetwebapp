'use client';

import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import { useParams } from 'next/navigation';
import { ArrowLeft, FileText, Search } from 'lucide-react';

import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
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
      <mark key={i} className="rounded bg-amber-200 px-0.5 dark:bg-amber-500/40">
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
    <LearnerDashboardShell>
      <div className="space-y-6">
        <Link
          href={sessionId ? `/me/classes/recordings/${sessionId}` : '/me/classes/past'}
          className="inline-flex items-center gap-1.5 text-sm text-muted hover:text-navy"
        >
          <ArrowLeft className="h-4 w-4" /> Back
        </Link>

        <LearnerPageHero
          title="Class transcript"
          description="Full transcript of the live class. Use the search box to jump to a specific phrase."
          icon={FileText}
        />

        {!sessionId ? (
          <InlineAlert variant="warning">Invalid session id.</InlineAlert>
        ) : loading ? (
          <div className="space-y-3">
            <Skeleton className="h-12 rounded-2xl" />
            <Skeleton className="h-96 rounded-2xl" />
          </div>
        ) : error ? (
          <InlineAlert variant="warning" className="flex items-center justify-between gap-3">
            <span>{error}</span>
            <Button type="button" variant="ghost" size="sm" onClick={() => setError(null)}>
              Dismiss
            </Button>
          </InlineAlert>
        ) : !text ? (
          <div className="rounded-2xl border border-dashed border-border bg-surface p-10 text-center">
            <FileText className="mx-auto mb-3 h-8 w-8 text-muted/50" />
            <p className="text-sm font-medium text-navy">No transcript available yet.</p>
            <p className="mt-1 text-sm text-muted">Transcripts appear once the recording is processed.</p>
          </div>
        ) : (
          <>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
              <div className="flex-1">
                <Input
                  type="search"
                  placeholder="Search the transcript..."
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                />
              </div>
              <span className="flex items-center gap-2 text-sm text-muted">
                <Search className="h-4 w-4" />
                {query.trim() ? `${matches} match${matches === 1 ? '' : 'es'}` : 'Type to search'}
              </span>
            </div>

            <article className="rounded-2xl border border-border bg-surface p-5 text-sm leading-7 text-navy shadow-sm whitespace-pre-wrap">
              {highlight(text, query)}
            </article>

            {transcript?.processedAt ? (
              <p className="text-xs text-muted">
                Processed {new Date(transcript.processedAt).toLocaleString()}
              </p>
            ) : null}
          </>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
