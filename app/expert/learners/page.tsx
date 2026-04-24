'use client';

import { useEffect, useMemo, useState } from 'react';
import { CheckCircle2, Clock3, Search, Sparkles, Users } from 'lucide-react';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-error';
import { Select } from '@/components/ui/form-controls';
import {
  ExpertRouteFreshnessBadge,
  ExpertRouteHero,
  ExpertRouteSectionHeader,
  ExpertRouteWorkspace,
} from '@/components/domain/expert-route-surface';
import { LearnerSurfaceCard } from '@/components/domain/learner-surface';
import { fetchExpertLearners, fetchExpertQueueFilterMetadata, isApiError } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { ExpertLearnerDirectoryResponse, ExpertQueueFilterMetadata } from '@/lib/types/expert';

type AsyncStatus = 'loading' | 'error' | 'success' | 'empty';

const RELEVANCE_OPTIONS = [
  { value: '', label: 'All review states' },
  { value: 'active', label: 'Active review context' },
  { value: 'overdue', label: 'Overdue or urgent' },
  { value: 'completed', label: 'Completed review history' },
  { value: 'rework', label: 'Rework / queued follow-up' },
];

export default function LearnersIndexPage() {
  const [directory, setDirectory] = useState<ExpertLearnerDirectoryResponse | null>(null);
  const [metadata, setMetadata] = useState<ExpertQueueFilterMetadata | null>(null);
  const [status, setStatus] = useState<AsyncStatus>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [profession, setProfession] = useState('');
  const [subTest, setSubTest] = useState('');
  const [relevance, setRelevance] = useState('');
  const [page, setPage] = useState(1);
  const [reloadToken, setReloadToken] = useState(0);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        setStatus('loading');
        setErrorMessage(null);
        const [directoryData, metadataData] = await Promise.all([
          fetchExpertLearners({ search, profession, subTest, relevance, page, pageSize: 12 }),
          metadata ? Promise.resolve(metadata) : fetchExpertQueueFilterMetadata(),
        ]);

        if (cancelled) return;

        setDirectory(directoryData);
        setMetadata(metadataData);
        setStatus(directoryData.items.length === 0 ? 'empty' : 'success');
        analytics.track('expert_learners_viewed', {
          totalCount: directoryData.totalCount,
          profession: profession || null,
          subTest: subTest || null,
        });
      } catch (error) {
        if (!cancelled) {
          setErrorMessage(isApiError(error) ? error.userMessage : 'Unable to load assigned learners right now.');
          setStatus('error');
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [metadata, page, profession, relevance, reloadToken, search, subTest]);

  const professionOptions = useMemo(() => {
    const values = metadata?.professions ?? [];
    return [{ value: '', label: 'All professions' }, ...values.map((value) => ({ value, label: value.replace(/_/g, ' ') }))];
  }, [metadata?.professions]);

  const subTestOptions = useMemo(() => {
    const values = metadata?.types ?? [];
    return [{ value: '', label: 'All sub-tests' }, ...values.map((value) => ({ value, label: value }))];
  }, [metadata?.types]);

  const urgentCount = directory?.items.filter((learner) => learner.lastReviewState === 'overdue' || learner.lastReviewState === 'rework').length ?? 0;
  const activeCount = directory?.items.filter((learner) => learner.lastReviewState === 'active').length ?? 0;

  return (
    <ExpertRouteWorkspace role="main" aria-label="Assigned learners">
      <AsyncStateWrapper
        status={status === 'empty' ? 'success' : status}
        onRetry={() => setReloadToken((current) => current + 1)}
        errorMessage={errorMessage ?? undefined}
        emptyContent={
          <EmptyState
            icon={<Users className="h-12 w-12 text-slate-400" />}
            title="No learners match the current expert scope"
            description="Try broadening the filters or remove one of the relevance constraints."
          />
        }
      >
        <div className="space-y-6">
          <ExpertRouteHero
            eyebrow="Privacy-scoped Directory"
            icon={Sparkles}
            accent="primary"
            title="Assigned Learners"
            description="Browse only the learners connected to your current or historical expert reviews. Search by learner, profession, sub-test context, or review-state relevance."
            highlights={[
              { icon: Users, label: 'Learners in scope', value: String(directory?.totalCount ?? 0) },
              { icon: CheckCircle2, label: 'Active review context', value: String(activeCount) },
              { icon: Clock3, label: 'Urgent or overdue', value: String(urgentCount) },
            ]}
            aside={directory ? <ExpertRouteFreshnessBadge value={directory.lastUpdatedAt} /> : undefined}
          />

          <section className="space-y-4">
            <ExpertRouteSectionHeader
              eyebrow="Directory Filters"
              title="Narrow learner scope"
              description="Use the filters below to narrow your learner context without leaving expert scope."
            />
            <Card>
              <CardContent className="space-y-4 p-5">
                <div className="grid grid-cols-1 gap-3 md:grid-cols-[1.2fr_0.7fr_0.7fr_0.8fr]">
                  <label className="relative block">
                    <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
                    <input
                      value={search}
                      onChange={(event) => {
                        setSearch(event.target.value);
                        setPage(1);
                      }}
                      placeholder="Search learner or review id"
                      className="w-full rounded-xl border border-border bg-surface py-2.5 pl-9 pr-3 text-sm outline-none transition focus:border-primary/40 focus:ring-2 focus:ring-primary/20"
                    />
                  </label>
                  <Select
                    value={profession}
                    onChange={(event) => {
                      setProfession(event.target.value);
                      setPage(1);
                    }}
                    options={professionOptions}
                    aria-label="Filter learners by profession"
                  />
                  <Select
                    value={subTest}
                    onChange={(event) => {
                      setSubTest(event.target.value);
                      setPage(1);
                    }}
                    options={subTestOptions}
                    aria-label="Filter learners by sub-test"
                  />
                  <Select
                    value={relevance}
                    onChange={(event) => {
                      setRelevance(event.target.value);
                      setPage(1);
                    }}
                    options={RELEVANCE_OPTIONS}
                    aria-label="Filter learners by review relevance"
                  />
                </div>
                <div className="flex items-center justify-between gap-3 text-xs text-muted">
                  <span>{directory ? `${directory.totalCount} learner${directory.totalCount === 1 ? '' : 's'} in scope.` : 'Loading learner scope.'}</span>
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => {
                      setSearch('');
                      setProfession('');
                      setSubTest('');
                      setRelevance('');
                      setPage(1);
                    }}
                  >
                    Clear filters
                  </Button>
                </div>
              </CardContent>
            </Card>
          </section>

          <section className="space-y-4">
            <ExpertRouteSectionHeader
              eyebrow="Assigned Learner Directory"
              title="Learner cards"
              description={directory ? `${directory.totalCount} learner${directory.totalCount === 1 ? '' : 's'} available in your expert scope.` : 'Loading learner scope.'}
            />

            <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
              {directory?.items.map((learner) => (
                <LearnerSurfaceCard
                  key={learner.id}
                  card={{
                    kind: 'navigation',
                    sourceType: 'frontend_navigation',
                    eyebrow: learner.lastReviewState.replace(/_/g, ' '),
                    title: learner.name,
                    description: `${learner.profession.replace(/_/g, ' ')} · goal ${learner.goalScore}`,
                    accent: learner.lastReviewState === 'overdue' || learner.lastReviewState === 'rework' ? 'amber' : 'primary',
                    metaItems: [
                      { label: `${learner.reviewsInScope} review${learner.reviewsInScope === 1 ? '' : 's'} in scope` },
                      { label: learner.subTests.join(', ') },
                      { label: learner.examDate ? `Exam ${new Date(learner.examDate).toLocaleDateString()}` : 'Exam date not set' },
                    ],
                    statusLabel: learner.lastReviewType,
                    primaryAction: {
                      label: 'Open Profile',
                      href: `/expert/learners/${learner.id}`,
                    },
                  }}
                />
              ))}
            </div>

            {(directory?.totalCount ?? 0) > (directory?.pageSize ?? 0) ? (
              <div className="flex items-center justify-between gap-3 pt-2 text-sm text-muted">
                <span>
                  Page {directory?.page ?? 1} of {Math.max(1, Math.ceil((directory?.totalCount ?? 1) / (directory?.pageSize ?? 1)))}
                </span>
                <div className="flex items-center gap-2">
                  <Button variant="outline" size="sm" disabled={(directory?.page ?? 1) <= 1} onClick={() => setPage((current) => Math.max(1, current - 1))}>
                    Previous
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={(directory?.page ?? 1) >= Math.ceil((directory?.totalCount ?? 1) / (directory?.pageSize ?? 1))}
                    onClick={() => setPage((current) => current + 1)}
                  >
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
