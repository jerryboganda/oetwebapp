'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { cardClassName } from '@/components/ui/card';
import { LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { WritingReleaseCountdown } from '@/components/domain/writing/WritingReleaseCountdown';
import { formatDateTime } from '@/lib/domain/datetime';
import { getWritingMyWork, retryWritingGrade } from '@/lib/writing/api';
import { toCandidateSafeWritingErrorMessage } from '@/lib/writing/submit-keys';
import type {
  WritingMyWorkActionKind,
  WritingMyWorkItemDto,
  WritingMyWorkState,
} from '@/lib/writing/types';
import { cn } from '@/lib/utils';

// Post Submissions (the "Past submissions" Writing view): the learner's
// unconsumed drafts and every V2 submission from GET /v1/writing/my-work,
// newest activity first. States, actions and their routes are server-computed;
// this list only renders them. Test ids are the live-QA harness contract.

const PAGE_SIZE = 20;

const STATE_BADGE: Record<WritingMyWorkState, NonNullable<BadgeProps['variant']>> = {
  draft: 'info',
  grading: 'warning',
  failed: 'danger',
  graded: 'success',
};

const ACTION_TEST_ID: Record<WritingMyWorkActionKind, string> = {
  resume: 'post-submission-resume',
  wait: 'post-submission-wait',
  retry: 'post-submission-retry',
  open_result: 'post-submission-open',
  view_letter: 'post-submission-view',
};

const LETTER_TYPE_CODES = new Set(['LT-RR', 'LT-UR', 'LT-DG', 'LT-TR', 'LT-NM', 'LT-OT']);

// An item plus the server clock of the page it came from: each page has its own
// `serverNow`, and a row's countdown is anchored to the clock of its own page.
type MyWorkRow = WritingMyWorkItemDto & { serverNow?: string };

export interface WritingMyWorkListProps {
  /** Loaded item count, or null while loading / after a failed load (never "empty" then). */
  onCountChange?: (count: number | null) => void;
}

export function WritingMyWorkList({ onCountChange }: WritingMyWorkListProps) {
  const t = useTranslations();
  const router = useRouter();
  const [items, setItems] = useState<MyWorkRow[] | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loadFailed, setLoadFailed] = useState(false);
  const [reloadToken, setReloadToken] = useState(0);
  const [loadingMore, setLoadingMore] = useState(false);
  const [loadMoreFailed, setLoadMoreFailed] = useState(false);
  const [retryingKey, setRetryingKey] = useState<string | null>(null);
  const [retryError, setRetryError] = useState<{ key: string; message: string } | null>(null);
  // A ref, not state: a double-click lands before the re-render that disables the button.
  const retryInFlight = useRef(false);

  useEffect(() => {
    let cancelled = false;
    getWritingMyWork({ limit: PAGE_SIZE })
      .then((page) => {
        if (cancelled) return;
        setItems(page.items.map((item) => ({ ...item, serverNow: page.serverNow })));
        setHasMore(page.hasMore);
      })
      .catch(() => {
        if (!cancelled) setLoadFailed(true);
      });
    return () => {
      cancelled = true;
    };
  }, [reloadToken]);

  useEffect(() => {
    onCountChange?.(items ? items.length : null);
  }, [items, onCountChange]);

  const reload = () => {
    setLoadFailed(false);
    setReloadToken((n) => n + 1);
  };

  const loadMore = () => {
    const last = items?.[items.length - 1];
    if (!last || loadingMore) return;
    setLoadingMore(true);
    setLoadMoreFailed(false);
    getWritingMyWork({ limit: PAGE_SIZE, before: last.lastActivityAt })
      .then((page) => {
        // Keyset paging on a timestamp can repeat a boundary row; keep keys unique.
        setItems((prev) => {
          const seen = new Set((prev ?? []).map((item) => item.key));
          return [
            ...(prev ?? []),
            ...page.items.filter((item) => !seen.has(item.key)).map((item) => ({ ...item, serverNow: page.serverNow })),
          ];
        });
        setHasMore(page.hasMore);
      })
      .catch(() => setLoadMoreFailed(true))
      .finally(() => setLoadingMore(false));
  };

  // Retry re-grades the SAME submission (never a new POST /submissions), once
  // per click, then opens its grading page. It stays disabled while navigating.
  const retryGrade = (item: WritingMyWorkItemDto, href: string) => {
    if (retryInFlight.current || !item.submissionId) return;
    retryInFlight.current = true;
    setRetryingKey(item.key);
    setRetryError(null);
    retryWritingGrade(item.submissionId)
      .then(() => router.push(href))
      .catch((err) => {
        retryInFlight.current = false;
        setRetryingKey(null);
        setRetryError({ key: item.key, message: toCandidateSafeWritingErrorMessage(err, t('writing.myWork.error.retry')) });
      });
  };

  // Nothing to show: the page's own empty state speaks for the whole view.
  if (items && items.length === 0) return null;

  const stateLabels: Record<WritingMyWorkState, string> = {
    draft: t('writing.myWork.state.draft'),
    grading: t('writing.myWork.state.grading'),
    failed: t('writing.myWork.state.failed'),
    graded: t('writing.myWork.state.graded'),
  };
  const actionLabels: Record<WritingMyWorkActionKind, string> = {
    resume: t('writing.myWork.actions.resume'),
    wait: t('writing.myWork.actions.wait'),
    retry: t('writing.myWork.actions.retry'),
    open_result: t('writing.myWork.actions.openResult'),
    view_letter: t('writing.myWork.actions.viewLetter'),
  };

  return (
    <section className="space-y-4">
      <LearnerSurfaceSectionHeader
        eyebrow={t('writing.myWork.eyebrow')}
        title={t('writing.myWork.title')}
        description={t('writing.myWork.description')}
      />

      {loadFailed ? (
        // A failed request is never shown as "no submissions".
        <InlineAlert
          variant="error"
          action={(
            <Button variant="outline" size="sm" onClick={reload}>
              {t('writing.myWork.tryAgain')}
            </Button>
          )}
        >
          {t('writing.myWork.error.load')}
        </InlineAlert>
      ) : !items ? (
        <div className="space-y-2" aria-hidden="true">
          {[1, 2].map((i) => (
            <Skeleton key={i} className="h-20 rounded-2xl" />
          ))}
        </div>
      ) : (
        <>
          <ul className="space-y-2" data-testid="post-submissions-list" aria-label={t('writing.myWork.listLabel')}>
            {items.map((item) => {
              const letterType = item.letterType
                ? LETTER_TYPE_CODES.has(item.letterType)
                  ? t(`writing.practice.library.letterType.${item.letterType}`)
                  : item.letterType
                : null;
              const time = formatDateTime(item.lastActivityAt);
              return (
                <li
                  key={item.key}
                  data-testid="post-submission-row"
                  data-submission-id={item.submissionId ?? undefined}
                  data-scenario-id={item.scenarioId}
                  data-state={item.state}
                  className={cn(cardClassName({ padding: 'sm' }), 'flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between')}
                >
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-1.5">
                      <Badge variant={STATE_BADGE[item.state]}>{stateLabels[item.state]}</Badge>
                      {item.state === 'grading' && item.releaseAt ? (
                        <WritingReleaseCountdown
                          compact
                          releaseAt={item.releaseAt}
                          serverNow={item.serverNow}
                          releaseState={item.releaseState}
                          onHeldElapsed={reload}
                        />
                      ) : null}
                      {letterType ? <Badge variant="muted">{letterType}</Badge> : null}
                      {item.isFreeSample ? <Badge variant="violet">{t('writing.hub.freeSample.badge')}</Badge> : null}
                    </div>
                    {/* Scenario titles are OET-authored English content. */}
                    <p className="mt-1.5 truncate text-sm font-bold text-navy" dir="auto">
                      {item.title || t('writing.myWork.untitled')}
                    </p>
                    <p className="mt-0.5 text-xs text-muted">
                      {t('writing.myWork.words', { count: item.wordCount })}
                      {' · '}
                      {item.kind === 'draft'
                        ? t('writing.myWork.savedAt', { time })
                        : t('writing.myWork.submittedAt', { time })}
                    </p>
                    {item.state === 'grading' && item.autoRetrying ? (
                      <p className="mt-1 text-xs font-semibold text-warning-strong">{t('writing.myWork.delayed')}</p>
                    ) : null}
                    {retryError?.key === item.key ? (
                      <p role="alert" className="mt-1 text-xs font-semibold text-danger-strong">{retryError.message}</p>
                    ) : null}
                  </div>
                  <div className="flex shrink-0 flex-wrap gap-2">
                    {/* A result opens only once the server reports the row as graded (released). */}
                    {item.actions.filter((action) => item.state !== 'grading' || action.kind !== 'open_result').map((action) => {
                      const testId = ACTION_TEST_ID[action.kind];
                      if (!testId) return null; // an action kind this client does not know yet
                      if (action.kind === 'retry') {
                        return item.submissionId ? (
                          <Button
                            key={`${action.kind}:${action.href}`}
                            size="sm"
                            data-testid={testId}
                            disabled={retryingKey !== null}
                            onClick={() => retryGrade(item, action.href)}
                          >
                            {retryingKey === item.key ? t('writing.myWork.actions.retrying') : actionLabels.retry}
                          </Button>
                        ) : null;
                      }
                      const secondary = action.kind === 'wait' || action.kind === 'view_letter';
                      return (
                        <Button key={`${action.kind}:${action.href}`} asChild size="sm" variant={secondary ? 'outline' : 'primary'}>
                          <Link href={action.href} data-testid={testId}>
                            {actionLabels[action.kind]}
                          </Link>
                        </Button>
                      );
                    })}
                  </div>
                </li>
              );
            })}
          </ul>

          {loadMoreFailed ? <InlineAlert variant="error">{t('writing.myWork.error.loadMore')}</InlineAlert> : null}

          {hasMore ? (
            <div className="flex justify-center">
              <Button variant="outline" onClick={loadMore} loading={loadingMore}>
                {t('writing.myWork.loadMore')}
              </Button>
            </div>
          ) : null}
        </>
      )}
    </section>
  );
}
