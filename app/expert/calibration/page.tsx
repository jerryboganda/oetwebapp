'use client';

import { useState, useEffect, useCallback } from 'react';
import { Card, CardContent } from '@/components/ui/card';
import { DataTable, Column } from '@/components/ui/data-table';
import { FilterBar, FilterGroup } from '@/components/ui/filter-bar';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Select, Textarea } from '@/components/ui/form-controls';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import type { CalibrationCase, CalibrationNote } from '@/lib/types/expert';
import { CheckCircle, AlertTriangle, Inbox, Clock, MessageSquare, Settings, X } from 'lucide-react';
import { fetchCalibrationCases, fetchCalibrationNotes, submitCalibrationCase } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type AsyncStatus = 'loading' | 'error' | 'empty' | 'success';

const FILTER_GROUPS: FilterGroup[] = [
  { id: 'profession', label: 'Profession', options: [{ id: 'nursing', label: 'Nursing' }, { id: 'medicine', label: 'Medicine' }, { id: 'dentistry', label: 'Dentistry' }] },
  { id: 'subTest', label: 'Sub-test', options: [{ id: 'writing', label: 'Writing' }, { id: 'speaking', label: 'Speaking' }] },
  { id: 'status', label: 'Status', options: [{ id: 'pending', label: 'Pending' }, { id: 'completed', label: 'Completed' }] },
];

