'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { CheckCircle2, AlertTriangle, RefreshCw, Save, Sparkles } from 'lucide-react';
import { AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import {
  buildCanonicalListeningSkeleton,
  getListeningStructure,
  proposeListeningStructure,
  replaceListeningStructure,
  validateListeningStructure,
  type ListeningAuthoredQuestion,
  type ListeningDistractorCategory,
  type ListeningPartCode,
  type ListeningQuestionType,
  type ListeningSpeakerAttitude,
  type ListeningValidationCounts,
  type ListeningValidationReport,
  LISTENING_PART_A_COUNT,
  LISTENING_PART_B_COUNT,
  LISTENING_PART_C_COUNT,
  LISTENING_CANONICAL_TOTAL,
} from '@/lib/listening-authoring-api';

const DISTRACTOR_CATEGORY_OPTIONS: { value: ListeningDistractorCategory | ''; label: string }[] = [
  { value: '',                  label: '— not authored —' },
  { value: 'too_strong',        label: 'Too strong (always / definitely)' },
  { value: 'too_weak',          label: 'Too weak (rarely / unlikely)' },
  { value: 'wrong_speaker',     label: 'Wrong speaker held the view' },
  { value: 'opposite_meaning',  label: 'Opposite meaning / negation flip' },
  { value: 'reused_keyword',    label: 'Re-used keyword from the audio' },
];

const SPEAKER_ATTITUDE_OPTIONS: { value: ListeningSpeakerAttitude | ''; label: string }[] = [
  { value: '',           label: '— not authored —' },
  { value: 'concerned',  label: 'Concerned' },
  { value: 'optimistic', label: 'Optimistic' },
  { value: 'doubtful',   label: 'Doubtful' },
  { value: 'critical',   label: 'Critical' },
  { value: 'neutral',    label: 'Neutral' },
  { value: 'other',      label: 'Other' },
];

/**
 * Admin authoring UI for the OET Listening 42-item question map.
 *
 * Mirrors the Reading editor pattern. Until the relational schema lands
 * (Phase 2), this writes into `ContentPaper.ExtractedTextJson.listeningQuestions`
 * — the same JSON document the learner runtime grades against.
 *
 * Canonical shape:
 *   Part A1 = Q1–12   (consultation 1, short-answer / fill-in-the-blank)
 *   Part A2 = Q13–24  (consultation 2, short-answer / fill-in-the-blank)
 *   Part B  = Q25–30  (six workplace extracts, single-select 3-option MCQ)
 *   Part C1 = Q31–36  (presentation 1, single-select 3-option MCQ)
 *   Part C2 = Q37–42  (presentation 2, single-select 3-option MCQ)
 */

type LoadStatus = 'loading' | 'ready' | 'error';

const SECTIONS: { code: ListeningPartCode; label: string; range: [number, number] }[] = [
  { code: 'A1', label: 'Part A · Consultation 1 (Q1–12)', range: [1, 12] },
  { code: 'A2', label: 'Part A · Consultation 2 (Q13–24)', range: [13, 24] },
  { code: 'B', label: 'Part B · Workplace extracts (Q25–30, MCQ)', range: [25, 30] },
  { code: 'C1', label: 'Part C · Presentation 1 (Q31–36, MCQ)', range: [31, 36] },
  { code: 'C2', label: 'Part C · Presentation 2 (Q37–42, MCQ)', range: [37, 42] },
];

export function ListeningStructureEditor({ paperId }: { paperId: string }) {
  const [status, setStatus] = useState<LoadStatus>('loading');
  const [questions, setQuestions] = useState<ListeningAuthoredQuestion[]>([]);
  const [counts, setCounts] = useState<ListeningValidationCounts | null>(null);
  const [report, setReport] = useState<ListeningValidationReport | null>(null);
  const [saving, setSaving] = useState(false);
  const [validating, setValidating] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const doc = await getListeningStructure(paperId);
      setQuestions(doc.questions);
      setCounts(doc.counts);
      setDirty(false);
      setStatus('ready');
    } catch (e) {
      setStatus('error');
      setToast({ variant: 'error', message: `Failed to load structure: ${(e as Error).message}` });
    }
  }, [paperId]);

  useEffect(() => { void load(); }, [load]);

  const liveCounts = useMemo<ListeningValidationCounts>(() => {
    let a = 0, b = 0, c = 0;
    for (const q of questions) {
      const code = q.partCode.toUpperCase();
      if (code.startsWith('A')) a++;
      else if (code.startsWith('B')) b++;
      else if (code.startsWith('C')) c++;
    }
    return { partACount: a, partBCount: b, partCCount: c, totalItems: a + b + c };
  }, [questions]);

  const updateQuestion = (n: number, patch: Partial<ListeningAuthoredQuestion>) => {
    setQuestions((prev) => prev.map((q) => (q.number === n ? { ...q, ...patch } : q)));
    setDirty(true);
  };

  const updateOption = (n: number, idx: number, value: string) => {
    setQuestions((prev) => prev.map((q) => {
      if (q.number !== n) return q;
      const opts = [...(q.options ?? ['', '', ''])];
      while (opts.length < 3) opts.push('');
      opts[idx] = value;
      return { ...q, options: opts.slice(0, 3) };
    }));
    setDirty(true);
  };

  // Phase 4: per-option distractor authoring helpers.
  const updateOptionDistractorWhy = (n: number, idx: number, value: string) => {
    setQuestions((prev) => prev.map((q) => {
      if (q.number !== n) return q;
      const arr = [...(q.optionDistractorWhy ?? [null, null, null])];
      while (arr.length < 3) arr.push(null);
      arr[idx] = value.trim() === '' ? null : value;
      return { ...q, optionDistractorWhy: arr.slice(0, 3) };
    }));
    setDirty(true);
  };
  const updateOptionDistractorCategory = (
    n: number,
    idx: number,
    value: ListeningDistractorCategory | '',
  ) => {
    setQuestions((prev) => prev.map((q) => {
      if (q.number !== n) return q;
      const arr = [...(q.optionDistractorCategory ?? [null, null, null])];
      while (arr.length < 3) arr.push(null);
      arr[idx] = value === '' ? null : value;
      return { ...q, optionDistractorCategory: arr.slice(0, 3) };
    }));
    setDirty(true);
  };

  const bootstrapSkeleton = () => {
    if (questions.length > 0 && !confirm(
      'This will replace the current question list with a blank 42-item OET skeleton. Unsaved edits will be lost. Continue?',
    )) return;
    setQuestions(buildCanonicalListeningSkeleton());
    setDirty(true);
    setReport(null);
  };

  // Phase 8: ask the AI extraction service for a 42-item proposal. Today
  // returns a deterministic stub; a grounded-gateway impl plugs in via DI.
  const proposeWithAi = async () => {
    if (questions.length > 0 && !confirm(
      'This will replace the current question list with the AI-proposed 42-item structure. Unsaved edits will be lost. Continue?',
    )) return;
    try {
      const draft = await proposeListeningStructure(paperId);
      if (draft.status === 'Failed') {
        setToast({ variant: 'error', message: `Extraction failed: ${draft.message}` });
        return;
      }
      setQuestions(draft.questions);
      setDirty(true);
      setReport(null);
      setToast({
        variant: 'success',
        message: draft.isStub
          ? 'Loaded deterministic 24/6/12 placeholder (AI gateway not yet wired).'
          : 'AI-proposed structure loaded.',
      });
    } catch (e) {
      setToast({ variant: 'error', message: `Extraction failed: ${(e as Error).message}` });
    }
  };

  const save = async () => {
    setSaving(true);
    try {
      const doc = await replaceListeningStructure(paperId, questions);
      setQuestions(doc.questions);
      setCounts(doc.counts);
      setDirty(false);
      setToast({ variant: 'success', message: 'Listening structure saved.' });
    } catch (e) {
      const detail = (e as Error & { detail?: { error?: string } }).detail;
      setToast({ variant: 'error', message: detail?.error ?? `Save failed: ${(e as Error).message}` });
    } finally { setSaving(false); }
  };

  const validate = async () => {
    setValidating(true);
    try {
      const r = await validateListeningStructure(paperId);
      setReport(r);
    } catch (e) {
      setToast({ variant: 'error', message: `Validate failed: ${(e as Error).message}` });
    } finally { setValidating(false); }
  };

  if (status === 'loading') {
    return <AdminRoutePanel title="Listening structure (42 items)"><Skeleton className="h-48" /></AdminRoutePanel>;
  }

  return (
    <>
      <AdminRoutePanel
        title="Listening structure (42 items)"
        actions={
          <div className="flex items-center gap-2">
            <Button variant="ghost" size="sm" onClick={() => void load()} disabled={saving || validating}>
              <RefreshCw className="w-4 h-4 mr-1" /> Reload
            </Button>
            <Button variant="ghost" size="sm" onClick={bootstrapSkeleton} disabled={saving}>
              <Sparkles className="w-4 h-4 mr-1" /> Bootstrap blank skeleton
            </Button>
            <Button variant="ghost" size="sm" onClick={() => void proposeWithAi()} disabled={saving}>
              <Sparkles className="w-4 h-4 mr-1" /> Propose with AI
            </Button>
            <Button variant="secondary" size="sm" onClick={() => void validate()} loading={validating}>
              Validate
            </Button>
            <Button variant="primary" size="sm" onClick={() => void save()} loading={saving} disabled={!dirty}>
              <Save className="w-4 h-4 mr-1" /> Save structure
            </Button>
          </div>
        }
      >
        <div className="space-y-6">
          <CountsBar live={liveCounts} server={counts} />

          {report && (
            <ValidationReportPanel report={report} />
          )}

          {questions.length === 0 ? (
            <InlineAlert variant="info">
              No Listening questions authored yet. Click <strong>Bootstrap blank skeleton</strong> to create
              the 42-item OET shape (24 Part A blanks + 6 Part B MCQs + 12 Part C MCQs), then fill each
              item against your sample&apos;s Question Paper + Answer Key + Audio Script.
            </InlineAlert>
          ) : (
            <div className="space-y-8">
              {SECTIONS.map((section) => {
                const items = questions
                  .filter((q) => q.partCode === section.code)
                  .sort((a, b) => a.number - b.number);
                return (
                  <SectionBlock
                    key={section.code}
                    label={section.label}
                    expectedCount={section.range[1] - section.range[0] + 1}
                    items={items}
                    onUpdate={updateQuestion}
                    onUpdateOption={updateOption}
                    onUpdateOptionDistractorWhy={updateOptionDistractorWhy}
                    onUpdateOptionDistractorCategory={updateOptionDistractorCategory}
                  />
                );
              })}
            </div>
          )}
        </div>
      </AdminRoutePanel>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </>
  );
}

