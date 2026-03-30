'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { AlertTriangle, CheckCircle, Clock, GraduationCap, Inbox, MessageSquare, Settings } from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { DataTable, type Column } from '@/components/ui/data-table';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { ExpertFreshnessBadge, ExpertMetricCard, ExpertPageHeader, ExpertSectionPanel } from '@/components/domain/expert-surface';
import type { CalibrationCase, CalibrationNote } from '@/lib/types/expert';
import { fetchCalibrationCases, fetchCalibrationNotes, isApiError } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type AsyncStatus = 'loading' | 'error' | 'empty' | 'success';

const FILTER_GROUPS: FilterGroup[] = [
  { id: 'profession', label: 'Profession', options: [{ id: 'nursing', label: 'Nursing' }, { id: 'medicine', label: 'Medicine' }, { id: 'dentistry', label: 'Dentistry' }] },
  { id: 'subTest', label: 'Sub-test', options: [{ id: 'writing', label: 'Writing' }, { id: 'speaking', label: 'Speaking' }] },
  { id: 'status', label: 'Status', options: [{ id: 'pending', label: 'Pending' }, { id: 'completed', label: 'Completed' }] },
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
  }, []);

  const filtered = useMemo(() => cases.filter((entry) => {
    if (filters.profession?.length && !filters.profession.includes(entry.profession)) return false;
    if (filters.subTest?.length && !filters.subTest.includes(entry.subTest)) return false;
    if (filters.status?.length && !filters.status.includes(entry.status)) return false;
    return true;
  }), [cases, filters]);

  const completedCases = cases.filter((entry) => entry.status === 'completed');
  const pendingCount = cases.filter((entry) => entry.status === 'pending').length;
  const alignmentPct = completedCases.length > 0
    ? Math.round((completedCases.filter((entry) => Math.abs((entry.benchmarkScore ?? 0) - (entry.reviewerScore ?? 0)) <= 20).length / completedCases.length) * 100)
    : 0;

  const columns: Column<CalibrationCase>[] = [
    { key: 'id', header: 'Case ID', render: (row) => <span className="font-mono text-xs">{row.id}</span> },
    { key: 'title', header: 'Title', render: (row) => row.title },
    { key: 'profession', header: 'Profession', render: (row) => <span className="capitalize">{row.profession}</span> },
    { key: 'type', header: 'Type', render: (row) => <span className="capitalize">{row.type}</span> },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <span className={`inline-flex rounded-full px-2.5 py-1 text-xs font-medium ${row.status === 'completed' ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700'}`}>
          {row.status}
        </span>
      ),
    },
    {
      key: 'alignment',
      header: 'Alignment',
      render: (row) => row.status === 'pending'
        ? <span className="text-xs text-slate-400">Awaiting submission</span>
        : <span className="text-sm font-medium text-slate-700">{Math.abs((row.benchmarkScore ?? 0) - (row.reviewerScore ?? 0)) <= 20 ? 'Aligned' : 'Needs review'}</span>,
    },
    {
      key: 'actions',
      header: 'Action',
      render: (row) => (
        <button
          type="button"
          onClick={() => router.push(`/expert/calibration/${row.id}`)}
          className="text-sm font-medium text-primary hover:underline"
        >
          {row.status === 'pending' ? 'Open workspace' : 'Review benchmark'}
        </button>
      ),
    },
  ];

  return (
    <div className="mx-auto flex max-w-7xl flex-col gap-6 p-4 md:p-8" role="main" aria-label="Calibration center">
      <ExpertPageHeader
        meta="Benchmark Review Ops"
        title="Calibration"
        description="Benchmark cases now open in a dedicated review workspace with exemplar artifacts, rubric rationale, and alignment evidence instead of the old inline scorer."
        actions={<ExpertFreshnessBadge value={notes[0]?.createdAt} />}
      />

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
        <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
          <ExpertMetricCard label="Alignment" value={`${alignmentPct}%`} hint="Completed calibration cases aligned to benchmark." tone={alignmentPct >= 90 ? 'success' : 'warning'} icon={<CheckCircle className="h-5 w-5" />} />
          <ExpertMetricCard label="Pending Cases" value={pendingCount} hint="Benchmark reviews still requiring your submission." tone={pendingCount > 0 ? 'warning' : 'success'} icon={<Clock className="h-5 w-5" />} />
          <ExpertMetricCard label="Total Benchmarks" value={cases.length} hint="Writing and speaking benchmark cases in scope." icon={<GraduationCap className="h-5 w-5" />} />
        </div>

        <ExpertSectionPanel title="Calibration Cases" description="Open a dedicated benchmark workspace to inspect exemplar materials, compare against benchmark logic, and submit criterion-level scores.">
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
        </ExpertSectionPanel>

        <ExpertSectionPanel title="Notes & History" description="Operational notes, completed calibrations, and system-assigned benchmark activity.">
          {notes.length === 0 ? (
            <p className="text-sm italic text-slate-500">No calibration activity recorded yet.</p>
          ) : (
            <ol className="relative ml-2 space-y-4 border-l border-slate-200">
              {notes.map((note) => (
                <li key={note.id} className="ml-4">
                  <span className={`absolute -left-2 h-4 w-4 rounded-full border-2 border-white ${note.type === 'completed' ? 'bg-emerald-400' : note.type === 'comment' ? 'bg-blue-400' : 'bg-slate-300'}`} />
                  <div className="mb-0.5 flex items-center gap-2">
                    {note.type === 'completed' && <CheckCircle className="h-3.5 w-3.5 text-emerald-600" />}
                    {note.type === 'comment' && <MessageSquare className="h-3.5 w-3.5 text-blue-600" />}
                    {note.type === 'system' && <Settings className="h-3.5 w-3.5 text-slate-500" />}
                    <time className="text-xs text-slate-400">{new Date(note.createdAt).toLocaleString()}</time>
                  </div>
                  <div className="flex items-start justify-between gap-3">
                    <p className="text-sm text-slate-800">{note.message}</p>
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
        </ExpertSectionPanel>

        {pendingCount > 0 ? (
          <div className="rounded-2xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800">
            <div className="flex items-start gap-3">
              <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0" />
              <p>{pendingCount} calibration case{pendingCount === 1 ? '' : 's'} still need attention. Open them from the table above to score directly against the benchmark workspace.</p>
            </div>
          </div>
        ) : null}
      </AsyncStateWrapper>
    </div>
  );
}
