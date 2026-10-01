'use client';

import { CheckCircle2, Clock3, Info } from 'lucide-react';
import { Button } from '@/components/ui/button';
import type { PlacementDiagnosticArea, PlacementResultReport, PlacementSkillResult } from '@/lib/api/placement';

/**
 * Indicative CEFR placement estimate, shared by the journey (foundation and
 * final profile) and the standalone results page. The primary profile is the
 * four skills only; Grammar and Vocabulary are a separate diagnostic, never a
 * fifth skill and never averaged in. Unmeasured and under-review skills are
 * stated honestly rather than shown as a level.
 */
export function ResultReportCard({
  title,
  report,
  primaryLabel,
  onPrimary,
  embedded = false,
}: {
  title: string;
  report: PlacementResultReport;
  primaryLabel?: string;
  onPrimary?: () => void;
  /** Render without the outer card chrome (inside another card). */
  embedded?: boolean;
}) {
  const skills = PRIMARY_SKILLS.map((key) => report.skills.find((skill) => skill.skill === key)).filter(
    (skill): skill is PlacementSkillResult => Boolean(skill),
  );
  const isPartial = skills.length < PRIMARY_SKILLS.length || skills.some((skill) => skill.status !== 'measured');
  const reasons = report.confidenceReasons ?? report.confidence_reasons ?? [];
  const retestAdvice = report.retestAdvice ?? report.retest_advice;
  const HeadingTag = embedded ? 'h3' : 'h2';

  const body = (
    <div className="space-y-5">
      <header className="space-y-2">
        <p className="text-xs font-semibold uppercase tracking-wide text-muted">
          Indicative CEFR Placement Estimate · Diagnostic English Profile
        </p>
        <HeadingTag className="text-xl font-semibold text-navy">{title}</HeadingTag>
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <Headline report={report} />
          <span className="rounded-full border border-border bg-background-light px-2.5 py-0.5 text-xs font-semibold text-navy">
            Confidence: {confidenceLabel(report.confidence)}
          </span>
          {isPartial ? (
            <span className="rounded-full border border-warning/40 bg-warning/10 px-2.5 py-0.5 text-xs font-semibold text-navy">
              Partial profile
            </span>
          ) : null}
        </div>
      </header>

      <section aria-label="Skill profile" className="grid gap-3 sm:grid-cols-2">
        {skills.map((skill) => (
          <SkillTile key={skill.skill} skill={skill} />
        ))}
      </section>

      <Diagnostics report={report} />

      {reasons.length > 0 ? (
        <section className="space-y-1">
          <h4 className="text-sm font-semibold text-navy">About this estimate</h4>
          <ul className="list-disc space-y-1 pl-5 text-xs text-muted">
            {reasons.map((reason) => (
              <li key={reason}>{reason}</li>
            ))}
          </ul>
        </section>
      ) : null}

      {retestAdvice || report.readiness ? (
        <section className="space-y-2 rounded-xl border border-border bg-background-light p-4">
          <h4 className="text-sm font-semibold text-navy">Next step</h4>
          {report.readiness ? (
            <p className="text-sm text-navy">
              {report.readiness.text}{' '}
              <span className="text-xs text-muted">{report.readiness.disclaimer}</span>
            </p>
          ) : null}
          {retestAdvice ? <p className="text-xs text-muted">{retestAdvice}</p> : null}
        </section>
      ) : null}

      {primaryLabel && onPrimary ? (
        <Button type="button" onClick={onPrimary} className="w-full sm:w-auto">
          {primaryLabel}
        </Button>
      ) : null}
    </div>
  );

  if (embedded) return body;
  return <div className="mx-auto max-w-3xl rounded-2xl border border-border bg-surface p-5 shadow-sm sm:p-6">{body}</div>;
}

const PRIMARY_SKILLS = ['RD', 'LSN', 'SPK', 'WRT'] as const;

const SKILL_LABELS: Record<string, string> = {
  RD: 'Reading',
  LSN: 'Listening',
  SPK: 'Speaking',
  WRT: 'Writing',
};

function confidenceLabel(value: string | null | undefined): string {
  // Beta results are capped at Moderate; anything else reads as Low.
  return String(value ?? '').toLowerCase() === 'moderate' ? 'Moderate' : 'Low';
}

function bandText(band: string | null | undefined, range: [string, string] | null | undefined): string | null {
  if (range && range[0] && range[1] && range[0] !== range[1]) return `${range[0]}–${range[1]}`;
  return band ?? null;
}

function Headline({ report }: { report: PlacementResultReport }) {
  const { kind, band, range } = report.headline;
  if (kind === 'indicative_overall' && band) {
    return <span className="text-sm font-semibold text-navy">Indicative overall profile: {band}</span>;
  }
  if (kind === 'uneven' && range) {
    return (
      <span className="text-sm font-semibold text-navy">
        Uneven profile: {range[0]}–{range[1]}
      </span>
    );
  }
  return <span className="text-sm text-muted">Overall profile shown once all four skills are measured</span>;
}

/** The exact note the engine leads a non-measured Speaking/Writing skill with
 *  while its submission awaits a human rater. Matched exactly — any other
 *  insufficient-evidence note (even one that mentions "review") is a real
 *  evidence gap and is shown as-is. */