// ── Subcomponents ──────────────────────────────────────────────────────────

function CountsPill({
  label, actual, expected,
}: { label: string; actual: number; expected: number }) {
  const ok = actual === expected;
  return (
    <div className={`rounded-lg border px-3 py-2 text-sm ${ok ? 'border-emerald-200 bg-emerald-50' : 'border-amber-200 bg-amber-50'}`}>
      <div className="text-xs text-muted">{label}</div>
      <div className="font-semibold">{actual} / {expected} {ok ? <CheckCircle2 className="inline w-4 h-4 text-emerald-600" /> : <AlertTriangle className="inline w-4 h-4 text-amber-600" />}</div>
    </div>
  );
}

function CountsBar({
  live, server,
}: {
  live: ListeningValidationCounts;
  server: ListeningValidationCounts | null;
}) {
  return (
    <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
      <CountsPill label="Part A" actual={live.partACount} expected={LISTENING_PART_A_COUNT} />
      <CountsPill label="Part B" actual={live.partBCount} expected={LISTENING_PART_B_COUNT} />
      <CountsPill label="Part C" actual={live.partCCount} expected={LISTENING_PART_C_COUNT} />
      <CountsPill label="Total"  actual={live.totalItems} expected={LISTENING_CANONICAL_TOTAL} />
      {server && server.totalItems !== live.totalItems && (
        <div className="md:col-span-4 text-xs text-muted">
          Last saved on server: {server.partACount}/{server.partBCount}/{server.partCCount} = {server.totalItems}.
        </div>
      )}
    </div>
  );
}

