'use client';

import { useCallback, useEffect, useState } from 'react';
import { MessageSquare } from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchSpeakingTranscriptComments, type SpeakingTranscriptComment } from '@/lib/api';

// Wave 4 of docs/SPEAKING-MODULE-PLAN.md.
//
// Renders inline expert comments grouped by transcript line index. Drop
// this component beneath the matching transcript line on the
// learner-facing transcript view and on the expert review surface.
//
// Consumers pass the line index of the current transcript line; the
// component fetches all comments once and filters client-side for that
// line. For attempts with very few comments this is the simplest sound
// approach; if comment volume grows, swap for a pre-grouped fetch.
export function SpeakingTranscriptComments({
  attemptId,
  lineIndex,
}: {
  attemptId: string;
  lineIndex: number;
}) {
  const [comments, setComments] = useState<SpeakingTranscriptComment[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setComments(await fetchSpeakingTranscriptComments(attemptId));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load comments.');
    } finally {
      setLoading(false);
    }
  }, [attemptId]);

  useEffect(() => {
    void load();
  }, [load]);

  const lineComments = comments.filter((c) => c.transcriptLineIndex === lineIndex);
  if (loading) return <Skeleton className="h-4 w-32" />;
  if (error) return <p className="text-xs text-danger">{error}</p>;
  if (lineComments.length === 0) return null;

  return (
    <ul className="mt-1 flex flex-col gap-1 border-l-2 border-info pl-3">
      {lineComments.map((c) => (
        <li key={c.commentId} className="flex flex-col gap-0.5">
          <span className="flex items-center gap-2 text-xs">
            <MessageSquare className="h-3 w-3 text-info" />
            <Badge variant="info">{c.criterionCode}</Badge>
            <span className="text-muted">{new Date(c.createdAt).toLocaleString()}</span>
          </span>
          <span className="text-sm">{c.body}</span>
        </li>
      ))}
    </ul>
  );
}
