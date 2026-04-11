'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { Clock3, Inbox, RefreshCw, Search, Sparkles, Unlock, Users } from 'lucide-react';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import {
  ExpertRouteFreshnessBadge,
  ExpertRouteHero,
  ExpertRouteSectionHeader,
  ExpertRouteSummaryCard,
  ExpertRouteWorkspace,
} from '@/components/domain/expert-route-surface';
import { claimReview, fetchExpertQueueFilterMetadata, fetchReviewQueue, isApiError, releaseReview } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { type ExpertQueueFilterMetadata, type ReviewRequest } from '@/lib/types/expert';

type AsyncStatus = 'loading' | 'error' | 'empty' | 'success';

const FILTER_KEYS = ['type', 'profession', 'priority', 'status', 'confidence', 'assignment', 'overdue'] as const;
const PAGE_SIZE = 25;

function toLabel(value: string) {
  return value.replace(/_/g, ' ').replace(/\b\w/g, (match) => match.toUpperCase());
}

function buildFilterGroups(metadata: ExpertQueueFilterMetadata | null): FilterGroup[] {
  return [
    { id: 'type', label: 'Sub-test', options: (metadata?.types ?? ['writing', 'speaking']).map((value) => ({ id: value, label: toLabel(value) })) },
    { id: 'profession', label: 'Profession', options: (metadata?.professions ?? []).map((value) => ({ id: value, label: toLabel(value) })) },
    { id: 'priority', label: 'Priority', options: (metadata?.priorities ?? ['high', 'normal']).map((value) => ({ id: value, label: toLabel(value) })) },
    { id: 'status', label: 'Status', options: (metadata?.statuses ?? ['queued', 'assigned', 'in_progress', 'overdue']).map((value) => ({ id: value, label: toLabel(value) })) },
    { id: 'confidence', label: 'AI Confidence', options: (metadata?.confidenceBands ?? ['high', 'medium', 'low', 'unknown']).map((value) => ({ id: value, label: toLabel(value) })) },
    { id: 'assignment', label: 'Assignment', options: (metadata?.assignmentStates ?? ['assigned', 'unassigned']).map((value) => ({ id: value, label: toLabel(value) })) },
    { id: 'overdue', label: 'Overdue', options: [{ id: 'true', label: 'Only Overdue' }] },
  ];
}

function parseFilters(searchParams: URLSearchParams | null): Record<string, string[]> {
  const initial: Record<string, string[]> = {};
  FILTER_KEYS.forEach((key) => {
    const value = searchParams?.get(key);
    if (value) {
      initial[key] = value.split(',').filter(Boolean);
    }
  });
  return initial;
}

