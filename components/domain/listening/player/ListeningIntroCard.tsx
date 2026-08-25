'use client';

// Listening V2 — pre-start intro card. Renders mode-specific guidance,
// extract metadata, and the
// readiness gate + Start CTA. Extracted from the monolithic
// `app/listening/player/[id]/page.tsx` so the surface can be Storybook'd
// and tested in isolation without booting the full Suspense + FSM tree.

import { motion, useReducedMotion } from 'motion/react';
import { AlertCircle, CheckCircle2, Loader2, Lock, Play, Timer, Volume2 } from 'lucide-react';
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
  audioUrls: string[];
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
    audioUrls,
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
      : isExam
        ? 'Exam Mode'
        : 'Practice Mode';

  return (
    <motion.div
      {...sectionMotion}
      className="mt-8 rounded-2xl border border-border bg-surface p-8 text-center shadow-sm sm:p-12"
      data-testid="listening-intro-card"
    >
      <Volume2 className="mx-auto mb-4 h-9 w-9 text-primary" aria-hidden="true" />
      <p className="mb-2 text-xs font-black uppercase tracking-widest text-muted">{modeLabel}</p>
      <h2 className="mb-4 text-2xl font-black text-navy">{session.paper.title}</h2>
      {session.preflight ? (
        <div className="mx-auto mb-6 max-w-2xl rounded-2xl border border-border bg-background-light p-4 text-left" data-testid="listening-preflight-summary">
          <h3 className="text-xs font-black uppercase tracking-[0.16em] text-muted">Confirm your test</h3>
          <dl className="mt-3 grid gap-2 text-sm sm:grid-cols-2">
            <div>
              <dt className="text-xs font-semibold uppercase text-muted">Candidate</dt>
              <dd className="font-semibold text-navy">{session.preflight.candidate.displayName}</dd>
            </div>
            <div>
              <dt className="text-xs font-semibold uppercase text-muted">Profession</dt>
              <dd className="font-semibold text-navy">{session.preflight.candidate.professionLabel ?? session.preflight.candidate.professionId ?? 'Not specified'}</dd>
            </div>
            <div>
              <dt className="text-xs font-semibold uppercase text-muted">Selected test</dt>
              <dd className="font-semibold text-navy">{session.preflight.selectedTest.title}</dd>
            </div>
            <div>
              <dt className="text-xs font-semibold uppercase text-muted">Eligibility</dt>
              <dd className={session.preflight.eligibility.eligible ? 'font-semibold text-success' : 'font-semibold text-danger'}>
                {session.preflight.eligibility.eligible ? 'Checked — eligible to start' : session.preflight.eligibility.reason ?? 'Not eligible to start'}
              </dd>
            </div>
          </dl>
        </div>
      ) : null}
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
          {session.modePolicy.onePlayOnly ? (
            <li className="flex items-start gap-2">
              <Lock className="h-5 w-5 shrink-0 text-warning" />
              <span>
                <strong className="text-navy">Forward-only:</strong> when a section&apos;s audio ends it
                opens an irreversible finish confirmation. After you confirm, it locks permanently and
                the next section opens; you cannot return to it.
              </span>
            </li>
          ) : (
            <li className="flex items-start gap-2">
              <CheckCircle2 className="h-5 w-5 shrink-0 text-success" />
              <span>
                <strong className="text-navy">Practice controls:</strong> this mode follows its
                server policy for pausing, scrubbing, replaying, and section review navigation.
              </span>
            </li>
          )}
          <li className="flex items-start gap-2">
            <Timer className="h-5 w-5 shrink-0 text-warning" />
            <span>
              <strong className="text-navy">Reading time:</strong> each section gives you a short
              reading window before its audio starts, so you can preview the questions and mark
              answers in advance.
            </span>
          </li>
          <li className="flex items-start gap-2">
            <Volume2 className="h-5 w-5 shrink-0 text-muted" />
            <span>
              Part B consists of six short workplace extracts (~40 seconds each), one multiple-choice
              item per extract.
            </span>
          </li>
          {strictReadinessRequired ? (
            <li className="flex items-start gap-2">
              <Volume2 className="h-5 w-5 shrink-0 text-muted" />
              <span>
                <strong className="text-navy">Exam-day setup guidance:</strong> use reliable audio,
                a stable connection, and a comfortable display. Resolution, headset type, display
                scale, VPN, and similar device checks are advisory on this platform and do not block
                your AI practice attempt.
              </span>
            </li>
          ) : null}
          {session.modePolicy.onePlayOnly ? (
            <li className="flex items-start gap-2">
              <AlertCircle className="h-5 w-5 shrink-0 text-danger" />
              <span className="font-bold text-danger">
                Audio plays once per section and cannot be paused, scrubbed, or replayed. Each section
                requires an irreversible finish confirmation when its audio ends.
              </span>
            </li>
          ) : null}
          {strictReadinessRequired ? (
            <li className="flex items-start gap-2">
              <Lock className="h-5 w-5 shrink-0 text-muted" />
              <span>
                <strong className="text-navy">Focus guidance:</strong> keep this test window
                visible when possible. Full-screen is optional and leaving it does not block the
                attempt; focus and fullscreen changes may be recorded for technical guidance.
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
          <TechReadinessCheck audioUrls={audioUrls} onReady={onTechReadinessReady} />
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
            session.preflight?.eligibility.eligible === false ||
            (strictReadinessRequired && !techReadiness?.audioOk)
          }
          className="gap-2"
        >
          {isStarting ? <Loader2 className="h-5 w-5 animate-spin" /> : <Play className="h-5 w-5" />}
          {isStarting ? 'Starting...' : 'Start Audio & Task'}
        </Button>
      </div>
    </motion.div>
  );
}
