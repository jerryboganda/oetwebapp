'use client';

/**
 * Writing Module V2 — admin Calibration Harness (spec §33).
 *
 * Lets Dr Ahmed (or an admin) curate a set of calibration letters with
 * reference grades, then run the V2 grading pipeline against every row
 * and inspect AI-vs-reference agreement.
 *
 * §33 release-gate: ≥90% of letters must score within ±2 raw points of
 * Dr Ahmed's grade.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Beaker, Plus, Play, RefreshCcw, AlertCircle, CheckCircle2 } from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { apiClient } from '@/lib/api';

interface CalibrationGradeDto {
  c1: number;
  c2: number;
  c3: number;
  c4: number;
  c5: number;
  c6: number;
  rawTotal: number;
  bandLabel: string;
  notes: string | null;
}

interface CalibrationLetterDto {
  id: string;
  scenarioId: string;
  letterContent: string;
  authorTier: 'exemplar' | 'learner' | 'synthetic';
  drAhmedGrade: CalibrationGradeDto;
  addedAt: string;
  addedById: string;
}

interface CalibrationResultDto {
  id: string;
  calibrationLetterId: string;
  reference: CalibrationGradeDto;
  ai: CalibrationGradeDto | null;
  absErrorRaw: number;
  bandMatch: boolean;
  aiError: string | null;
}

interface CalibrationRunDto {
  id: string;
  runDate: string;
  modelVersion: string;
  totalLetters: number;
  within2PointsCount: number;
  meanAbsError: number;
  bandAgreementCount: number;
  notesMarkdown: string;
  results: CalibrationResultDto[];
}

interface NewLetterForm {
  scenarioId: string;
  letterContent: string;
  authorTier: 'exemplar' | 'learner' | 'synthetic';
  c1: number;
  c2: number;
  c3: number;
  c4: number;
  c5: number;
  c6: number;
  bandLabel: string;
  notes: string;
}

const EMPTY_FORM: NewLetterForm = {
  scenarioId: '',
  letterContent: '',
  authorTier: 'learner',
  c1: 0,
  c2: 0,
  c3: 0,
  c4: 0,
  c5: 0,
  c6: 0,
  bandLabel: 'B',
  notes: '',
};

function rawTotal(g: { c1: number; c2: number; c3: number; c4: number; c5: number; c6: number }): number {
  return g.c1 + g.c2 + g.c3 + g.c4 + g.c5 + g.c6;
}

export default function AdminWritingCalibrationPage() {
  const [letters, setLetters] = useState<CalibrationLetterDto[]>([]);
  const [latestRun, setLatestRun] = useState<CalibrationRunDto | null>(null);
  const [form, setForm] = useState<NewLetterForm>(EMPTY_FORM);
  const [showForm, setShowForm] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<'load' | 'add' | 'run' | null>(null);

  const load = useCallback(async () => {
    setBusy('load');
    setError(null);
    try {
      const [ls, run] = await Promise.all([
        apiClient.get<CalibrationLetterDto[]>('/v1/admin/writing/calibration/letters'),
        apiClient.get<CalibrationRunDto | undefined>('/v1/admin/writing/calibration/runs/latest'),
      ]);
      setLetters(ls);
      setLatestRun(run ?? null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load calibration data.');
    } finally {
      setBusy(null);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const onAdd = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy('add');
    setError(null);
    try {
      const grade: CalibrationGradeDto = {
        c1: form.c1,
        c2: form.c2,
        c3: form.c3,
        c4: form.c4,
        c5: form.c5,
        c6: form.c6,
        rawTotal: rawTotal(form),
        bandLabel: form.bandLabel,
        notes: form.notes.trim().length === 0 ? null : form.notes,
      };
      await apiClient.post<CalibrationLetterDto>('/v1/admin/writing/calibration/letters', {
        scenarioId: form.scenarioId,
        letterContent: form.letterContent,
        authorTier: form.authorTier,
        drAhmedGrade: grade,
      });
      setForm(EMPTY_FORM);
      setShowForm(false);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not add the calibration letter.');
    } finally {
      setBusy(null);
    }
  };

  const onRun = async () => {
    if (typeof window !== 'undefined' && !window.confirm(`Run calibration against all ${letters.length} letters? This calls the AI gateway once per letter.`)) {
      return;
    }
    setBusy('run');
    setError(null);
    try {
      await apiClient.post('/v1/admin/writing/calibration/run', {});
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Run failed.');
    } finally {
      setBusy(null);
    }
  };

  const within2Pct = useMemo(() => {
    if (!latestRun || latestRun.totalLetters === 0) return 0;
    return Math.round((latestRun.within2PointsCount / latestRun.totalLetters) * 100);
  }, [latestRun]);
  const gatePassed = within2Pct >= 90;

  return (
    <div className="space-y-6 p-6">
      <header className="flex items-start justify-between gap-4">
        <div className="flex items-start gap-3">
          <span className="rounded-full bg-violet-100 p-3 text-violet-700">
            <Beaker className="h-6 w-6" aria-hidden="true" />
          </span>
          <div>
            <h1 className="text-2xl font-bold">Writing calibration harness</h1>
            <p className="text-sm text-muted">
              50-letter corpus with Dr Ahmed&apos;s reference grades. Release gate (spec §33):
              ≥90% within ±2 raw points.
            </p>
          </div>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" onClick={load} disabled={busy === 'load'}>
            <RefreshCcw className="mr-1 h-4 w-4" aria-hidden="true" /> Reload
          </Button>
          <Button onClick={() => setShowForm((s) => !s)}>
            <Plus className="mr-1 h-4 w-4" aria-hidden="true" /> {showForm ? 'Cancel' : 'Add letter'}
          </Button>
          <Button onClick={onRun} disabled={busy === 'run' || letters.length === 0}>
            <Play className="mr-1 h-4 w-4" aria-hidden="true" /> {busy === 'run' ? 'Running…' : 'Run calibration'}
          </Button>
        </div>
      </header>

      {error ? (
        <div className="rounded-2xl border border-red-300 bg-red-50 p-3 text-sm text-red-800">
          {error}
        </div>
      ) : null}

      {/* Add form */}
      {showForm ? (
        <Card>
          <CardContent className="p-6">
            <form onSubmit={onAdd} className="grid gap-3 md:grid-cols-2">
              <label className="block text-xs font-bold uppercase tracking-wider text-muted">
                Scenario ID (GUID)
                <input
                  required
                  value={form.scenarioId}
                  onChange={(e) => setForm({ ...form, scenarioId: e.target.value })}
                  className="mt-1 block w-full rounded border border-border bg-background p-2 text-sm"
                />
              </label>
              <label className="block text-xs font-bold uppercase tracking-wider text-muted">
                Author tier
                <select
                  value={form.authorTier}
                  onChange={(e) => setForm({ ...form, authorTier: e.target.value as NewLetterForm['authorTier'] })}
                  className="mt-1 block w-full rounded border border-border bg-background p-2 text-sm"
                >
                  <option value="exemplar">Exemplar (A-band sample)</option>
                  <option value="learner">Learner (real submission)</option>
                  <option value="synthetic">Synthetic (test data)</option>
                </select>
              </label>
              <label className="block text-xs font-bold uppercase tracking-wider text-muted md:col-span-2">
                Letter content
                <textarea
                  required
                  rows={8}
                  value={form.letterContent}
                  onChange={(e) => setForm({ ...form, letterContent: e.target.value })}
                  className="mt-1 block w-full rounded border border-border bg-background p-2 text-sm font-mono"
                />
              </label>

              <fieldset className="md:col-span-2 grid grid-cols-3 gap-3 md:grid-cols-6">
                <legend className="col-span-full text-xs font-bold uppercase tracking-wider text-muted">
                  Dr Ahmed&apos;s grade (raw total {rawTotal(form)}/38)
                </legend>
                {(
                  [
                    ['c1', 3, 'C1 Purpose'],
                    ['c2', 7, 'C2 Content'],
                    ['c3', 7, 'C3 Conciseness'],
                    ['c4', 7, 'C4 Genre'],
                    ['c5', 7, 'C5 Organisation'],
                    ['c6', 7, 'C6 Language'],
                  ] as Array<[keyof NewLetterForm, number, string]>
                ).map(([key, max, label]) => (
                  <label key={String(key)} className="block text-[10px] font-bold uppercase tracking-wider text-muted">
                    {label}
                    <input
                      type="number"
                      min={0}
                      max={max}
                      value={String(form[key])}
                      onChange={(e) => setForm({ ...form, [key]: Math.max(0, Math.min(max, Number.parseInt(e.target.value, 10) || 0)) })}
                      className="mt-1 block w-full rounded border border-border bg-background p-2 text-sm"
                    />
                  </label>
                ))}
              </fieldset>

              <label className="block text-xs font-bold uppercase tracking-wider text-muted">
                Band label
                <select
                  value={form.bandLabel}
                  onChange={(e) => setForm({ ...form, bandLabel: e.target.value })}
                  className="mt-1 block w-full rounded border border-border bg-background p-2 text-sm"
                >
                  {['A', 'B+', 'B', 'C+', 'C', 'D', 'E'].map((b) => <option key={b} value={b}>{b}</option>)}
                </select>
              </label>
              <label className="block text-xs font-bold uppercase tracking-wider text-muted md:col-span-2">
                Notes
                <textarea
                  rows={2}
                  value={form.notes}
                  onChange={(e) => setForm({ ...form, notes: e.target.value })}
                  className="mt-1 block w-full rounded border border-border bg-background p-2 text-sm"
                />
              </label>

              <div className="md:col-span-2">
                <Button type="submit" disabled={busy === 'add'}>
                  {busy === 'add' ? 'Saving…' : 'Save letter'}
                </Button>
              </div>
            </form>
          </CardContent>
        </Card>
      ) : null}

      {/* Letter corpus */}
      <Card>
        <CardContent className="p-6">
          <header className="mb-3 flex items-center justify-between">
            <h2 className="text-lg font-bold">Calibration corpus</h2>
            <span className="text-xs text-muted">{letters.length} letters (target: 50)</span>
          </header>
          {letters.length === 0 ? (
            <p className="text-sm text-muted">No calibration letters yet.</p>
          ) : (
            <ul className="divide-y divide-border" aria-label="Calibration letters">
              {letters.map((l) => (
                <li key={l.id} className="flex items-start justify-between gap-3 py-2 text-sm">
                  <div className="min-w-0">
                    <p className="font-mono text-xs text-muted">{l.id.slice(0, 8)}… · scenario {l.scenarioId.slice(0, 8)}…</p>
                    <p className="line-clamp-2 text-navy">{l.letterContent.slice(0, 160)}…</p>
                  </div>
                  <div className="flex shrink-0 items-center gap-2">
                    <Badge>{l.authorTier}</Badge>
                    <Badge>{l.drAhmedGrade.bandLabel} · {l.drAhmedGrade.rawTotal}/38</Badge>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

      {/* Latest run report */}
      <Card>
        <CardContent className="p-6">
          <header className="mb-3 flex items-center justify-between">
            <h2 className="text-lg font-bold">Latest run</h2>
            {latestRun ? (
              <span className={`inline-flex items-center gap-2 rounded-full px-3 py-1 text-xs font-bold ${gatePassed ? 'bg-emerald-100 text-emerald-800' : 'bg-amber-100 text-amber-800'}`}>
                {gatePassed ? <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" /> : <AlertCircle className="h-3.5 w-3.5" aria-hidden="true" />}
                Gate: {within2Pct}% within ±2 raw
              </span>
            ) : null}
          </header>
          {!latestRun ? (
            <p className="text-sm text-muted">No runs yet — add letters and click &ldquo;Run calibration&rdquo;.</p>
          ) : (
            <div className="space-y-4">
              <dl className="grid grid-cols-2 gap-3 text-sm md:grid-cols-4">
                <div>
                  <dt className="text-xs uppercase tracking-wider text-muted">Run date</dt>
                  <dd className="font-bold">{new Date(latestRun.runDate).toLocaleString()}</dd>
                </div>
                <div>
                  <dt className="text-xs uppercase tracking-wider text-muted">Model</dt>
                  <dd className="font-mono text-xs">{latestRun.modelVersion}</dd>
                </div>
                <div>
                  <dt className="text-xs uppercase tracking-wider text-muted">Mean abs error</dt>
                  <dd className="font-bold">{latestRun.meanAbsError.toFixed(2)}</dd>
                </div>
                <div>
                  <dt className="text-xs uppercase tracking-wider text-muted">Band agreement</dt>
                  <dd className="font-bold">{latestRun.bandAgreementCount}/{latestRun.totalLetters}</dd>
                </div>
              </dl>

              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="bg-surface text-xs uppercase tracking-wider text-muted">
                    <tr>
                      <th className="px-2 py-1 text-left">Letter</th>
                      <th className="px-2 py-1 text-left">Reference</th>
                      <th className="px-2 py-1 text-left">AI</th>
                      <th className="px-2 py-1 text-right">|Δ|</th>
                      <th className="px-2 py-1 text-right">Band</th>
                    </tr>
                  </thead>
                  <tbody>
                    {latestRun.results.map((r) => (
                      <tr key={r.id} className={`border-t border-border ${r.absErrorRaw > 2 ? 'bg-amber-50' : ''}`}>
                        <td className="px-2 py-1 font-mono text-xs">{r.calibrationLetterId.slice(0, 8)}…</td>
                        <td className="px-2 py-1">{r.reference.bandLabel} · {r.reference.rawTotal}</td>
                        <td className="px-2 py-1">
                          {r.ai ? `${r.ai.bandLabel} · ${r.ai.rawTotal}` : <span className="text-red-700">{r.aiError ?? 'unavailable'}</span>}
                        </td>
                        <td className={`px-2 py-1 text-right font-bold ${r.absErrorRaw > 2 ? 'text-amber-700' : 'text-emerald-700'}`}>{r.absErrorRaw}</td>
                        <td className="px-2 py-1 text-right">{r.bandMatch ? '✓' : '·'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {latestRun.notesMarkdown ? (
                <pre className="whitespace-pre-wrap rounded-xl border border-border bg-surface p-3 text-xs font-mono text-navy">
                  {latestRun.notesMarkdown}
                </pre>
              ) : null}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
