'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import {
  GitCompare,
  Send,
  Clock,
  History,
  CheckCircle2,
  XCircle,
  Download,
  AlertTriangle,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import {
  SubmissionCard,
  SubmissionFilterBar,
  SparklineStrip,
} from '@/components/domain/submissions';
import {
  fetchSubmissionsPage,
  hideSubmission,
  unhideSubmission,
  submissionsExportCsvUrl,
  createBulkReviewRequests,
} from '@/lib/api';
import type { Submission, SubmissionListQuery, SubmissionListResponse } from '@/lib/mock-data';
import { analytics } from '@/lib/analytics';

/**
 * Submission History (learner-facing) — `/submissions`.
 *
 * This page orchestrates:
 *   - Filter/sort/search/pagination state (URL-synced so shareable).
 *   - Canonical scoring via server-provided `scaledScore` + `passState`.
 *   - Compare-pick multi-select mode with a clear CTA.
 *   - Soft hide / unhide with optimistic updates.
 *   - CSV export and bulk review request for up to 5 attempts.
 *   - Sparkline strip over recent scaled scores per sub-test.
 */
export default function SubmissionHistory() {
  const router = useRouter();
  const searchParams = useSearchParams();

  // ── Query state (URL-synced) ──────────────────────────────────────────
  const [query, setQuery] = useState<SubmissionListQuery>(() => readQueryFromUrl(searchParams));
  const [page, setPage] = useState<SubmissionListResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [nextLoading, setNextLoading] = useState(false);

  // Keep URL in sync with query.
  useEffect(() => {
    const qs = writeQueryToUrl(query);
    const current = searchParams?.toString();
    if (qs !== current) {
      router.replace(`/submissions${qs ? `?${qs}` : ''}`, { scroll: false });
    }
  }, [query, router, searchParams]);

  // Fetch on query change. Cancel stale responses.
  const requestIdRef = useRef(0);
  const loadPage = useCallback(async (q: SubmissionListQuery, requestId: number) => {
    setLoading(true);
    setError(null);
    try {
      const res = await fetchSubmissionsPage(q);
      if (requestIdRef.current !== requestId) return;
      setPage(res);
    } catch {
      if (requestIdRef.current !== requestId) return;
      setError('Failed to load submissions. Please try again.');
    } finally {
      if (requestIdRef.current === requestId) setLoading(false);
    }
  }, []);

  useEffect(() => {
    analytics.track('evaluation_viewed', { type: 'submissions' });
    const thisId = ++requestIdRef.current;
    loadPage(query, thisId).catch(() => {});
  }, [query, loadPage]);

  // Apply pass-only filter client-side (country-aware Writing resolution
  // cannot be translated to SQL safely).
  const visibleItems = useMemo(() => {
    if (!page) return [] as Submission[];
    if (!query.passOnly) return page.items;
    return page.items.filter((i) => i.passState === 'pass');
  }, [page, query.passOnly]);

  const pendingReviewCount = useMemo(
    () => (page?.items ?? []).filter((s) => s.reviewStatus === 'pending').length,
    [page],
  );
  const passCount = useMemo(
    () => (page?.items ?? []).filter((s) => s.passState === 'pass').length,
    [page],
  );

  // ── Compare-pick multi-select mode ────────────────────────────────────
  const [compareMode, setCompareMode] = useState(false);
  const [picked, setPicked] = useState<string[]>([]);
  const togglePick = useCallback((id: string) => {
    setPicked((prev) => {
      if (prev.includes(id)) return prev.filter((x) => x !== id);
      if (prev.length >= 2) return [prev[1], id];
      return [...prev, id];
    });
  }, []);
  const startCompare = () => {
    if (picked.length !== 2) return;
    analytics.track('submissions_compare_started', { leftId: picked[0], rightId: picked[1] });
    router.push(`/submissions/compare?leftId=${picked[0]}&rightId=${picked[1]}`);
  };

  // ── Bulk review ───────────────────────────────────────────────────────
  const [bulkRunning, setBulkRunning] = useState(false);
  const [bulkMessage, setBulkMessage] = useState<string | null>(null);
  const pickedSubmissions = useMemo(
    () => visibleItems.filter((i) => picked.includes(i.id)),
    [visibleItems, picked],
  );
  const canBulkReview = pickedSubmissions.length > 0
    && pickedSubmissions.length <= 5
    && pickedSubmissions.every((s) => s.canRequestReview
      && (s.subTest === 'Writing' || s.subTest === 'Speaking'));

  async function submitBulkReview() {
    if (!canBulkReview) return;
    setBulkRunning(true);
    setBulkMessage(null);
    try {
      const result = await createBulkReviewRequests({
        items: pickedSubmissions.map((s) => ({
          attemptId: s.id,
          subtest: s.subTest.toLowerCase(),
          turnaroundOption: 'standard_72h',
          focusAreas: [],
          learnerNotes: null,
          paymentSource: 'credits',
          idempotencyKey: `bulk-${s.id}-${Date.now()}`,
        })),
      });
      const ok = result.items.filter((r) => r.ok).length;
      analytics.track('submissions_bulk_review_requested', { count: pickedSubmissions.length, ok });
      setBulkMessage(`Requested ${ok} of ${result.items.length} reviews.`);
      setPicked([]);
      // Refresh the page to reflect new review status.
      setQuery((q) => ({ ...q }));
    } catch (err) {
      setBulkMessage(err instanceof Error ? err.message : 'Bulk review request failed.');
    } finally {
      setBulkRunning(false);
    }
  }

  // ── Hide / unhide handlers (optimistic) ───────────────────────────────
  async function onHide(id: string) {
    setPage((prev) => prev ? {
      ...prev,
      items: prev.items.map((i) => i.id === id ? { ...i, isHidden: true } : i),
    } : prev);
    analytics.track('submissions_hidden', { submissionId: id });
    try { await hideSubmission(id); } catch { /* noop — next refresh restores truth */ }
  }
  async function onUnhide(id: string) {
    setPage((prev) => prev ? {
      ...prev,
      items: prev.items.map((i) => i.id === id ? { ...i, isHidden: false } : i),
    } : prev);
    analytics.track('submissions_unhidden', { submissionId: id });
    try { await unhideSubmission(id); } catch { /* noop */ }
  }

  // ── Filter bar handlers ───────────────────────────────────────────────
  function onFilterChange(delta: Partial<SubmissionListQuery>) {
    analytics.track('submissions_filter_applied', {
      subtest: delta.subtest ?? null,
      context: delta.context ?? null,
      reviewStatus: delta.reviewStatus ?? null,
      passOnly: delta.passOnly ?? null,
      q: delta.q ?? null,
    });
    setQuery((prev) => ({ ...prev, ...delta, cursor: undefined }));
  }
  function onSortChange(sort: SubmissionListQuery['sort']) {
    analytics.track('submissions_sort_changed', { sort });
    setQuery((prev) => ({ ...prev, sort, cursor: undefined }));
  }
  function onClearFilters() {
    setQuery({ sort: query.sort });
  }
  function onLoadMore() {
    if (!page?.nextCursor) return;
    setNextLoading(true);
    fetchSubmissionsPage({ ...query, cursor: page.nextCursor })
      .then((res) => {
        setPage((prev) => prev ? {
          ...res,
          items: [...prev.items, ...res.items],
        } : res);
      })
      .finally(() => setNextLoading(false));
  }

  function onExportCsv() {
    analytics.track('submissions_exported', {
      subtest: query.subtest ?? null,
      context: query.context ?? null,
      reviewStatus: query.reviewStatus ?? null,
      passOnly: query.passOnly ?? null,
    });
    const url = submissionsExportCsvUrl(query);
    // Use a plain link to let the browser handle the download via cookie/auth.
    const a = document.createElement('a');
    a.href = url;
    a.download = 'submissions.csv';
    document.body.appendChild(a);
    a.click();
    a.remove();
  }

  function onSparklineTileClick(subtest: string) {
    analytics.track('submissions_sparkline_tile_clicked', { subtest });
    setQuery((prev) => ({ ...prev, subtest: prev.subtest === subtest ? undefined : subtest, cursor: undefined }));
  }

  return (
    <LearnerDashboardShell
      pageTitle="Submission History"
      subtitle="Review your past work and follow up on feedback"
      backHref="/"
    >
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Evidence History"
          icon={History}
          accent="slate"
          title="Reopen the attempts that need review or comparison"
          description="Use submission history to find the attempts that still need feedback, comparison, or a fresh follow-up decision."
          highlights={[
            { icon: History, label: 'Attempts', value: `${page?.total ?? 0} recorded` },
            { icon: Clock, label: 'Pending reviews', value: `${pendingReviewCount} waiting` },
            { icon: CheckCircle2, label: 'Passing', value: `${passCount} attempts` },
          ]}
        />

        {/* Sparkline strip */}
        {page && Object.keys(page.sparkline ?? {}).length > 0 ? (
          <SparklineStrip
            data={page.sparkline}
            activeSubtest={query.subtest}
            onTileClick={onSparklineTileClick}
          />
        ) : null}

        {/* Control bar */}
        <SubmissionFilterBar
          query={query}
          facets={page?.facets}
          total={page?.total ?? 0}
          onChange={(delta) => {
            if ('sort' in delta) onSortChange(delta.sort ?? 'date-desc');
            else onFilterChange(delta);
          }}
          onClear={onClearFilters}
          onExportCsv={onExportCsv}
        />

        {/* Compare & bulk toolbar */}
        <div className="flex items-center justify-between gap-3 flex-wrap">
          <div className="flex items-center gap-2">
            <Button
              variant={compareMode ? 'primary' : 'outline'}
              onClick={() => { setCompareMode((v) => !v); setPicked([]); }}
              aria-label={compareMode ? 'Exit compare mode' : 'Enter compare mode'}
            >
              <GitCompare className="w-4 h-4" />
              {compareMode ? 'Exit compare mode' : 'Compare attempts'}
            </Button>
            {compareMode && picked.length === 2 ? (
              <Button variant="primary" onClick={startCompare}>
                Compare selected
              </Button>
            ) : null}
            {compareMode && picked.length > 0 ? (
              <span className="text-sm text-muted">{picked.length}/2 selected</span>
            ) : null}
          </div>
          {compareMode && canBulkReview ? (
            <Button
              variant="outline"
              onClick={submitBulkReview}
              loading={bulkRunning}
            >
              <Send className="w-4 h-4" />
              Request review for {pickedSubmissions.length} selected
            </Button>
          ) : null}
        </div>

        {bulkMessage ? <InlineAlert variant="info">{bulkMessage}</InlineAlert> : null}

        {/* List states */}
        {loading && !page ? (
          <div className="space-y-4" aria-busy="true">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-36 rounded-[24px]" />)}
          </div>
        ) : null}

        {!loading && error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {!loading && !error && visibleItems.length === 0 ? (
          <EmptyState
            title="No submissions match these filters"
            description="Clear filters to see all attempts, or start a writing or speaking task to record new evidence."
            action={{ label: 'Clear filters', onClick: onClearFilters }}
            className="py-16"
          />
        ) : null}

        {!loading && !error && visibleItems.length > 0 ? (
          <section>
            <LearnerSurfaceSectionHeader
              eyebrow="Past Evidence"
              title="Keep review state and score direction visible"
              description="Each card should answer what was submitted, when, how it performed, and whether follow-up is still available."
              className="mb-4"
            />

            <div className="space-y-4">
              {visibleItems.map((submission, idx) => (
                <SubmissionCard
                  key={submission.id}
                  submission={submission}
                  compareMode={compareMode}
                  isComparePicked={picked.includes(submission.id)}
                  onTogglePick={togglePick}
                  onHide={onHide}
                  onUnhide={onUnhide}
                  delayIndex={idx}
                />
              ))}
            </div>

            {page?.nextCursor ? (
              <div className="flex justify-center pt-6">
                <Button variant="outline" onClick={onLoadMore} loading={nextLoading}>
                  Load more
                </Button>
              </div>
            ) : null}
          </section>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}

// ──────────────────────────────────────────────────────────────────────────
// URL state helpers — the full filter state lives in the query string so
// the page is shareable + survives back/forward navigation.
// ──────────────────────────────────────────────────────────────────────────

const ALLOWED_SORTS = new Set(['date-desc', 'date-asc', 'score-desc', 'score-asc']);

function readQueryFromUrl(sp: URLSearchParams | null): SubmissionListQuery {
  const get = (k: string) => sp?.get(k) ?? undefined;
  const sortRaw = get('sort');
  const sort = sortRaw && ALLOWED_SORTS.has(sortRaw) ? (sortRaw as SubmissionListQuery['sort']) : 'date-desc';
  return {
    cursor: undefined,
    limit: 20,
    subtest: get('subtest') || undefined,
    context: get('context') || undefined,
    reviewStatus: get('reviewStatus') || undefined,
    from: get('from') || undefined,
    to: get('to') || undefined,
    passOnly: get('passOnly') === 'true',
    q: get('q') || undefined,
    sort,
    includeHidden: get('includeHidden') === 'true',
  };
}

function writeQueryToUrl(q: SubmissionListQuery): string {
  const p = new URLSearchParams();
  if (q.subtest) p.set('subtest', q.subtest);
  if (q.context) p.set('context', q.context);
  if (q.reviewStatus) p.set('reviewStatus', q.reviewStatus);
  if (q.from) p.set('from', q.from);
  if (q.to) p.set('to', q.to);
  if (q.passOnly) p.set('passOnly', 'true');
  if (q.q) p.set('q', q.q);
  if (q.sort && q.sort !== 'date-desc') p.set('sort', q.sort);
  if (q.includeHidden) p.set('includeHidden', 'true');
  return p.toString();
}
