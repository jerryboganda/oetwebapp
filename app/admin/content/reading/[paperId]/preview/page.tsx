'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, Clock3, Eye, Play, RotateCcw } from 'lucide-react';

import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { ReadingPdfViewer } from '@/components/domain/reading-pdf-viewer';
import {
  getReadingStructureAdminPreview,
  type ReadingLearnerStructureDto,
} from '@/lib/reading-authoring-api';

const OPTION_LABELS = ['A', 'B', 'C', 'D', 'E', 'F'];

type PreviewPartCode = 'A' | 'B' | 'C';
type PreviewTimerWindow = 'A' | 'BC';

function timerWindowForPart(partCode: PreviewPartCode): PreviewTimerWindow {
  return partCode === 'A' ? 'A' : 'BC';
}

function initialTimerSeconds(timerWindow: PreviewTimerWindow): number {
  return timerWindow === 'A' ? 15 * 60 : 45 * 60;
}

function formatTimer(seconds: number): string {
  const minutes = Math.floor(Math.max(0, seconds) / 60);
  const remainder = Math.max(0, seconds) % 60;
  return `${minutes}:${String(remainder).padStart(2, '0')}`;
}

function asOptionList(options: unknown): string[] {
  if (Array.isArray(options)) {
    return options.map((option) => {
      if (typeof option === 'string') return option;
      if (option && typeof option === 'object' && 'label' in option) {
        return String((option as { label?: unknown }).label ?? '');
      }
      return String(option);
    }).filter(Boolean);
  }
  return [];
}

