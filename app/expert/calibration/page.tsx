'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { AlertTriangle, CheckCircle, Clock, GraduationCap, Inbox, MessageSquare, Settings, Sparkles } from 'lucide-react';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { InlineAlert } from '@/components/ui/alert';
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
import { fetchCalibrationCases, fetchCalibrationNotes, isApiError } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { CalibrationCase, CalibrationNote } from '@/lib/types/expert';

type AsyncStatus = 'loading' | 'error' | 'empty' | 'success';

const FILTER_GROUPS: FilterGroup[] = [
  { id: 'profession', label: 'Profession', options: [{ id: 'nursing', label: 'Nursing' }, { id: 'medicine', label: 'Medicine' }, { id: 'dentistry', label: 'Dentistry' }] },
  { id: 'subTest', label: 'Sub-test', options: [{ id: 'writing', label: 'Writing' }, { id: 'speaking', label: 'Speaking' }] },
  { id: 'status', label: 'Status', options: [{ id: 'pending', label: 'Pending' }, { id: 'draft', label: 'Draft' }, { id: 'completed', label: 'Completed' }] },
];

export default function CalibrationCenterPage() {
  const router = useRouter();
  const [cases, setCases] = useState<CalibrationCase[]>([]);
  const [notes, setNotes] = useState<CalibrationNote[]>([]);
  const [pageStatus, setPageStatus] = useState<AsyncStatus>('loading');
  const [filters, setFilters] = useState<Record<string, string[]>>({});
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    try {
      const [data, notesData] = await Promise.all([fetchCalibrationCases(), fetchCalibrationNotes()]);
      setCases(data);
      setNotes(notesData);
      setPageStatus(data.length === 0 ? 'empty' : 'success');
      analytics.track('calibration_viewed', { count: data.length });
    } catch (error) {
      setErrorMessage(isApiError(error) ? error.userMessage : 'Unable to load calibration data right now.');
      setPageStatus('error');
    }
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [data, notesData] = await Promise.all([fetchCalibrationCases(), fetchCalibrationNotes()]);
        if (cancelled) return;
        setCases(data);
        setNotes(notesData);
        setPageStatus(data.length === 0 ? 'empty' : 'success');
        analytics.track('calibration_viewed', { count: data.length });
      } catch (error) {
        if (!cancelled) {
          setErrorMessage(isApiError(error) ? error.userMessage : 'Unable to load calibration data right now.');
          setPageStatus('error');
        }
      }
    })();

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const filtered = useMemo(() => cases.filter((entry) => {
    if (filters.profession?.length && !filters.profession.includes(entry.profession)) return false;
    if (filters.subTest?.length && !filters.subTest.includes(entry.subTest)) return false;
    if (filters.status?.length && !filters.status.includes(entry.status)) return false;
    return true;
  }), [cases, filters]);

  const completedCases = cases.filter((entry) => entry.status === 'completed');
  const openCount = cases.filter((entry) => entry.status !== 'completed').length;
  const alignmentPct = completedCases.length > 0
    ? Math.round(completedCases.reduce((total, entry) => total + (entry.alignmentScore ?? 0), 0) / completedCases.length)
    : 0;

  const columns: Column<CalibrationCase>[] = [
    { key: 'id', header: 'Case ID', render: (row) => <span className="font-mono text-xs">{row.id}</span> },
    { key: 'title', header: 'Title', render: (row) => row.title },
    { key: 'profession', header: 'Profession', render: (row) => <span className="capitalize">{row.profession}</span> },
    { key: 'type', header: 'Type', render: (row) => <span className="capitalize">{row.type}</span> },
    {
      key: 'status',
      header: 'Status',
      render: (row) => {
        const statusStyle = row.status === 'completed'
          ? 'bg-emerald-50 text-emerald-700'
          : row.status === 'draft'
            ? 'bg-blue-50 text-blue-700'
            : 'bg-amber-50 text-amber-700';
        return (
          <span className={`inline-flex rounded-full px-2.5 py-1 text-xs font-medium ${statusStyle}`}>
            {row.status === 'draft' ? 'Draft' : row.status === 'completed' ? 'Completed' : 'Pending'}
          </span>
        );
      },
    },
    {
      key: 'alignment',
      header: 'Alignment',
      render: (row) => {
        if (row.status === 'pending') {
          return <span className="text-xs text-muted">Awaiting submission</span>;
        }

        if (row.status === 'draft') {
          return <span className="text-xs text-blue-700">Draft saved</span>;
        }

        const alignmentScore = row.alignmentScore ?? 0;
        return (
          <span className="text-sm font-medium text-navy">
            {alignmentScore >= 90 ? 'Aligned' : 'Needs review'} ({alignmentScore.toFixed(1)}%)
          </span>
        );
      },
    },
    {
      key: 'actions',
      header: 'Action',
      render: (row) => {
        const actionLabel = row.status === 'draft'
          ? 'Resume draft'
          : row.status === 'pending'
            ? 'Open workspace'
            : 'Review benchmark';
        return (
          <button
            type="button"
            onClick={() => router.push(`/expert/calibration/${row.id}`)}
            className="text-sm font-medium text-primary hover:underline"
          >
            {actionLabel}
          </button>
        );
      },
    },
  ];

  return (
    <ExpertRouteWorkspace role="main" aria-label="Calibration center">
      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => {
          setPageStatus('loading');
          setErrorMessage(null);
          void loadData();
        }}
        errorMessage={errorMessage ?? undefined}
        emptyContent={<EmptyState icon={<Inbox className="h-12 w-12 text-slate-400" />} title="No calibration cases" description="There are no benchmark cases available right now." />}
      >
        <div className="space-y-6">
          <ExpertRouteHero
            eyebrow="Benchmark Review Ops"
            icon={Sparkles}
            accent="primary"
            title="Calibration"
            description="Benchmark cases open in a learner-style workspace with exemplar artifacts, rubric rationale, and alignment evidence instead of the older inline scorer."
            highlights={[
              { icon: CheckCircle, label: 'Alignment', value: `${alignmentPct}%` },
              { icon: Clock, label: 'Open cases', value: String(openCount) },
              { icon: GraduationCap, label: 'Total benchmarks', value: String(cases.length) },
            ]}
            aside={<ExpertRouteFreshnessBadge value={notes[0]?.createdAt} />}
          />

          <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
            <ExpertRouteSummaryCard
              label="Alignment"
              value={`${alignmentPct}%`}
              hint="Completed calibration cases aligned to benchmark."
              accent={alignmentPct >= 90 ? 'emerald' : 'amber'}
              icon={CheckCircle}
            />
            <ExpertRouteSummaryCard
              label="Open Cases"
              value={openCount}
              hint="Pending cases and saved drafts still requiring final submission."
              accent={openCount > 0 ? 'amber' : 'emerald'}
              icon={Clock}
            />
            <ExpertRouteSummaryCard
              label="Total Benchmarks"
              value={cases.length}
              hint="Writing and speaking benchmark cases in scope."
              accent="navy"
              icon={GraduationCap}
            />
          </div>

          <InlineAlert variant="info" title="Advisory benchmark workflow">
            Score each criterion independently, then compare your reasoning with the benchmark rationale. This workspace sharpens rubric consistency without replacing live review judgment.
          </InlineAlert>

          <section className="space-y-4">
            <ExpertRouteSectionHeader
              eyebrow="Calibration Cases"
              title="Open benchmark workspaces"
              description="Use learner-style cards and a decision-first table to inspect exemplar materials and submit criterion-level scores."
            />
            <Card className="space-y-4">
              <CardContent className="space-y-4 p-5">
                <FilterBar
                  groups={FILTER_GROUPS}
                  selected={filters}
                  onChange={(groupId, optionId) => {
                    setFilters((current) => {
                      const currentValues = current[groupId] ?? [];
                      return {
                        ...current,
                        [groupId]: currentValues.includes(optionId)
                          ? currentValues.filter((value) => value !== optionId)
                          : [...currentValues, optionId],
                      };
                    });
                  }}
                  onClear={() => setFilters({})}
                />

                <Card>
                  <CardContent className="p-0">
                    <DataTable
                      data={filtered}
                      columns={columns}
                      keyExtractor={(item) => item.id}
                      emptyMessage="No calibration cases match the current filters."
                      aria-label="Calibration cases table"
                    />
                  </CardContent>
                </Card>
              </CardContent>
            </Card>
          </section>

          <section className="space-y-4">
            <ExpertRouteSectionHeader
              eyebrow="Notes & History"
              title="Calibration activity"
              description="Operational notes, completed calibrations, and system-assigned benchmark activity."
            />
            <Card className="overflow-hidden">
              <CardContent className="p-5">
                {notes.length === 0 ? (
                  <p className="text-sm italic text-muted">No calibration activity recorded yet.</p>
                ) : (
                  <ol className="relative ml-2 space-y-4 border-l border-border">
                    {notes.map((note) => (
                      <li key={note.id} className="ml-4">
                        <span className={`absolute -left-2 h-4 w-4 rounded-full border-2 border-white ${note.type === 'completed' ? 'bg-emerald-400' : note.type === 'comment' ? 'bg-blue-400' : 'bg-border'}`} />
                        <div className="mb-0.5 flex items-center gap-2">
                          {note.type === 'completed' ? <CheckCircle className="h-3.5 w-3.5 text-emerald-600" /> : null}
                          {note.type === 'comment' ? <MessageSquare className="h-3.5 w-3.5 text-blue-600" /> : null}
                          {note.type === 'system' ? <Settings className="h-3.5 w-3.5 text-muted" /> : null}
                          <time className="text-xs text-muted">{new Date(note.createdAt).toLocaleString()}</time>
                        </div>
                        <div className="flex items-start justify-between gap-3">
                          <p className="text-sm text-navy">{note.message}</p>
                          {note.caseId ? (
                            <button
                              type="button"
                              onClick={() => router.push(`/expert/calibration/${note.caseId}`)}
                              className="text-xs font-medium text-primary hover:underline"
                            >
                              Open case
                            </button>
                          ) : null}
                        </div>
                      </li>
                    ))}
                  </ol>
                )}
              </CardContent>
            </Card>
          </section>

          {openCount > 0 ? (
            <InlineAlert variant="warning">
              <div className="flex items-start gap-3">
                <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0" />
                <p>{openCount} calibration case{openCount === 1 ? '' : 's'} still need attention. Open or resume them from the table above to score directly against the benchmark workspace.</p>
              </div>
            </InlineAlert>
          ) : null}
        </div>
      </AsyncStateWrapper>
    </ExpertRouteWorkspace>
  );
}