export const QUEUED_FOR_REVIEW_NOTE = 'Queued for human review - not yet scored.';

/** Writing/Speaking awaiting a human rater — a wait, not a gap. */
function isUnderReview(skill: PlacementSkillResult): boolean {
  return (
    (skill.skill === 'SPK' || skill.skill === 'WRT') &&
    skill.status !== 'measured' &&
    skill.notes.includes(QUEUED_FOR_REVIEW_NOTE)
  );
}

function SkillTile({ skill }: { skill: PlacementSkillResult }) {
  const label = SKILL_LABELS[skill.skill] ?? skill.skill;
  const canDo = (skill.canDo ?? skill.can_do ?? []).slice(0, 3);
  const growth = (skill.growthAreas ?? skill.growth_areas ?? []).slice(0, 3);
  const level = bandText(skill.band, skill.range);

  let state: { tone: string; text: string; detail?: string };
  if (skill.status === 'measured' && level) {
    state = { tone: 'text-primary', text: level };
  } else if (isUnderReview(skill)) {
    state = { tone: 'text-navy', text: "Being reviewed by Dr Hesham's team", detail: 'Your level appears here once it has been reviewed.' };
  } else if (skill.status === 'not_measured') {
    state = { tone: 'text-muted', text: 'Not taken yet', detail: 'Complete this part to add it to your profile.' };
  } else {
    state = {
      tone: 'text-muted',
      text: 'Not enough evidence yet',
      detail: skill.notes[0] ?? 'There was not enough usable evidence to place this skill.',
    };
  }

  return (
    <article className="space-y-2 rounded-xl border border-border bg-background-light p-4">
      <div className="flex items-start justify-between gap-2">
        <h4 className="text-sm font-semibold text-navy">{label}</h4>
        {skill.status === 'measured' ? (
          <CheckCircle2 className="h-4 w-4 shrink-0 text-success-strong" aria-hidden />
        ) : isUnderReview(skill) ? (
          <Clock3 className="h-4 w-4 shrink-0 text-warning-strong" aria-hidden />
        ) : (
          <Info className="h-4 w-4 shrink-0 text-muted" aria-hidden />
        )}
      </div>
      <p className={`text-lg font-semibold ${state.tone}`}>{state.text}</p>
      {state.detail ? <p className="text-xs text-muted">{state.detail}</p> : null}
      {skill.status === 'measured' && canDo.length > 0 ? (
        <div>
          <p className="text-xs font-semibold text-navy">You can</p>
          <ul className="list-disc space-y-0.5 pl-4 text-xs text-muted">
            {canDo.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </div>
      ) : null}
      {skill.status === 'measured' && growth.length > 0 ? (
        <div>
          <p className="text-xs font-semibold text-navy">Focus next</p>
          <ul className="list-disc space-y-0.5 pl-4 text-xs text-muted">
            {growth.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </div>
      ) : null}
    </article>
  );
}

function DiagnosticArea({ label, area }: { label: string; area: PlacementDiagnosticArea }) {
  return (
    <div className="space-y-1.5 rounded-xl border border-border bg-surface p-3">
      <p className="text-sm font-semibold text-navy">
        {label}{' '}
        <span className="text-xs font-normal text-muted">
          ({area.correct} of {area.total} correct)
        </span>
      </p>
      {area.strengths.length > 0 ? (
        <p className="text-xs text-muted">
          <span className="font-semibold text-navy">Strengths:</span> {area.strengths.join(', ')}
        </p>
      ) : null}
      {area.weaknesses.length > 0 ? (
        <p className="text-xs text-muted">
          <span className="font-semibold text-navy">Priorities:</span> {area.weaknesses.join(', ')}
        </p>
      ) : null}
    </div>
  );
}

function Diagnostics({ report }: { report: PlacementResultReport }) {
  const breakdown = report.diagnostics?.language_systems ?? null;
  const constructs = report.diagnostics?.languageSystems;
  const strong = constructs?.constructsStrong ?? [];
  const weak = constructs?.constructsWeak ?? [];
  const hasBreakdown = Boolean(breakdown?.grammar?.total || breakdown?.vocabulary?.total);
  if (!hasBreakdown && strong.length === 0 && weak.length === 0) return null;

  return (
    <section className="space-y-2">
      <h4 className="text-sm font-semibold text-navy">Grammar &amp; Vocabulary diagnostics</h4>
      <p className="text-xs text-muted">
        A diagnostic from Part 1. It guides your study plan and is not part of the skill levels above.
      </p>
      {hasBreakdown ? (
        <div className="grid gap-2 sm:grid-cols-2">
          {breakdown?.grammar?.total ? <DiagnosticArea label="Grammar" area={breakdown.grammar} /> : null}
          {breakdown?.vocabulary?.total ? <DiagnosticArea label="Vocabulary" area={breakdown.vocabulary} /> : null}
        </div>
      ) : (
        <div className="space-y-1 rounded-xl border border-border bg-surface p-3 text-xs text-muted">
          {strong.length > 0 ? (
            <p>
              <span className="font-semibold text-navy">Strengths:</span> {strong.join(', ')}
            </p>
          ) : null}
          {weak.length > 0 ? (
            <p>
              <span className="font-semibold text-navy">Priorities:</span> {weak.join(', ')}
            </p>
          ) : null}
        </div>
      )}
    </section>
  );
}
