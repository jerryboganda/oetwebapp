'use client';

/**
 * Expert: speaking review queue.
 *
 * Lists completed speaking sessions awaiting tutor review. Tutors can filter
 * by profession + age preset, claim a session (which expires after a TTL on
 * the backend), and navigate to the assess page.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Headphones, Mic, RefreshCw, Sparkles, Unlock, UserCheck } from 'lucide-react';

import { ExpertDashboardShell } from '@/components/layout';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Skeleton } from '@/components/ui/skeleton';
import { PROFESSION_OPTIONS } from '@/lib/api/speaking-role-play-cards';
import {
  SpeakingAssessmentApiError,
  tutorClaimSession,
  tutorListQueue,
  tutorReleaseSession,
  type TutorQueueItem,
} from '@/lib/api/speaking-assessments';

const FILTER_KEYS = ['profession', 'age'] as const;

const AGE_PRESETS: { id: string; label: string }[] = [
  { id: 'child', label: 'Child (0–11)' },
  { id: 'adolescent', label: 'Adolescent (12–17)' },
  { id: 'adult', label: 'Adult (18–64)' },
  { id: 'older_adult', label: 'Older adult (65+)' },
];

function parseFilters(searchParams: URLSearchParams | null): Record<string, string[]> {
  const initial: Record<string, string[]> = {};
  FILTER_KEYS.forEach((key) => {
    const value = searchParams?.get(key);
    if (value) initial[key] = value.split(',').filter(Boolean);
  });
  return initial;
}

function formatDuration(seconds: number): string {
  const m = Math.floor(seconds / 60);
  const s = Math.max(0, seconds - m * 60);
  return `${m}m ${s.toString().padStart(2, '0')}s`;
}

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

export default function SpeakingQueuePage() {
  const router = useRouter();
  const searchParams = useSearchParams();

  const [items, setItems] = useState<TutorQueueItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [selectedFilters, setSelectedFilters] = useState<Record<string, string[]>>(() => parseFilters(searchParams));
  const [pendingId, setPendingId] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error' | 'info'; message: string } | null>(null);

  const filterGroups: FilterGroup[] = useMemo(
    () => [
      { id: 'profession', label: 'Profession', options: PROFESSION_OPTIONS.map((p) => ({ id: p.value, label: p.label })) },
      { id: 'age', label: 'Patient age', options: AGE_PRESETS },
    ],
    [],
  );

  const filters = useMemo(
    () => ({
      professionId: selectedFilters.profession?.[0],
      agePreset: selectedFilters.age?.[0],
    }),
    [selectedFilters],
  );

  const load = useCallback(async () => {
    setLoading(true);
    setErrorMsg(null);
    try {
      const response = await tutorListQueue(filters);
      setItems(response.items);
      setTotalCount(response.totalCount);
    } catch (err) {
      const msg = err instanceof SpeakingAssessmentApiError ? err.message : 'Failed to load the review queue.';
      setErrorMsg(msg);
    } finally {
      setLoading(false);
    }
  }, [filters]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleFilterChange = useCallback((groupId: string, optionId: string) => {
    setSelectedFilters((prev) => {
      const current = prev[groupId] ?? [];
      const next = current.includes(optionId) ? current.filter((id) => id !== optionId) : [...current, optionId];
      return { ...prev, [groupId]: next };
    });
  }, []);

  const handleFilterClear = useCallback(() => setSelectedFilters({}), []);

  const handleClaim = useCallback(async (item: TutorQueueItem) => {
    setPendingId(item.sessionId);
    try {
      await tutorClaimSession(item.sessionId);
      setToast({ variant: 'success', message: `Claimed session for ${item.learnerDisplayName}.` });
      router.push(`/expert/speaking/sessions/${encodeURIComponent(item.sessionId)}/assess`);
    } catch (err) {
      const msg = err instanceof SpeakingAssessmentApiError ? err.message : 'Failed to claim session.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setPendingId(null);
    }
  }, [router]);

  const handleRelease = useCallback(async (item: TutorQueueItem) => {
    setPendingId(item.sessionId);
    try {
      await tutorReleaseSession(item.sessionId);
      setToast({ variant: 'info', message: 'Session released.' });
      await load();
    } catch (err) {
      const msg = err instanceof SpeakingAssessmentApiError ? err.message : 'Failed to release session.';
      setToast({ variant: 'error', message: msg });
    } finally {
      setPendingId(null);
    }
  }, [load]);

  const columns: Column<TutorQueueItem>[] = useMemo(
    () => [
      {
        key: 'session',
        header: 'Session',
        render: (item) => (
          <div className="flex flex-col">
            <span className="font-bold text-navy">{item.cardTitle}</span>
            <span className="text-xs text-muted">{item.sessionId.slice(0, 8)}…</span>
          </div>
        ),
      },
      {
        key: 'learner',
        header: 'Learner',
        render: (item) => (
          <div className="flex items-center gap-2">
            <span className="inline-flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 text-xs font-bold text-primary">
              {item.learnerDisplayName.slice(0, 2).toUpperCase()}
            </span>
            <span className="text-sm font-semibold text-navy">{item.learnerDisplayName}</span>
          </div>
        ),
      },
      {
        key: 'profession',
        header: 'Profession',
        render: (item) => (
          <Badge variant="muted" className="capitalize">
            {item.professionId.replace(/_/g, ' ')}
          </Badge>
        ),
      },
      {
        key: 'duration',
        header: 'Duration',
        hideOnMobile: true,
        render: (item) => <span className="tabular-nums text-sm">{formatDuration(item.durationSeconds)}</span>,
      },
      {
        key: 'ai',
        header: 'AI estimate',
        hideOnMobile: true,
        render: (item) =>
          item.aiScaledScore !== undefined ? (
            <div className="flex items-center gap-2">
              <Sparkles className="h-3.5 w-3.5 text-indigo-500" aria-hidden />
              <span className="font-bold tabular-nums">{Math.round(item.aiScaledScore)}</span>
              {item.aiReadinessBand && (
                <Badge variant="info" className="capitalize">
                  {item.aiReadinessBand.replace(/_/g, ' ')}
                </Badge>
              )}
            </div>
          ) : (
            <span className="text-xs text-muted">Pending</span>
          ),
      },
      {
        key: 'ended',
        header: 'Ended',
        hideOnMobile: true,
        render: (item) => <span className="text-xs text-muted">{formatRelative(item.endedAt)}</span>,
      },
      {
        key: 'actions',
        header: '',
        render: (item) => {
          const isPending = pendingId === item.sessionId;
          if (item.claimedByMe) {
            return (
              <div className="flex flex-wrap items-center gap-2">
                <Button
                  size="sm"
                  variant="primary"
                  loading={isPending}
                  onClick={() => router.push(`/expert/speaking/sessions/${encodeURIComponent(item.sessionId)}/assess`)}
                >
                  Continue review
                </Button>
                <Button size="sm" variant="ghost" onClick={() => void handleRelease(item)} loading={isPending}>
                  <Unlock className="mr-1 h-3.5 w-3.5" aria-hidden />
                  Release
                </Button>
              </div>
            );
          }
          if (item.claimedBySomeoneElse) {
            return (
              <Badge variant="warning" className="inline-flex items-center gap-1">
                <UserCheck className="h-3 w-3" aria-hidden />
                Claimed
              </Badge>
            );
          }
          return (
            <Button size="sm" variant="primary" onClick={() => void handleClaim(item)} loading={isPending}>
              Claim &amp; review
            </Button>
          );
        },
      },
    ],
    [handleClaim, handleRelease, pendingId, router],
  );

  return (
    <ExpertDashboardShell pageTitle="Speaking review queue" subtitle="Claim sessions and submit tutor assessments">
      <div className="flex flex-col gap-4">
        <Card padding="md" className="flex flex-wrap items-center gap-3">
          <Mic className="h-5 w-5 text-primary" aria-hidden />
          <div className="flex-1">
            <h2 className="text-base font-bold text-navy">Pending speaking reviews</h2>
            <p className="text-xs text-muted">{totalCount} session{totalCount === 1 ? '' : 's'} awaiting your assessment.</p>
          </div>
          <Button variant="outline" size="sm" onClick={() => void load()} loading={loading}>
            <RefreshCw className="mr-1.5 h-3.5 w-3.5" aria-hidden />
            Refresh
          </Button>
        </Card>

        <FilterBar
          groups={filterGroups}
          selected={selectedFilters}
          onChange={handleFilterChange}
          onClear={handleFilterClear}
        />

        {errorMsg && (
          <InlineAlert variant="error" title="Failed to load queue">
            {errorMsg}
          </InlineAlert>
        )}

        {loading ? (
          <div className="flex flex-col gap-3">
            {[0, 1, 2, 3].map((i) => (
              <Skeleton key={i} className="h-14 w-full" />
            ))}
          </div>
        ) : items.length === 0 ? (
          <EmptyState
            icon={<Headphones className="h-12 w-12" aria-hidden />}
            title="No sessions awaiting review"
            description="When a learner completes a speaking role-play, it will appear here for tutor review."
          />
        ) : (
          <DataTable
            data={items}
            columns={columns}
            keyExtractor={(item) => item.sessionId}
            aria-label="Speaking review queue"
            emptyMessage="No sessions awaiting review."
          />
        )}

        {toast && (
          <Toast
            variant={toast.variant === 'info' ? 'info' : toast.variant === 'success' ? 'success' : 'error'}
            message={toast.message}
            onClose={() => setToast(null)}
          />
        )}
      </div>
    </ExpertDashboardShell>
  );
}
