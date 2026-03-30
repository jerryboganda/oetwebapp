'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Search, Users } from 'lucide-react';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Button } from '@/components/ui/button';
import { Select } from '@/components/ui/form-controls';
import { ExpertFreshnessBadge, ExpertPageHeader, ExpertSectionPanel } from '@/components/domain/expert-surface';
import { fetchExpertLearners, fetchExpertQueueFilterMetadata, isApiError } from '@/lib/api';
import type { ExpertLearnerDirectoryResponse, ExpertQueueFilterMetadata } from '@/lib/types/expert';
import { analytics } from '@/lib/analytics';

type AsyncStatus = 'loading' | 'error' | 'success' | 'empty';

const RELEVANCE_OPTIONS = [
  { value: '', label: 'All review states' },
  { value: 'active', label: 'Active review context' },
  { value: 'overdue', label: 'Overdue or urgent' },
  { value: 'completed', label: 'Completed review history' },
  { value: 'rework', label: 'Rework / queued follow-up' },
];

export default function LearnersIndexPage() {
  const router = useRouter();
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

  return (
    <div className="mx-auto flex max-w-7xl flex-col gap-6 p-4 md:p-8" role="main" aria-label="Assigned learners">
      <ExpertPageHeader
        meta="Privacy-scoped Directory"
        title="Assigned Learners"
        description="Browse only the learners connected to your current or historical expert reviews. Search by learner, profession, sub-test context, or review-state relevance."
        actions={directory ? <ExpertFreshnessBadge value={directory.lastUpdatedAt} /> : undefined}
      />

      <ExpertSectionPanel title="Directory Filters" description="Use the filters below to narrow your learner context without leaving expert scope.">
        <div className="grid grid-cols-1 gap-3 md:grid-cols-[1.2fr_0.7fr_0.7fr_0.8fr]">
          <label className="relative block">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <input
              value={search}
              onChange={(event) => {
                setSearch(event.target.value);
                setPage(1);
              }}
              placeholder="Search learner or review id"
              className="w-full rounded-xl border border-slate-200 bg-white py-2.5 pl-9 pr-3 text-sm text-slate-900 outline-none transition focus:border-primary/40 focus:ring-2 focus:ring-primary/10"
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
      </ExpertSectionPanel>

      <AsyncStateWrapper
        status={status === 'empty' ? 'success' : status}
        onRetry={() => setReloadToken((current) => current + 1)}
        errorMessage={errorMessage ?? undefined}
      >
        {status === 'empty' ? (
          <EmptyState
            icon={<Users className="h-12 w-12 text-slate-400" />}
            title="No learners match the current expert scope"
            description="Try broadening the filters or remove one of the relevance constraints."
          />
        ) : (
          <ExpertSectionPanel
            title="Assigned Learner Directory"
            description={directory ? `${directory.totalCount} learner${directory.totalCount === 1 ? '' : 's'} available in your expert scope.` : 'Loading learner scope.'}
          >
            <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
              {directory?.items.map((learner) => (
                <button
                  key={learner.id}
                  type="button"
                  onClick={() => router.push(`/expert/learners/${learner.id}`)}
                  className="rounded-2xl border border-slate-200 bg-white p-5 text-left transition hover:border-primary/40 hover:shadow-sm"
                >
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <h3 className="text-base font-semibold text-slate-900">{learner.name}</h3>
                      <p className="text-sm text-slate-500">
                        {learner.profession.replace(/_/g, ' ')} | goal {learner.goalScore}
                      </p>
                    </div>
                    <span className="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-medium text-slate-600">
                      {learner.reviewsInScope} review{learner.reviewsInScope === 1 ? '' : 's'}
                    </span>
                  </div>

                  <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
                    <div className="rounded-xl bg-slate-50 p-3">
                      <p className="text-xs font-medium uppercase tracking-[0.12em] text-slate-500">Sub-tests in scope</p>
                      <p className="mt-2 text-sm font-medium text-slate-900">{learner.subTests.join(', ')}</p>
                    </div>
                    <div className="rounded-xl bg-slate-50 p-3">
                      <p className="text-xs font-medium uppercase tracking-[0.12em] text-slate-500">Latest review state</p>
                      <p className="mt-2 text-sm font-medium text-slate-900">{learner.lastReviewState.replace(/_/g, ' ')}</p>
                    </div>
                  </div>

                  <div className="mt-4 flex flex-wrap items-center gap-4 text-xs text-slate-500">
                    <span>Last review {learner.lastReviewId}</span>
                    <span>{learner.lastReviewType}</span>
                    <span>{new Date(learner.lastReviewAt).toLocaleString()}</span>
                    <span>{learner.examDate ? `Exam ${new Date(learner.examDate).toLocaleDateString()}` : 'Exam date not set'}</span>
                  </div>
                </button>
              ))}
            </div>

            {(directory?.totalCount ?? 0) > (directory?.pageSize ?? 0) ? (
              <div className="flex items-center justify-between gap-3 pt-2 text-sm text-slate-500">
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
          </ExpertSectionPanel>
        )}
      </AsyncStateWrapper>
    </div>
  );
}
