'use client';

/**
 * Expert: speaking double-marking + senior moderation queue (§15.4 / §15.5).
 *
 * Lists moderation cases that need attention:
 *   - pending_second     → a second independent marker is required.
 *   - pending_moderation → the two marks diverged beyond threshold and a
 *                          senior moderator must reconcile a final score.
 *
 * Separation of duties (second marker ≠ first marker; moderator ≠ either
 * human marker) is enforced server-side, so this surface simply routes the
 * acting expert to the case detail where the relevant action is offered.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { RefreshCw, Scale, Users } from 'lucide-react';

import { ExpertDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Skeleton } from '@/components/ui/skeleton';
import { PROFESSION_OPTIONS } from '@/lib/api/speaking-role-play-cards';
import {
  moderationListQueue,
  moderationStatusLabel,
  SpeakingAssessmentApiError,
  type SpeakingModerationQueueItem,
} from '@/lib/api/speaking-assessments';

function formatRelative(iso: string): string {
  try {
    const ms = Date.now() - new Date(iso).getTime();
    if (ms < 60_000) return 'just now';
    if (ms < 3_600_000) return `${Math.floor(ms / 60_000)}m ago`;
    if (ms < 86_400_000) return `${Math.floor(ms / 3_600_000)}h ago`;
    return new Date(iso).toLocaleDateString();
  } catch {
    return iso;
  }
}

export default function SpeakingModerationQueuePage() {
  const router = useRouter();
  const searchParams = useSearchParams();

  const initialProfession = searchParams?.get('profession') ?? '';
  const [profession, setProfession] = useState<string>(initialProfession);
  const [items, setItems] = useState<SpeakingModerationQueueItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const filterGroups: FilterGroup[] = useMemo(
    () => [
      {
        id: 'profession',
        label: 'Profession',
        options: PROFESSION_OPTIONS.map((p) => ({ id: p.value, label: p.label })),
      },
    ],
    [],
  );

  const load = useCallback(async () => {
    setLoading(true);
    setErrorMsg(null);
    try {
      const next = await moderationListQueue(profession || undefined);
      setItems(next);
    } catch (err) {
      const message =
        err instanceof SpeakingAssessmentApiError
          ? err.message
          : 'Could not load the moderation queue. Please try again.';
      setErrorMsg(message);
    } finally {
      setLoading(false);
    }
  }, [profession]);

  useEffect(() => {
    void load();
  }, [load]);

  const columns: Column<SpeakingModerationQueueItem>[] = useMemo(
    () => [
      {
        key: 'session',
        header: 'Session',
        render: (row) => (
          <div className="flex flex-col">
            <span className="font-mono text-xs text-muted-foreground">{row.sessionId}</span>
            <span className="text-sm">{row.professionId || '—'}</span>
          </div>
        ),
      },
      {
        key: 'status',
        header: 'Status',
        render: (row) => (
          <div className="flex items-center gap-2">
            {row.needsSecondMark ? (
              <Users className="h-4 w-4 text-amber-500" aria-hidden />
            ) : (
              <Scale className="h-4 w-4 text-rose-500" aria-hidden />
            )}
            <Badge variant={row.needsModeration ? 'danger' : 'warning'}>
              {moderationStatusLabel(row.status)}
            </Badge>
          </div>
        ),
      },
      {
        key: 'variance',
        header: 'Variance',
        render: (row) =>
          row.variancePoints == null ? (
            <span className="text-muted-foreground">—</span>
          ) : (
            <span className="font-medium">{row.variancePoints} pts</span>
          ),
      },
      {
        key: 'reason',
        header: 'Reason',
        render: (row) => <span className="capitalize">{row.reason.replace(/_/g, ' ')}</span>,
      },
      {
        key: 'created',
        header: 'Opened',
        render: (row) => <span className="text-muted-foreground">{formatRelative(row.createdAt)}</span>,
      },
      {
        key: 'actions',
        header: '',
        render: (row) => (
          <Button
            variant="primary"
            size="sm"
            onClick={() => router.push(`/expert/speaking/moderation/${encodeURIComponent(row.sessionId)}`)}
          >
            {row.needsSecondMark ? 'Second mark' : 'Moderate'}
          </Button>
        ),
      },
    ],
    [router],
  );

  return (
    <ExpertDashboardShell>
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-6 p-4 md:p-6">
        <header className="flex flex-col gap-2">
          <div className="flex items-center gap-2">
            <Scale className="h-6 w-6 text-primary" aria-hidden />
            <h1 className="text-2xl font-semibold tracking-tight">Speaking moderation</h1>
          </div>
          <p className="text-sm text-muted-foreground">
            Double-marking and senior moderation for speaking sessions. A second independent marker is
            required for flagged sessions; when two marks diverge beyond the agreement threshold, a
            senior moderator reconciles the final score.
          </p>
        </header>

        <Card className="p-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <FilterBar
              groups={filterGroups}
              selected={{ profession: profession ? [profession] : [] }}
              onChange={(groupId, optionId) => {
                if (groupId !== 'profession') return;
                setProfession((prev) => (prev === optionId ? '' : optionId));
              }}
              onClear={() => setProfession('')}
            />
            <Button variant="secondary" size="sm" onClick={() => void load()} disabled={loading}>
              <RefreshCw className={`mr-2 h-4 w-4 ${loading ? 'animate-spin' : ''}`} aria-hidden />
              Refresh
            </Button>
          </div>
        </Card>

        {errorMsg && <InlineAlert variant="error">{errorMsg}</InlineAlert>}

        {loading ? (
          <div className="flex flex-col gap-2">
            <Skeleton className="h-12 w-full" />
            <Skeleton className="h-12 w-full" />
            <Skeleton className="h-12 w-full" />
          </div>
        ) : items.length === 0 ? (
          <EmptyState
            icon={<Scale className="h-8 w-8" aria-hidden />}
            title="No sessions awaiting moderation"
            description="Flagged double-marking and divergence cases will appear here."
          />
        ) : (
          <Card className="overflow-hidden">
            <DataTable data={items} columns={columns} keyExtractor={(row) => row.caseId} />
          </Card>
        )}
      </div>
    </ExpertDashboardShell>
  );
}