function ValidationReportPanel({ report }: { report: ListeningValidationReport }) {
  if (report.isPublishReady && report.issues.length === 0) {
    return (
      <InlineAlert variant="success">
        Listening structure passes the publish gate ({report.counts.totalItems}/{LISTENING_CANONICAL_TOTAL} items).
      </InlineAlert>
    );
  }
  return (
    <div className="space-y-2">
      {report.issues.map((issue, idx) => (
        <InlineAlert key={`${issue.code}-${idx}`} variant={issue.severity === 'error' ? 'warning' : 'info'}>
          <strong className="font-mono text-xs mr-2">{issue.code}</strong> {issue.message}
        </InlineAlert>
      ))}
    </div>
  );
}

function SectionBlock({
  label, expectedCount, items, onUpdate, onUpdateOption,
  onUpdateOptionDistractorWhy, onUpdateOptionDistractorCategory,
}: {
  label: string;
  expectedCount: number;
  items: ListeningAuthoredQuestion[];
  onUpdate: (n: number, patch: Partial<ListeningAuthoredQuestion>) => void;
  onUpdateOption: (n: number, idx: number, value: string) => void;
  onUpdateOptionDistractorWhy: (n: number, idx: number, value: string) => void;
  onUpdateOptionDistractorCategory: (n: number, idx: number, value: ListeningDistractorCategory | '') => void;
}) {
  const ok = items.length === expectedCount;
  return (
    <section className="rounded-xl border border-gray-200 p-4">
      <header className="flex items-center justify-between mb-3">
        <h3 className="font-semibold">{label}</h3>
        <Badge variant={ok ? 'success' : 'warning'}>{items.length}/{expectedCount}</Badge>
      </header>
      {items.length === 0 ? (
        <p className="text-sm text-muted">No items in this section yet.</p>
      ) : (
        <ul className="space-y-4">
          {items.map((q) => (
            <QuestionRow
              key={q.number}
              q={q}
              onUpdate={(patch) => onUpdate(q.number, patch)}
              onUpdateOption={(idx, value) => onUpdateOption(q.number, idx, value)}
              onUpdateOptionDistractorWhy={(idx, value) => onUpdateOptionDistractorWhy(q.number, idx, value)}
              onUpdateOptionDistractorCategory={(idx, value) => onUpdateOptionDistractorCategory(q.number, idx, value)}
            />
          ))}
        </ul>
      )}
    </section>
  );
}

