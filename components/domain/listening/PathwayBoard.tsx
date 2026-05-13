'use client';

import Link from 'next/link';
import { ArrowRight, CheckCircle2, Circle, Lock, PlayCircle } from 'lucide-react';

export interface PathwayStageView {
  stageKey: string;
  status: 'Locked' | 'Unlocked' | 'InProgress' | 'Completed';
  bestScaledScore: number | null;
  attemptsCount: number;
  actionHref?: string;
}

export interface PathwayBoardProps {
  stages: PathwayStageView[];
}

const STAGE_META: Record<string, { label: string; focus: string }> = {
  diagnostic: { label: 'Diagnostic', focus: 'Placement attempt and first weakness map.' },
  foundation_partA: { label: 'Foundation - Part A', focus: 'Exact clinical notes, numbers, and spellings.' },
  foundation_partB: { label: 'Foundation - Part B', focus: 'Short workplace extracts and final-intention traps.' },
  foundation_partC: { label: 'Foundation - Part C', focus: 'Long talks, opinion shifts, and speaker attitude.' },
  drill_partA: { label: 'Drill - Part A', focus: 'Repeat exact-detail capture under mild time pressure.' },
  drill_partB: { label: 'Drill - Part B', focus: 'Separate distractors from the final answer.' },
  drill_partC: { label: 'Drill - Part C', focus: 'Track argument structure across longer audio.' },
  minitest_partA: { label: 'Mini-Test - Part A', focus: 'Part A timing with strict marking.' },
  minitest_partBC: { label: 'Mini-Test - Parts B+C', focus: 'MCQ stamina before full papers.' },
  fullpaper_paper: { label: 'Full Paper (paper mode)', focus: 'Free-navigation paper simulation.' },
  fullpaper_cbt: { label: 'Full Paper (CBT mode)', focus: 'One-play computer-based timing and locks.' },
  exam_simulation: { label: 'OET@Home exam simulation', focus: 'Final rehearsal with strict home-mode constraints.' },
};

export function pathwayStageLabel(stageKey: string) {
  return STAGE_META[stageKey]?.label ?? stageKey.replace(/_/g, ' ');
}

/**
 * Listening V2 — 12-stage learner pathway board. Reads `/v1/listening/v2/me/pathway`.
 * Status colors follow DESIGN.md: green (completed), blue (in-progress),
 * neutral (unlocked), muted/locked (gated).
 */
export function PathwayBoard({ stages }: PathwayBoardProps) {
  return (
    <ol className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
      {stages.map((stage, index) => (
        <PathwayTile key={stage.stageKey} stage={stage} index={index} />
      ))}
    </ol>
  );
}

function PathwayTile({ stage, index }: { stage: PathwayStageView; index: number }) {
  const meta = STAGE_META[stage.stageKey] ?? { label: pathwayStageLabel(stage.stageKey), focus: 'Listening pathway stage.' };
  const unlocked = stage.status === 'Unlocked' || stage.status === 'InProgress';
  const canOpen = unlocked && stage.actionHref;
  const actionVerb = stage.status === 'InProgress' ? 'Continue' : 'Start';

  const body = (
    <div
      className={`flex min-h-[190px] flex-col rounded-2xl border p-5 shadow-sm transition ${tileClass(stage.status)} ${
        canOpen ? 'hover:-translate-y-0.5 hover:shadow-clinical focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2' : ''
      }`}
    >
      <div className="flex items-start justify-between gap-3">
        <span className="rounded-lg bg-white/70 px-2.5 py-1 text-[11px] font-black uppercase tracking-[0.16em] text-muted">
          Stage {index + 1}
        </span>
        <StatusBadge status={stage.status} />
      </div>
      <h3 className="mt-5 text-lg font-bold text-navy">{meta.label}</h3>
      <p className="mt-2 text-sm leading-6 text-muted">{meta.focus}</p>
      <div className="mt-auto pt-5">
        <div className="flex flex-wrap items-center gap-2 text-xs font-semibold text-muted">
          {stage.bestScaledScore != null ? <span>Best {stage.bestScaledScore}/500</span> : null}
          {stage.attemptsCount > 0 ? <span>{stage.attemptsCount} attempt{stage.attemptsCount === 1 ? '' : 's'}</span> : null}
          {stage.bestScaledScore == null && stage.attemptsCount === 0 ? <span>Not started</span> : null}
        </div>
        {canOpen ? (
          <span className="mt-4 inline-flex items-center gap-2 text-sm font-bold text-primary">
            {actionVerb} {meta.label} <ArrowRight className="h-4 w-4" aria-hidden />
          </span>
        ) : null}
      </div>
    </div>
  );

  if (!canOpen) return <li>{body}</li>;

  return (
    <li>
      <Link href={stage.actionHref!} aria-label={`${actionVerb} ${meta.label}`} className="block">
        {body}
      </Link>
    </li>
  );
}

function tileClass(status: PathwayStageView['status']) {
  if (status === 'Completed') return 'border-success/30 bg-success/5';
  if (status === 'InProgress') return 'border-info/30 bg-info/5';
  if (status === 'Unlocked') return 'border-primary/25 bg-surface';
  return 'border-border bg-background-light opacity-75';
}

function StatusBadge({ status }: { status: PathwayStageView['status'] }) {
  if (status === 'Completed') {
    return (
      <span className="inline-flex items-center gap-1 rounded-lg bg-success/10 px-2 py-1 text-xs font-bold text-success">
        <CheckCircle2 className="h-3.5 w-3.5" aria-hidden /> Completed
      </span>
    );
  }
  if (status === 'InProgress') {
    return (
      <span className="inline-flex items-center gap-1 rounded-lg bg-info/10 px-2 py-1 text-xs font-bold text-info">
        <PlayCircle className="h-3.5 w-3.5" aria-hidden /> In progress
      </span>
    );
  }
  if (status === 'Unlocked') {
    return (
      <span className="inline-flex items-center gap-1 rounded-lg bg-primary/10 px-2 py-1 text-xs font-bold text-primary">
        <Circle className="h-3.5 w-3.5" aria-hidden /> Ready
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 rounded-lg bg-slate-100 px-2 py-1 text-xs font-bold text-muted">
      <Lock className="h-3.5 w-3.5" aria-hidden /> Locked
    </span>
  );
}