export default function ReviewQueuePage() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const [data, setData] = useState<ReviewRequest[]>([]);
  const [status, setStatus] = useState<AsyncStatus>('loading');
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [searchQuery, setSearchQuery] = useState(() => searchParams?.get('search') ?? '');
  const [selectedFilters, setSelectedFilters] = useState<Record<string, string[]>>(() => parseFilters(searchParams));
  const [lastRefreshed, setLastRefreshed] = useState<string | null>(null);
  const [lastUpdatedAt, setLastUpdatedAt] = useState<string | null>(null);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [isRowActionPending, setIsRowActionPending] = useState<string | null>(null);
  const [showStaleWarning, setShowStaleWarning] = useState(false);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(() => Math.max(1, Number(searchParams?.get('page') ?? '1')));
  const [metadata, setMetadata] = useState<ExpertQueueFilterMetadata | null>(null);

  useEffect(() => {
    setSelectedFilters(parseFilters(searchParams));
    setSearchQuery(searchParams?.get('search') ?? '');
    setPage(Math.max(1, Number(searchParams?.get('page') ?? '1')));
  }, [searchParams]);

  const updateUrl = useCallback((nextFilters: Record<string, string[]>, nextSearch: string, nextPage: number) => {
    const params = new URLSearchParams();
    Object.entries(nextFilters).forEach(([key, values]) => {
      if (values.length > 0) {
        params.set(key, values.join(','));
      }
    });
    if (nextSearch.trim()) {
      params.set('search', nextSearch.trim());
    }
    if (nextPage > 1) {
      params.set('page', String(nextPage));
    }
    const query = params.toString();
    router.replace(query ? `${pathname}?${query}` : pathname, { scroll: false });
  }, [pathname, router]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const response = await fetchExpertQueueFilterMetadata();
        if (!cancelled) {
          setMetadata(response);
        }
      } catch {
        // Queue can still render with fallback labels if metadata fetch fails.
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    const timeout = window.setTimeout(() => {
      updateUrl(selectedFilters, searchQuery, page);
    }, 250);
    return () => window.clearTimeout(timeout);
  }, [page, searchQuery, selectedFilters, updateUrl]);

  const requestParams = useMemo(() => ({
    search: searchQuery.trim() || undefined,
    type: selectedFilters.type,
    profession: selectedFilters.profession,
    priority: selectedFilters.priority,
    status: selectedFilters.status,
    confidence: selectedFilters.confidence,
    assignment: selectedFilters.assignment,
    overdue: selectedFilters.overdue?.includes('true') || undefined,
    page,
    pageSize: PAGE_SIZE,
  }), [page, searchQuery, selectedFilters]);

  const loadQueue = useCallback(async (showLoadingState = true) => {
    try {
      if (showLoadingState) {
        setStatus('loading');
      }
      setErrorMsg(null);
      const response = await fetchReviewQueue(requestParams);
      setData(response.items);
      setTotalCount(response.totalCount);
      setStatus(response.items.length === 0 ? 'empty' : 'success');
      setLastUpdatedAt(response.lastUpdatedAt);
      setLastRefreshed(new Date(response.lastUpdatedAt).toLocaleTimeString());
      setShowStaleWarning(false);
    } catch (error) {
      const message = isApiError(error) ? error.userMessage : 'Failed to load the review queue. Please try again.';
      setErrorMsg(message);
      setStatus('error');
    }
  }, [requestParams]);

  useEffect(() => {
    void loadQueue();
    analytics.track('review_queue_viewed', { search: requestParams.search ?? null });
    const interval = window.setInterval(() => { void loadQueue(false); }, 2 * 60 * 1000);
    return () => window.clearInterval(interval);
  }, [loadQueue, requestParams.search]);

  useEffect(() => {
    const timer = window.setTimeout(() => setShowStaleWarning(true), 5 * 60 * 1000);
    return () => window.clearTimeout(timer);
  }, [lastRefreshed]);

  const handleRefresh = async () => {
    setIsRefreshing(true);
    await loadQueue(false);
    setIsRefreshing(false);
  };

  const handleFilterChange = (groupId: string, optionId: string) => {
    setPage(1);
    setSelectedFilters((current) => {
      const currentValues = current[groupId] ?? [];
      const nextValues = currentValues.includes(optionId)
        ? currentValues.filter((value) => value !== optionId)
        : [...currentValues, optionId];
      return { ...current, [groupId]: nextValues };
    });
  };

  const clearFilters = () => {
    setSelectedFilters({});
    setSearchQuery('');
    setPage(1);
  };

  const openReview = useCallback(async (review: ReviewRequest) => {
    const route = `/expert/review/${review.type}/${review.id}`;
    try {
      setIsRowActionPending(review.id);
      if (review.availableActions?.canClaim) {
        await claimReview(review.id);
      }
      router.push(route);
    } catch (error) {
      const message = isApiError(error) ? error.userMessage : 'Unable to open this review right now.';
      setToast({ variant: 'error', message });
      await loadQueue(false);
    } finally {
      setIsRowActionPending(null);
    }
  }, [loadQueue, router]);

  const handleRelease = useCallback(async (review: ReviewRequest) => {
    try {
      setIsRowActionPending(review.id);
      await releaseReview(review.id);
      setToast({ variant: 'success', message: `Released ${review.id} back to the queue.` });
      await loadQueue(false);
    } catch (error) {
      const message = isApiError(error) ? error.userMessage : 'Unable to release this review right now.';
      setToast({ variant: 'error', message });
    } finally {
      setIsRowActionPending(null);
    }
  }, [loadQueue]);

  const hasActiveFilters = Object.values(selectedFilters).some((values) => values.length > 0) || !!searchQuery.trim();
  const filterGroups = useMemo(() => buildFilterGroups(metadata), [metadata]);
  const overdueCount = useMemo(() => data.filter((item) => item.isOverdue).length, [data]);
  const assignedCount = useMemo(() => data.filter((item) => item.assignmentState === 'claimed' || item.assignmentState === 'assigned').length, [data]);
  const draftReadyCount = useMemo(() => data.filter((item) => item.status === 'in_progress').length, [data]);

  const columns: Column<ReviewRequest>[] = [
    { key: 'id', header: 'Review ID', render: (row) => <span className="font-mono text-xs">{row.id}</span> },
    {
      key: 'learner',
      header: 'Learner',
      render: (row) => (
        <button
          className="text-left font-medium text-primary hover:underline"
          onClick={(event) => {
            event.stopPropagation();
            router.push(`/expert/learners/${row.learnerId}`);
          }}
          aria-label={`View profile for ${row.learnerName}`}
        >
          {row.learnerName}
        </button>
      ),
    },
    { key: 'profession', header: 'Profession', render: (row) => <span className="capitalize">{row.profession.replace('_', ' ')}</span> },
    { key: 'type', header: 'Sub-test', render: (row) => <span className="capitalize">{row.type}</span> },
    { key: 'aiConfidence', header: 'AI Confidence', render: (row) => row.aiConfidence === 'unknown' ? <span className="text-muted text-xs">Unknown</span> : <span className="capitalize">{row.aiConfidence}</span> },
    { key: 'priority', header: 'Priority', render: (row) => <span className={row.priority === 'high' ? 'font-semibold capitalize text-error' : 'capitalize'}>{row.priority}</span> },
    {
      key: 'slaDue',
      header: 'SLA Due',
      render: (row) => {
        const date = new Date(row.slaDue);
        const formatted = `${date.toISOString().split('T')[0]} ${date.toISOString().split('T')[1].slice(0, 5)}`;
        return <span className={row.isOverdue ? 'font-bold text-error' : row.slaState === 'at_risk' ? 'font-semibold text-amber-600' : ''}>{formatted} UTC</span>;
      },
    },
    { key: 'assignedReviewer', header: 'Assigned Reviewer', render: (row) => <span className={row.assignedReviewerName ? 'text-navy' : 'italic text-muted'}>{row.assignedReviewerName ?? 'Unassigned'}</span> },
    {
      key: 'status',
      header: 'Status',
      render: (row) => {
        const variant = row.status === 'overdue' ? 'bg-red-50 text-red-700' : row.status === 'assigned' ? 'bg-blue-50 text-blue-700' : row.status === 'in_progress' ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-700';
        return <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${variant}`}>{row.status.replace('_', ' ')}</span>;
      },
    },
    {
      key: 'actions',
      header: 'Action',
      render: (row) => {
        const pending = isRowActionPending === row.id;
        return (
          <div className="flex items-center gap-2">
            <Button
              size="sm"
              onClick={(event) => {
                event.stopPropagation();
                void openReview(row);
              }}
              disabled={pending || !(row.availableActions?.canOpen || row.availableActions?.canClaim)}
              aria-label={row.availableActions?.canClaim ? `Claim and review ${row.id}` : `Open ${row.id}`}
            >
              {pending ? 'Working...' : row.availableActions?.canClaim ? 'Claim & Review' : 'Open'}
            </Button>
            {row.availableActions?.canRelease ? (
              <Button
                size="sm"
                variant="outline"
                onClick={(event) => {
                  event.stopPropagation();
                  void handleRelease(row);
                }}
                disabled={pending}
                aria-label={`Release ${row.id}`}
              >
                <Unlock className="mr-1 h-4 w-4" /> Release
              </Button>
            ) : null}
          </div>
        );
      },
    },
  ];

  return (
    <ExpertRouteWorkspace role="main" aria-label="Review Queue">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AsyncStateWrapper
        status={status}
        onRetry={() => void loadQueue()}
        errorMessage={errorMsg ?? undefined}
        emptyContent={
          <EmptyState
            icon={<Inbox className="h-12 w-12 text-muted" />}
            title={hasActiveFilters ? 'No reviews match the current filters' : 'No reviews in queue'}
            description={hasActiveFilters ? 'Try clearing some filters or broadening your search.' : 'New reviews will appear here when learner requests are ready for expert handling.'}
          />
        }
      >
        <div className="space-y-6">
          <ExpertRouteHero
            eyebrow="Queue Operations"
            icon={Sparkles}
            accent="primary"
            title="Review queue"
            description="Claim, release, and prioritize pending learner submissions from a learner-style workspace that keeps the next decision in view."
            highlights={[
              { icon: Inbox, label: 'Visible queue items', value: String(totalCount) },
              { icon: Users, label: 'Assigned / claimed', value: String(assignedCount) },
              { icon: Clock3, label: 'Overdue in view', value: String(overdueCount) },
            ]}
            aside={(
              <div className="space-y-3">
                <ExpertRouteFreshnessBadge value={lastUpdatedAt} />
                <div className="flex flex-wrap gap-2">
                  <Button variant="outline" onClick={() => router.push('/expert/learners')}>
                    Learners
                  </Button>
                  <Button variant="outline" onClick={handleRefresh} disabled={isRefreshing}>
                    <RefreshCw className={`mr-2 h-4 w-4 ${isRefreshing ? 'animate-spin' : ''}`} />
                    Refresh
                  </Button>
                </div>
              </div>
            )}
          />

          <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
            <ExpertRouteSummaryCard
              label="Visible Queue Items"
              value={totalCount}
              hint="Results after your current filters."
              icon={Inbox}
            />
            <ExpertRouteSummaryCard
              label="Assigned / Claimed"
              value={assignedCount}
              hint="Items already in owned workflow scope on this page."
              accent={assignedCount > 0 ? 'navy' : 'emerald'}
              icon={Unlock}
            />
            <ExpertRouteSummaryCard
              label="Overdue In View"
              value={overdueCount}
              hint={`${draftReadyCount} in-progress item(s) currently visible.`}
              accent={overdueCount > 0 ? 'amber' : 'emerald'}
              icon={Clock3}
            />
          </div>

          <InlineAlert variant="info" title="Ownership" action={<span className="text-xs">Claim a shared review before entering the workspace.</span>}>
            Shared queue items remain locked to the reviewer who claims them first. Release a claimed review if you are handing it back.
          </InlineAlert>

          {showStaleWarning && status === 'success' ? (
            <InlineAlert variant="warning" dismissible>
              Queue data may be outdated. Refresh the queue before claiming time-sensitive work.
            </InlineAlert>
          ) : null}

          <section className="space-y-4">
            <ExpertRouteSectionHeader
              eyebrow="Queue Controls"
              title="Find the right work fast"
              description="Search by review ID or learner name, then narrow the queue with scoped filters."
              action={hasActiveFilters ? <Button variant="ghost" size="sm" onClick={clearFilters}>Clear filters</Button> : null}
            />

            <Card className="overflow-hidden">
              <CardContent className="space-y-4 p-5">
                <div className="relative max-w-sm">
                  <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
                  <input
                    type="text"
                    placeholder="Search by Review ID or Learner Name..."
                    value={searchQuery}
                    onChange={(event) => {
                      setSearchQuery(event.target.value);
                      setPage(1);
                    }}
                    className="w-full rounded-xl border border-border bg-surface py-2.5 pl-9 pr-3 text-sm focus:outline-none focus:ring-2 focus:ring-primary/20"
                    aria-label="Search reviews"
                  />
                </div>
                <FilterBar groups={filterGroups} selected={selectedFilters} onChange={handleFilterChange} onClear={clearFilters} />
              </CardContent>
            </Card>
          </section>

          <section className="space-y-4">
            <ExpertRouteSectionHeader
              eyebrow="Queue Table"
              title="Review queue items"
              description={totalCount > 0 ? `${totalCount} review${totalCount === 1 ? '' : 's'} available in the current view.` : 'No active reviews loaded.'}
            />

            <Card padding="none" className="overflow-hidden">
              <CardContent className="p-0">
                <DataTable
                  data={data}
                  columns={columns}
                  keyExtractor={(item) => item.id}
                  emptyMessage="No reviews match the current filters."
                  aria-label="Review queue table"
                />
              </CardContent>
            </Card>

            {totalCount > PAGE_SIZE ? (
              <div className="flex items-center justify-between gap-3 text-sm text-muted">
                <span>
                  Page {page} of {Math.max(1, Math.ceil(totalCount / PAGE_SIZE))}
                </span>
                <div className="flex items-center gap-2">
                  <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage((current) => Math.max(1, current - 1))}>
                    Previous
                  </Button>
                  <Button variant="outline" size="sm" disabled={page >= Math.ceil(totalCount / PAGE_SIZE)} onClick={() => setPage((current) => current + 1)}>
                    Next
                  </Button>
                </div>
              </div>
            ) : null}
          </section>
        </div>
      </AsyncStateWrapper>
    </ExpertRouteWorkspace>
  );
}