function QuestionRow({
  q, onUpdate, onUpdateOption,
  onUpdateOptionDistractorWhy, onUpdateOptionDistractorCategory,
}: {
  q: ListeningAuthoredQuestion;
  onUpdate: (patch: Partial<ListeningAuthoredQuestion>) => void;
  onUpdateOption: (idx: number, value: string) => void;
  onUpdateOptionDistractorWhy: (idx: number, value: string) => void;
  onUpdateOptionDistractorCategory: (idx: number, value: ListeningDistractorCategory | '') => void;
}) {
  const isMcq = q.type === 'multiple_choice_3';
  const isPartC = q.partCode === 'C1' || q.partCode === 'C2';
  return (
    <li className="rounded-lg bg-gray-50 p-3">
      <div className="flex items-center gap-3 mb-2">
        <Badge variant="info">Q{q.number}</Badge>
        <Select
          value={q.type}
          onChange={(e) => {
            const next = e.target.value as ListeningQuestionType;
            onUpdate({
              type: next,
              options: next === 'multiple_choice_3'
                ? (q.options.length === 3 ? q.options : ['', '', ''])
                : [],
            });
          }}
          options={[
            { value: 'short_answer', label: 'Short answer / fill-in-the-blank' },
            { value: 'multiple_choice_3', label: 'Multiple choice (3 options)' },
          ]}
        />
        <Input
          label="Points"
          type="number"
          value={q.points}
          onChange={(e) => onUpdate({ points: Math.max(1, Number(e.target.value) || 1) })}
        />
      </div>

      <Input
        label={isMcq ? 'Stem (the question wording)' : 'Stem (note label / sentence with the blank)'}
        value={q.stem}
        onChange={(e) => onUpdate({ stem: e.target.value })}
        placeholder={isMcq
          ? 'e.g. "The patient expresses a concern about…"'
          : 'e.g. "developed ___ pain"'}
      />

      {isMcq && (
        <div className="grid grid-cols-1 md:grid-cols-3 gap-2 mt-2">
          {[0, 1, 2].map((i) => (
            <Input
              key={i}
              label={`Option ${String.fromCharCode(65 + i)}`}
              value={q.options[i] ?? ''}
              onChange={(e) => onUpdateOption(i, e.target.value)}
            />
          ))}
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 gap-2 mt-2">
        <Input
          label={isMcq ? 'Correct answer (A/B/C or full text)' : 'Correct answer'}
          value={q.correctAnswer}
          onChange={(e) => onUpdate({ correctAnswer: e.target.value })}
          placeholder={isMcq ? 'A' : 'lower back'}
        />
        <Input
          label="Accepted alternates (comma-separated)"
          value={q.acceptedAnswers.join(', ')}
          onChange={(e) => onUpdate({
            acceptedAnswers: e.target.value
              .split(',')
              .map((s) => s.trim())
              .filter((s) => s.length > 0),
          })}
          placeholder={isMcq ? '' : 'iron-deficiency anaemia, iron-deficiency anemia'}
        />
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-2 mt-2">
        <Input
          label="Transcript excerpt (for review)"
          value={q.transcriptExcerpt ?? ''}
          onChange={(e) => onUpdate({ transcriptExcerpt: e.target.value || null })}
          placeholder="Quote from the audio script that supports this answer"
        />
        <Input
          label="Distractor / explanation"
          value={q.distractorExplanation ?? ''}
          onChange={(e) => onUpdate({ distractorExplanation: e.target.value || null })}
          placeholder="Why the wrong options are tempting"
        />
      </div>

      {/* Phase 5: time-coded transcript evidence (jump-to-evidence in review). */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-2 mt-2">
        <Input
          label="Evidence start (ms in section audio)"
          type="number"
          value={q.transcriptEvidenceStartMs ?? ''}
          onChange={(e) => {
            const v = e.target.value === '' ? null : Math.max(0, Number(e.target.value) || 0);
            onUpdate({ transcriptEvidenceStartMs: v });
          }}
          placeholder="e.g. 42500"
        />
        <Input
          label="Evidence end (ms in section audio)"
          type="number"
          value={q.transcriptEvidenceEndMs ?? ''}
          onChange={(e) => {
            const v = e.target.value === '' ? null : Math.max(0, Number(e.target.value) || 0);
            onUpdate({ transcriptEvidenceEndMs: v });
          }}
          placeholder="e.g. 48000"
        />
      </div>

      {/* Phase 4: per-option distractor analysis (MCQ only). */}
      {isMcq && (
        <div className="mt-3 rounded-lg border border-slate-200 bg-white p-3">
          <p className="text-xs font-medium uppercase tracking-wide text-slate-500 mb-2">
            Per-option distractor analysis
          </p>
          <div className="space-y-2">
            {[0, 1, 2].map((i) => (
              <div key={i} className="grid grid-cols-1 md:grid-cols-2 gap-2">
                <Select
                  label={`Option ${String.fromCharCode(65 + i)} category`}
                  value={q.optionDistractorCategory?.[i] ?? ''}
                  onChange={(e) => onUpdateOptionDistractorCategory(
                    i,
                    e.target.value as ListeningDistractorCategory | '',
                  )}
                  options={DISTRACTOR_CATEGORY_OPTIONS}
                />
                <Input
                  label={`Why ${String.fromCharCode(65 + i)} is wrong`}
                  value={q.optionDistractorWhy?.[i] ?? ''}
                  onChange={(e) => onUpdateOptionDistractorWhy(i, e.target.value)}
                  placeholder="Short explanation surfaced in the post-attempt review"
                />
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Phase 4: speaker attitude (Part C only). */}
      {isPartC && (
        <div className="mt-3">
          <Select
            label="Speaker attitude (Part C)"
            value={q.speakerAttitude ?? ''}
            onChange={(e) => onUpdate({
              speakerAttitude: e.target.value === '' ? null : (e.target.value as ListeningSpeakerAttitude),
            })}
            options={SPEAKER_ATTITUDE_OPTIONS}
          />
        </div>
      )}
    </li>
  );
}