export default function ReadingPreviewAsStudentPage() {
  const params = useParams<{ paperId: string }>();
  const paperId = params?.paperId ?? '';

  const [structure, setStructure] = useState<ReadingLearnerStructureDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [previewStarted, setPreviewStarted] = useState(false);
  const [activePart, setActivePart] = useState<PreviewPartCode>('A');
  const activeTimerWindow = timerWindowForPart(activePart);
  const [remainingSeconds, setRemainingSeconds] = useState(initialTimerSeconds('A'));

  useEffect(() => {
    if (!paperId) return;
    let cancelled = false;
    setLoading(true);
    setError(null);
    getReadingStructureAdminPreview(paperId)
      .then((data) => {
        if (!cancelled) setStructure(data);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Failed to load preview');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, [paperId]);

  useEffect(() => {
    setPreviewStarted(false);
    setRemainingSeconds(initialTimerSeconds(activeTimerWindow));
  }, [activeTimerWindow]);

  useEffect(() => {
    if (!previewStarted || remainingSeconds <= 0) return;
    const timerId = window.setInterval(() => {
      setRemainingSeconds((seconds) => Math.max(0, seconds - 1));
    }, 1000);
    return () => window.clearInterval(timerId);
  }, [previewStarted, remainingSeconds]);

  return (
    <AdminSettingsLayout
      title="Preview as student"
      description="Read-only rendering using the learner endpoint. Correct answers are never exposed here."
      eyebrow="Reading authoring"
      icon={<Eye className="h-5 w-5" />}
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Reading', href: '/admin/content/reading' },
        { label: 'Paper', href: `/admin/content/reading/${paperId}` },
        { label: 'Preview' },
      ]}
      actions={
        <Button asChild variant="ghost" size="sm" startIcon={<ArrowLeft className="h-4 w-4" />}>
          <Link href={`/admin/content/reading/${paperId}`}>Back to paper</Link>
        </Button>
      }
    >
      <div className="space-y-6">
        <InlineAlert variant="info">
          This preview is built from the same learner-safe projection used in the student player. It never exposes correct answers, explanations, or accepted variants.
        </InlineAlert>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {loading ? (
          <div className="space-y-4">
            <Skeleton variant="card" />
            <Skeleton variant="card" />
          </div>
        ) : structure ? (
          <>
            <div>
              <h2 className="text-lg font-semibold text-admin-fg-strong">{structure.paper.title}</h2>
              <p className="text-sm text-admin-fg-muted">{structure.paper.subtestCode}</p>
            </div>

            <SettingsSection
              title="Timed preview console"
              description="Exercise the learner-safe PDF layout with official Reading timing before publishing. This does not create a learner attempt."
            >
              <div className="space-y-4">
                <div className="grid gap-3 md:grid-cols-4">
                  <PreviewTimerTile label="Part A window" value="15:00" detail="Hard lock" />
                  <PreviewTimerTile label="Parts B+C window" value="45:00" detail="Shared timer" />
                  <PreviewTimerTile
                    label="Active timer"
                    value={formatTimer(remainingSeconds)}
                    detail={activePart === 'A' ? 'Part A active' : 'Parts B+C active'}
                  />
                  <PreviewTimerTile
                    label="Question count"
                    value={String(structure.parts.reduce((sum, part) => sum + part.questions.length, 0))}
                    detail="Expected 42"
                  />
                </div>

                <div className="flex flex-wrap items-center gap-2">
                  <Button
                    type="button"
                    variant={previewStarted ? 'secondary' : 'primary'}
                    size="sm"
                    onClick={() => {
                      if (previewStarted) {
                        setPreviewStarted(false);
                        setRemainingSeconds(initialTimerSeconds(activeTimerWindow));
                        return;
                      }
                      setPreviewStarted(true);
                    }}
                    startIcon={previewStarted ? <RotateCcw className="h-4 w-4" /> : <Play className="h-4 w-4" />}
                  >
                    {previewStarted ? 'Reset timed preview' : 'Start timed preview'}
                  </Button>
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    disabled
                  >
                    Read-only PDF mode
                  </Button>
                </div>

                <div className="rounded-admin-lg border border-admin-border bg-admin-bg-subtle p-4">
                  <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">
                        {previewStarted ? 'Preview running' : 'Preview idle'}
                      </p>
                      <p className="text-sm text-admin-fg-default">
                        {previewStarted
                          ? `Active timer ${formatTimer(remainingSeconds)} for ${activePart === 'A' ? 'Part A' : 'Parts B+C'}.`
                          : 'PDF preview shows the uploaded question paper with answer-sheet style entry.'}
                      </p>
                    </div>
                    <div className="inline-flex rounded-admin-lg border border-admin-border bg-admin-bg-surface p-1">
                      {(['A', 'B', 'C'] as const).map((partCode) => (
                        <button
                          key={partCode}
                          type="button"
                          onClick={() => setActivePart(partCode)}
                          className={`rounded-admin-md px-3 py-1.5 text-xs font-semibold transition-colors ${activePart === partCode ? 'bg-[var(--admin-primary)] text-[var(--admin-primary-fg)]' : 'text-admin-fg-muted hover:bg-admin-bg-subtle'}`}
                        >
                          Part {partCode}
                        </button>
                      ))}
                    </div>
                  </div>

                  <PreviewAnswerSheet structure={structure} activePart={activePart} />
                </div>
              </div>
            </SettingsSection>

            {structure.parts.map((part) => (
              <SettingsSection
                key={part.id}
                title={`Part ${part.partCode}`}
                description={`${part.timeLimitMinutes} min - ${part.questions.length} question${part.questions.length !== 1 ? 's' : ''}`}
              >
                <div className="space-y-6">
                  <ReadingPdfViewer
                    paperId={paperId}
                    partCode={part.partCode}
                    assets={structure.paper.questionPaperAssets ?? []}
                    annotations={[]}
                    readOnly
                  />

                  <ol className="space-y-3">
                    {part.questions.map((q) => {
                      const options = asOptionList(q.options);
                      return (
                        <li
                          key={q.id}
                          className="rounded-lg border border-admin-border p-3"
                        >
                          <div className="flex items-start gap-2">
                            <span className="text-xs font-mono text-admin-fg-muted">
                              Q{q.displayOrder}
                            </span>
                            <p className="flex-1 text-sm text-admin-fg-strong">{q.stem}</p>
                            <Badge variant="muted" size="sm">{q.points}pt</Badge>
                          </div>
                          {options.length > 0 && (
                            <ul className="mt-2 space-y-1 pl-6">
                              {options.map((opt, idx) => (
                                <li key={idx} className="text-sm text-admin-fg-default">
                                  <span className="font-medium">{OPTION_LABELS[idx] ?? idx + 1}.</span> {opt}
                                </li>
                              ))}
                            </ul>
                          )}
                        </li>
                      );
                    })}
                  </ol>
                </div>
              </SettingsSection>
            ))}
          </>
        ) : null}
      </div>
    </AdminSettingsLayout>
  );
}

function PreviewTimerTile({ label, value, detail }: { label: string; value: string; detail: string }) {
  return (
    <div className="rounded-admin-lg border border-admin-border bg-admin-bg-surface p-3">
      <p className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">
        <Clock3 className="h-3.5 w-3.5" aria-hidden />
        {label}
      </p>
      <p className="mt-1 text-2xl font-bold text-admin-fg-strong">{value}</p>
      <p className="text-xs text-admin-fg-muted">{detail}</p>
    </div>
  );
}

function PreviewAnswerSheet({ structure, activePart }: { structure: ReadingLearnerStructureDto; activePart: PreviewPartCode }) {
  const part = structure.parts.find((item) => item.partCode === activePart);
  if (!part) return null;

  return (
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
      {part.questions.map((question) => {
        const options = asOptionList(question.options);
        return (
          <label key={question.id} className="rounded-admin-lg border border-admin-border bg-admin-bg-surface p-3 text-sm">
            <span className="mb-2 block text-xs font-semibold text-admin-fg-muted">
              Part {part.partCode} - Q{question.displayOrder}
            </span>
            {options.length > 0 ? (
              <select className="w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-2 py-2 text-sm text-admin-fg-default">
                <option value="">Select answer</option>
                {options.map((option, index) => (
                  <option key={`${question.id}-${index}`} value={OPTION_LABELS[index] ?? String(index + 1)}>
                    {OPTION_LABELS[index] ?? index + 1}. {option}
                  </option>
                ))}
              </select>
            ) : (
              <input
                type="text"
                placeholder="Type answer"
                className="w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-2 py-2 text-sm text-admin-fg-default"
              />
            )}
          </label>
        );
      })}
    </div>
  );
}
