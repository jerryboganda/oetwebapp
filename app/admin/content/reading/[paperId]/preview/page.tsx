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
import { readingPublicDisplayNumber } from '@/lib/reading-display-number';

const OPTION_LABELS = ['A', 'B', 'C', 'D', 'E', 'F'];

type PreviewSectionCode = 'B1' | 'B2' | 'B3' | 'B4' | 'B5' | 'B6' | 'C1' | 'C2';

const SECTION_LABELS: Record<PreviewSectionCode, string> = {
  B1: 'B1',
  B2: 'B2',
  B3: 'B3',
  B4: 'B4',
  B5: 'B5',
  B6: 'B6',
  C1: 'C1',
  C2: 'C2',
};

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

function getSectionCodeForQuestion(partCode: PreviewPartCode, displayOrder: number): PreviewSectionCode | null {
  if (partCode === 'B') {
    return (`B${Math.min(6, Math.max(1, displayOrder))}` as PreviewSectionCode);
  }
  if (partCode === 'C') {
    return displayOrder <= 8 ? 'C1' : 'C2';
  }
  return null;
}

function getSectionsForPart(part: ReadingLearnerStructureDto['parts'][number]) {
  if (part.partCode === 'A') {
    return [] as Array<{ code: PreviewSectionCode; label: string; questionCount: number; questions: ReadingLearnerStructureDto['parts'][number]['questions'] }>;
  }

  if (part.sections && part.sections.length > 0) {
    return part.sections.map((section) => ({
      code: section.sectionCode,
      label: SECTION_LABELS[section.sectionCode],
      questionCount: section.questions.length,
      questions: section.questions,
    }));
  }

  const sectionOrder: PreviewSectionCode[] = part.partCode === 'B'
    ? ['B1', 'B2', 'B3', 'B4', 'B5', 'B6']
    : ['C1', 'C2'];

  return sectionOrder.map((code) => ({
    code,
    label: SECTION_LABELS[code],
    questionCount: part.questions.filter((question) => getSectionCodeForQuestion(part.partCode, question.displayOrder) === code).length,
    questions: part.questions.filter((question) => getSectionCodeForQuestion(part.partCode, question.displayOrder) === code),
  }));
}

export default function ReadingPreviewAsStudentPage() {
  const params = useParams<{ paperId: string }>();
  const paperId = params?.paperId ?? '';

  const [structure, setStructure] = useState<ReadingLearnerStructureDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [previewStarted, setPreviewStarted] = useState(false);
  const [activePart, setActivePart] = useState<PreviewPartCode>('A');
  const [activeSection, setActiveSection] = useState<PreviewSectionCode | null>(null);
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

  const activePartStructure = structure?.parts.find((part) => part.partCode === activePart) ?? null;
  const activePartSections = activePartStructure ? getSectionsForPart(activePartStructure) : [];

  useEffect(() => {
    if (!activePartStructure) {
      setActiveSection(null);
      return;
    }
    if (activePartStructure.partCode === 'A') {
      setActiveSection(null);
      return;
    }
    const firstSection = activePartSections[0]?.code ?? null;
    setActiveSection((current) => (activePartSections.some((section) => section.code === current) ? current : firstSection));
  }, [activePartSections, activePartStructure]);

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

                  {activePartStructure && activePartSections.length > 0 ? (
                    <div className="flex flex-wrap gap-2">
                      {activePartSections.map((section) => (
                        <button
                          key={section.code}
                          type="button"
                          onClick={() => setActiveSection(section.code)}
                          className={`rounded-admin-md border px-3 py-1.5 text-xs font-semibold transition-colors ${activeSection === section.code ? 'border-transparent bg-[var(--admin-primary)] text-[var(--admin-primary-fg)]' : 'border-admin-border text-admin-fg-muted hover:bg-admin-bg-subtle'}`}
                        >
                          {section.label}
                          {section.questionCount > 0 ? ` (${section.questionCount})` : ''}
                        </button>
                      ))}
                    </div>
                  ) : null}

                  <PreviewAnswerSheet part={activePartStructure} activeSection={activeSection} />
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
                              Q{readingPublicDisplayNumber(part.partCode, q.displayOrder)}
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

function PreviewAnswerSheet({ part, activeSection }: { part: ReadingLearnerStructureDto['parts'][number] | null; activeSection: PreviewSectionCode | null }) {
  if (!part) return null;

  const sections = getSectionsForPart(part);
  const selectedSection = part.partCode === 'A'
    ? null
    : sections.find((section) => section.code === activeSection) ?? sections[0] ?? null;
  const questions = selectedSection?.questions ?? part.questions;

  return (
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
      {questions.map((question) => {
        const options = asOptionList(question.options);
        return (
          <label key={question.id} className="rounded-admin-lg border border-admin-border bg-admin-bg-surface p-3 text-sm">
            <span className="mb-2 block text-xs font-semibold text-admin-fg-muted">
              Part {part.partCode}{selectedSection ? ` ${selectedSection.label}` : ''} - Q{readingPublicDisplayNumber(part.partCode, question.displayOrder)}
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