export default function CalibrationCenterPage() {
  const [cases, setCases] = useState<CalibrationCase[]>([]);
  const [notes, setNotes] = useState<CalibrationNote[]>([]);
  const [pageStatus, setPageStatus] = useState<AsyncStatus>('loading');
  const [filters, setFilters] = useState<Record<string, string[]>>({});
  const [activeCase, setActiveCase] = useState<CalibrationCase | null>(null);
  const [calibrationScores, setCalibrationScores] = useState<Record<string, string>>({});
  const [calibrationNotes, setCalibrationNotes] = useState('');
  const [isSubmittingCalibration, setIsSubmittingCalibration] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const loadData = useCallback(async () => {
    try {
      setPageStatus('loading');
      const [data, notesData] = await Promise.all([fetchCalibrationCases(), fetchCalibrationNotes()]);
      setCases(data);
      setNotes(notesData);
      setPageStatus(data.length === 0 ? 'empty' : 'success');
      analytics.track('calibration_viewed', { count: data.length });
    } catch {
      setPageStatus('error');
    }
  }, []);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const handleStartCase = (c: CalibrationCase) => {
    setActiveCase(c);
    setCalibrationScores({});
    setCalibrationNotes('');
    analytics.track('calibration_case_started', { caseId: c.id });
  };

  const handleSubmitCalibration = async () => {
    if (!activeCase) return;
    const numScores: Record<string, number> = {};
    Object.entries(calibrationScores).forEach(([k, v]) => { if (v) numScores[k] = parseInt(v); });
    if (Object.keys(numScores).length === 0) {
      setToast({ variant: 'error', message: 'Please provide at least one score.' });
      return;
    }
    setIsSubmittingCalibration(true);
    try {
      await submitCalibrationCase(activeCase.id, { scores: numScores, notes: calibrationNotes || undefined });
      analytics.track('calibration_case_completed', { caseId: activeCase.id });
      setToast({ variant: 'success', message: 'Calibration case submitted!' });
      setActiveCase(null);
      void loadData();
    } catch {
      setToast({ variant: 'error', message: 'Failed to submit calibration. Please try again.' });
    } finally {
      setIsSubmittingCalibration(false);
    }
  };

  // Derived KPIs
  const completedCases = cases.filter(c => c.status === 'completed');
  const pendingCount = cases.filter(c => c.status === 'pending').length;
  const alignedCount = completedCases.filter(c => Math.abs((c.benchmarkScore || 0) - (c.reviewerScore || 0)) <= 20).length;
  const disagreementCount = completedCases.length - alignedCount;
  const alignmentPct = completedCases.length > 0 ? Math.round((alignedCount / completedCases.length) * 100) : 0;

  // Filtered data
  const filtered = cases.filter(c => {
    if (filters.profession?.length && !filters.profession.includes(c.profession)) return false;
    if (filters.subTest?.length && !filters.subTest.includes(c.subTest)) return false;
    if (filters.status?.length && !filters.status.includes(c.status)) return false;
    return true;
  });

  const columns: Column<CalibrationCase>[] = [
    { key: 'id', header: 'Case ID', render: (row) => <span className="font-mono text-xs">{row.id}</span> },
    { key: 'title', header: 'Title', render: (row) => row.title },
    { key: 'profession', header: 'Profession', render: (row) => <span className="capitalize">{row.profession}</span> },
    { key: 'type', header: 'Type', render: (row) => <span className="capitalize">{row.type}</span> },
    {
      key: 'status',
      header: 'Status',
      render: (row) => row.status === 'completed'
        ? <Badge variant="success">Completed</Badge>
        : <Badge variant="default">Pending</Badge>,
    },
    {
      key: 'alignment',
      header: 'Alignment',
      render: (row) => {
        if (row.status === 'pending') return <span className="text-muted">-</span>;
        const diff = Math.abs((row.benchmarkScore || 0) - (row.reviewerScore || 0));
        if (diff <= 20) return <div className="flex items-center gap-1 text-emerald-600"><CheckCircle className="w-4 h-4" /> Aligned</div>;
        return <div className="flex items-center gap-1 text-amber-600"><AlertTriangle className="w-4 h-4" /> Review Needed</div>;
      },
    },
    {
      key: 'actions',
      header: 'Action',
      render: (row) => (
        <Button
          size="sm"
          variant={row.status === 'pending' ? 'primary' : 'outline'}
          onClick={() => handleStartCase(row)}
          aria-label={`${row.status === 'pending' ? 'Start' : 'Review'} case ${row.id}`}
        >
          {row.status === 'pending' ? 'Start' : 'Review'}
        </Button>
      ),
    },
  ];

  const SCORE_OPTIONS = [
    { value: '100', label: '100 — Perfect Match' },
    { value: '80', label: '80 — Close Match' },
    { value: '60', label: '60 — Partial Match' },
    { value: '40', label: '40 — Significant Gaps' },
    { value: '20', label: '20 — Major Disagreement' },
    { value: '0', label: '0 — Complete Mismatch' },
  ];

  return (
    <div className="max-w-7xl mx-auto p-4 md:p-8 space-y-6" role="main" aria-label="Calibration Center">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-navy">Calibration Center</h1>
          <p className="text-muted text-sm mt-1">Review benchmark cases to ensure scoring alignment.</p>
        </div>
      </div>

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => void loadData()}
        emptyContent={<EmptyState icon={<Inbox className="w-12 h-12 text-muted" />} title="No Calibration Cases" description="There are no calibration cases available at the moment." />}
      >
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
          <Card>
            <CardContent className="p-4 flex items-center justify-between">
              <div>
                <p className="text-sm text-muted font-semibold">Overall Alignment</p>
                <p className="text-2xl font-bold text-emerald-600 mt-1">{alignmentPct}%</p>
              </div>
              <CheckCircle className="w-8 h-8 text-emerald-100" />
            </CardContent>
          </Card>
          <Card>
            <CardContent className="p-4 flex items-center justify-between">
              <div>
                <p className="text-sm text-muted font-semibold">Pending Cases</p>
                <p className="text-2xl font-bold text-navy mt-1">{pendingCount}</p>
              </div>
            </CardContent>
          </Card>
          <Card>
            <CardContent className="p-4 flex items-center justify-between">
              <div>
                <p className="text-sm text-muted font-semibold">Disagreements</p>
                <p className="text-2xl font-bold text-amber-600 mt-1">{disagreementCount}</p>
              </div>
              <AlertTriangle className="w-8 h-8 text-amber-100" />
            </CardContent>
          </Card>
        </div>

        <FilterBar groups={FILTER_GROUPS} selected={filters} onChange={(groupId, optionId) => {
          setFilters(prev => {
            const current = prev[groupId] ?? [];
            return { ...prev, [groupId]: current.includes(optionId) ? current.filter(x => x !== optionId) : [...current, optionId] };
          });
        }} onClear={() => setFilters({})} />

        <Card>
          <CardContent className="p-0">
            <DataTable
              data={filtered}
              columns={columns}
              keyExtractor={(item) => item.id}
              emptyMessage="No cases match filters."
              aria-label="Calibration cases table"
            />
          </CardContent>
        </Card>

        {/* Inline Calibration Scoring Panel */}
        {activeCase && (
          <Card className="border-primary/30 ring-1 ring-primary/20">
            <div className="p-4 border-b border-gray-200 flex items-center justify-between bg-lavender/20">
              <div>
                <h3 className="font-semibold text-navy">Scoring: {activeCase.title}</h3>
                <p className="text-xs text-muted mt-0.5">
                  {activeCase.type} — {activeCase.profession} | Benchmark: {activeCase.benchmarkScore}
                </p>
              </div>
              <button onClick={() => setActiveCase(null)} className="text-muted hover:text-navy p-1" aria-label="Close scoring panel">
                <X className="w-5 h-5" />
              </button>
            </div>
            <CardContent className="p-4 space-y-4">
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <Select
                  label="Overall Score"
                  options={SCORE_OPTIONS}
                  value={calibrationScores['overall'] ?? ''}
                  onChange={(e) => setCalibrationScores(prev => ({ ...prev, overall: e.target.value }))}
                  placeholder="Select your score…"
                  aria-label="Calibration overall score"
                />
                <Select
                  label="Content Accuracy"
                  options={SCORE_OPTIONS}
                  value={calibrationScores['content'] ?? ''}
                  onChange={(e) => setCalibrationScores(prev => ({ ...prev, content: e.target.value }))}
                  placeholder="Select score…"
                  aria-label="Calibration content accuracy score"
                />
              </div>
              <Textarea
                label="Notes (optional)"
                placeholder="Any observations about this case…"
                value={calibrationNotes}
                onChange={(e) => setCalibrationNotes(e.target.value)}
                rows={3}
                aria-label="Calibration notes"
              />
              <div className="flex justify-end gap-2">
                <Button variant="outline" onClick={() => setActiveCase(null)}>Cancel</Button>
                <Button onClick={handleSubmitCalibration} disabled={isSubmittingCalibration}>
                  {isSubmittingCalibration ? 'Submitting…' : 'Submit Scores'}
                </Button>
              </div>
            </CardContent>
          </Card>
        )}

        {/* Notes & History — §4.8 / §6.5.4 */}
        <Card>
          <div className="p-4 border-b border-gray-200 flex items-center gap-2">
            <Clock className="w-4 h-4 text-muted" />
            <h3 className="font-semibold text-navy">Notes &amp; History</h3>
          </div>
          <CardContent className="p-4">
            {notes.length === 0 ? (
              <p className="text-sm text-muted italic">No calibration activity recorded yet.</p>
            ) : (
              <ol className="relative border-l border-gray-200 ml-2 space-y-4" aria-label="Calibration activity timeline">
                {notes.map(note => (
                  <li key={note.id} className="ml-4">
                    <span className={`absolute -left-2 w-4 h-4 rounded-full border-2 border-white ${note.type === 'completed' ? 'bg-emerald-400' : note.type === 'comment' ? 'bg-blue-400' : 'bg-gray-300'}`} />
                    <div className="flex items-center gap-2 mb-0.5">
                      {note.type === 'completed' && <CheckCircle className="w-3.5 h-3.5 text-emerald-600" />}
                      {note.type === 'comment' && <MessageSquare className="w-3.5 h-3.5 text-blue-600" />}
                      {note.type === 'system' && <Settings className="w-3.5 h-3.5 text-gray-500" />}
                      <time className="text-xs text-muted">{new Date(note.createdAt).toLocaleDateString()} {new Date(note.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</time>
                    </div>
                    <p className="text-sm text-navy">{note.message}</p>
                  </li>
                ))}
              </ol>
            )}
          </CardContent>
        </Card>
      </AsyncStateWrapper>
    </div>
  );
}
