'use client';

// Listening V2 — pre-start intro card. Renders mode-specific guidance,
// extract metadata, optional printable booklet preview, and the
// readiness gate + Start CTA. Extracted from the monolithic
// `app/listening/player/[id]/page.tsx` so the surface can be Storybook'd
// and tested in isolation without booting the full Suspense + FSM tree.

import Link from 'next/link';
import { motion, useReducedMotion } from 'motion/react';
import { AlertCircle, CheckCircle2, FileText, Loader2, Lock, Play, Timer, Volume2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { getSurfaceMotion, prefersReducedMotion } from '@/lib/motion';
import { TechReadinessCheck } from '@/components/domain/listening/TechReadinessCheck';
import type { ListeningSessionDto } from '@/lib/listening-api';

export interface ListeningIntroCardProps {
  session: ListeningSessionDto;
  isExam: boolean;
  drillId: string | null;
  strictReadinessRequired: boolean;
  techReadiness: { audioOk: boolean; durationMs: number } | null;
  isStarting: boolean;
  audioError: string | null;
  startError: string | null;
  onTechReadinessReady: (result: { audioOk: boolean; durationMs: number }) => void;
  onStart: () => void;
}

function formatTime(seconds: number) {
  if (!seconds || Number.isNaN(seconds)) return '00:00';
  const minutes = Math.floor(seconds / 60);
  const secs = Math.floor(seconds % 60);
  return `${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
}

function formatMilliseconds(value: number | null | undefined) {
  if (value == null) return null;
  return formatTime(Math.floor(value / 1000));
}

function describeSpeakers(extract: NonNullable<ListeningSessionDto['paper']['extracts']>[number]) {
  if (!extract.speakers?.length) return 'Speakers not specified';
  return extract.speakers
    .map((s) => [s.role, s.accent ?? extract.accentCode].filter(Boolean).join(' · '))
    .join(', ');
}

export function ListeningIntroCard(props: ListeningIntroCardProps) {
  const {
    session,
    isExam,
    drillId,
    strictReadinessRequired,
    techReadiness,
    isStarting,
    audioError,
    startError,
    onTechReadinessReady,
    onStart,
  } = props;
  const reduced = prefersReducedMotion(useReducedMotion());
  const sectionMotion = getSurfaceMotion('section', reduced);
  const extracts = session.paper.extracts ?? [];

  const modeLabel =
    session.modePolicy.mode === 'home'
      ? 'OET@Home Mode'
      : session.modePolicy.mode === 'paper'
        ? 'Paper-Simulation Mode'
        : isExam
          ? 'Exam Mode'
          : 'Practice Mode';

  return (
    <motion.div
      {...sectionMotion}
      className="mt-8 rounded-2xl border border-border bg-surface p-8 text-center shadow-sm sm:p-12"
      data-testid="listening-intro-card"
    >
      <div className="mx-auto mb-6 flex h-20 w-20 items-center justify-center rounded-full bg-primary/10">
        <Volume2 className="h-10 w-10 text-primary" />
      </div>
      <p className="mb-2 text-xs font-black uppercase tracking-widest text-muted">{modeLabel}</p>
      <h2 className="mb-4 text-2xl font-black text-navy">{session.paper.title}</h2>
      {drillId ? (
        <p className="mx-auto mb-4 max-w-lg text-sm text-muted">
          This launch came from a focused drill route, so listen for the error pattern before
          returning to review.
        </p>
      ) : null}

      <div className="mx-auto mb-8 max-w-lg space-y-4 rounded-2xl bg-background-light p-6 text-left">
        <h3 className="text-sm font-black uppercase tracking-widest text-muted">Before you start</h3>
        <ul className="space-y-3 text-sm text-muted">
          <li className="flex items-start gap-2">
            <CheckCircle2 className="h-5 w-5 shrink-0 text-success" />
            <span>Answers autosave to your server attempt as you work.</span>
          </li>
          <li className="flex items-start gap-2">
            <CheckCircle2 className="h-5 w-5 shrink-0 text-success" />
            <span>Transcript evidence and answer keys stay locked until submit.</span>
          </li>
          <li className="flex items-start gap-2">
            <Lock className="h-5 w-5 shrink-0 text-warning" />
            <span>
              <strong className="text-navy">Forward-only exam:</strong> once you press Next on a
              section, it locks permanently and you cannot return to it.
            </span>
          </li>
          <li className="flex items-start gap-2">
            <Timer className="h-5 w-5 shrink-0 text-warning" />
            <span>
              <strong className="text-navy">Review windows:</strong> A1 = 60s, A2 = 60s, C1 = 30s,
              C2 = 120s. Part B has no review window. Answer boxes remain editable during each
              window for its own section only.
            </span>
          </li>
          <li className="flex items-start gap-2">
            <Volume2 className="h-5 w-5 shrink-0 text-muted" />
            <span>
              Part B consists of six short workplace extracts (~40 seconds each), one multiple-choice
              item per extract.
            </span>
          </li>
          {isExam ? (
            <li className="flex items-start gap-2">
              <AlertCircle className="h-5 w-5 shrink-0 text-danger" />
              <span className="font-bold text-danger">
                Exam mode plays once and disables pause/scrub controls.
              </span>
            </li>
          ) : (
            <li className="flex items-start gap-2">
              <CheckCircle2 className="h-5 w-5 shrink-0 text-success" />
              <span>Practice mode allows pause and scrubbing while you build accuracy.</span>
            </li>
          )}
          {session.modePolicy.integrityLockRequired ? (
            <li className="flex items-start gap-2">
              <Lock className="h-5 w-5 shrink-0 text-danger" />
              <span className="font-bold text-danger">
                OET@Home: stay in full-screen for the entire test. Leaving full-screen flags an
                integrity event.
              </span>
            </li>
          ) : null}
          {session.modePolicy.printableBooklet ? (
            <li className="flex items-start gap-2">
              <FileText className="h-5 w-5 shrink-0 text-warning" />
              <span>
                Paper-simulation: open the printable booklet alongside the player and write your
                answers there before transcribing them online.
              </span>
            </li>
          ) : null}
        </ul>
      </div>

      {extracts.length > 0 ? (
        <div className="mx-auto mb-8 max-w-2xl rounded-2xl border border-border bg-surface p-5 text-left">
          <h3 className="text-sm font-black uppercase tracking-widest text-muted">Extract metadata</h3>
          <div className="mt-4 grid gap-3 sm:grid-cols-2">
            {extracts.map((extract) => (
              <div
                key={`${extract.partCode}-${extract.displayOrder}`}
                className="rounded-xl border border-border bg-background-light p-3 text-sm"
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="font-bold text-navy">
                    {extract.partCode} · {extract.title}
                  </span>
                  <span className="text-xs font-semibold uppercase text-muted">{extract.kind}</span>
                </div>
                <p className="mt-2 text-xs text-muted">
                  {extract.accentCode ?? 'Accent not specified'} · {describeSpeakers(extract)}
                </p>
                {extract.audioStartMs != null || extract.audioEndMs != null ? (
                  <p className="mt-1 text-xs text-muted">
                    Audio window {formatMilliseconds(extract.audioStartMs) ?? '00:00'} -{' '}
                    {formatMilliseconds(extract.audioEndMs) ?? 'end'}
                  </p>
                ) : null}
              </div>
            ))}
          </div>
        </div>
      ) : null}

      {session.modePolicy.printableBooklet ? (
        <div className="mx-auto mb-8 max-w-2xl rounded-2xl border border-border bg-surface p-5 text-left">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <h3 className="text-sm font-black uppercase tracking-widest text-muted">
                Printable booklet
              </h3>
              <p className="mt-1 text-sm text-muted">
                Print the answer sheet before starting, then transcribe final answers into the online
                boxes.
              </p>
            </div>
            <Button variant="outline" onClick={() => window.print()} className="gap-2">
              <FileText className="h-4 w-4" /> Print
            </Button>
          </div>
          <div className="mt-4 grid grid-cols-2 gap-2 text-xs text-muted sm:grid-cols-4">
            {session.questions.map((question) => (
              <div
                key={`print-preview-${question.id}`}
                className="rounded-lg border border-border bg-background-light px-3 py-2"
              >
                Q{question.number} <span className="text-muted/70">{question.partCode}</span>
              </div>
            ))}
          </div>
        </div>
      ) : null}

      {!session.paper.audioAvailable ? (
        <InlineAlert variant="warning" className="mx-auto mb-6 max-w-lg text-left">
          {session.paper.audioUnavailableReason ?? 'Audio is not available for this task yet.'}
        </InlineAlert>
      ) : null}
      {!session.readiness.objectiveReady ? (
        <InlineAlert variant="warning" className="mx-auto mb-6 max-w-lg text-left">
          {session.readiness.missingReason ?? 'Structured Listening questions are not ready yet.'}
        </InlineAlert>
      ) : null}
      {audioError ? (
        <InlineAlert variant="error" className="mx-auto mb-6 max-w-lg text-left">
          {audioError}
        </InlineAlert>
      ) : null}
      {startError ? (
        <InlineAlert variant="error" className="mx-auto mb-6 max-w-lg text-left">
          {startError}
        </InlineAlert>
      ) : null}

      {strictReadinessRequired ? (
        <div className="mx-auto mb-8 max-w-2xl text-left">
          <TechReadinessCheck onReady={onTechReadinessReady} />
        </div>
      ) : null}

      <div className="flex flex-col justify-center gap-3 sm:flex-row">
        <Button
          size="lg"
          onClick={onStart}
          disabled={
            isStarting ||
            !session.paper.audioAvailable ||
            !session.readiness.objectiveReady ||
            (strictReadinessRequired && !techReadiness?.audioOk)
          }
          className="gap-2"
        >
          {isStarting ? <Loader2 className="h-5 w-5 animate-spin" /> : <Play className="h-5 w-5" />}
          {isStarting ? 'Starting...' : 'Start Audio & Task'}
        </Button>
        {session.paper.questionPaperUrl ? (
          <Link href={session.paper.questionPaperUrl} target="_blank">
            <Button size="lg" variant="outline" className="gap-2">
              <FileText className="h-5 w-5" /> Question Paper
            </Button>
          </Link>
        ) : null}
      </div>
    </motion.div>
  );
}
