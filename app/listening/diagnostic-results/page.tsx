'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import {
  Trophy,
  Target,
  Headphones,
  Pencil,
  SpellCheck2,
  Clock,
  CalendarDays,
  ChevronRight,
  Sparkles,
  AlertTriangle,
} from 'lucide-react';
import { useAuth } from '@/contexts/auth-context';
import {
  accentLabel,
  getDiagnosticResults,
  skillLabel,
  type DiagnosticResult,
  type RoadmapWeek,
} from '@/lib/listening-pathway-api';
import { SkillRadarChart } from '@/components/listening/SkillRadarChart';
import { AccentBarChart } from '@/components/listening/AccentBarChart';

// ─────────────────────────────────────────────────────────────────────────────
// Local helpers
// ─────────────────────────────────────────────────────────────────────────────

function phaseTone(phase: string): { bg: string; text: string; ring: string } {
  switch (phase) {
    case 'foundation':
      return { bg: 'bg-sky-100', text: 'text-sky-700', ring: 'ring-sky-200' };
    case 'practice':
      return { bg: 'bg-amber-100', text: 'text-amber-800', ring: 'ring-amber-200' };
    case 'mastery':
      return { bg: 'bg-emerald-100', text: 'text-emerald-800', ring: 'ring-emerald-200' };
    default:
      return { bg: 'bg-lavender', text: 'text-primary', ring: 'ring-primary/20' };
  }
}

function formatSeconds(value: number): string {
  if (!value || value <= 0) return '0:00';
  const m = Math.floor(value / 60);
  const s = value % 60;
  return `${m}:${String(s).padStart(2, '0')}`;
}

// ─────────────────────────────────────────────────────────────────────────────
// Section components
// ─────────────────────────────────────────────────────────────────────────────

function HeroSection({ result }: { result: DiagnosticResult }) {
  const { hero } = result;
  return (
    <div className="rounded-2xl border border-border bg-surface p-8 text-center shadow-sm">
      <p className="mb-2 text-sm font-semibold uppercase tracking-widest text-primary">
        Diagnostic complete
      </p>
      <h1 className="mb-4 text-xl font-extrabold text-navy">
        Your estimated OET Listening score
      </h1>
      <div className="mb-1 text-5xl font-extrabold text-navy">{hero.scaledScore}</div>
      <p className="text-sm text-muted">
        Raw {hero.rawScore}/{hero.totalQuestions} · Grade {hero.gradeLabel || '-'}
      </p>
      <div className="mt-4 flex flex-wrap justify-center gap-2 text-xs">
        <span className="inline-flex items-center gap-1 rounded-full bg-lavender px-3 py-1 font-semibold text-primary">
          <Target className="h-3.5 w-3.5" aria-hidden />
          Target {hero.targetBandLabel || '-'}
        </span>
        <span className="inline-flex items-center gap-1 rounded-full bg-background-light px-3 py-1 font-medium text-navy">
          Confidence: {hero.confidenceLowerBound}–{hero.confidenceUpperBound}
        </span>
        <span className="inline-flex items-center gap-1 rounded-full bg-success/10 px-3 py-1 font-medium text-success">
          +0 since baseline
        </span>
      </div>
    </div>
  );
}

function SkillRadarSection({ result }: { result: DiagnosticResult }) {
  if (result.skillRadar.length === 0) return null;
  return (
    <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
      <h2 className="mb-1 text-base font-bold text-navy">Sub-skill radar</h2>
      <p className="mb-4 text-sm text-muted">
        How you scored across the eight OET listening sub-skills.
      </p>
      <SkillRadarChart scores={result.skillRadar} />
      <ul className="mt-5 grid grid-cols-2 gap-y-2 text-xs text-muted">
        {result.skillRadar.map((skill) => (
          <li key={skill.skillCode} className="flex justify-between gap-3">
            <span className="font-medium text-navy">
              {skill.skillCode} · {skill.label || skillLabel(skill.skillCode)}
            </span>
            <span className="font-bold text-navy">{skill.diagnosticScore}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function AccentSection({ result }: { result: DiagnosticResult }) {
  const weakAccents = useMemo(
    () => result.accentChart.filter((accent) => accent.accuracyPercentage < 60),
    [result.accentChart],
  );

  if (result.accentChart.length === 0) return null;

  return (
    <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
      <h2 className="mb-1 text-base font-bold text-navy">Accent profile</h2>
      <p className="mb-4 text-sm text-muted">
        Accuracy by accent across the diagnostic.
      </p>
      <AccentBarChart accents={result.accentChart} />
      {weakAccents.length > 0 && (
        <div className="mt-4 flex items-start gap-3 rounded-xl border border-warning/20 bg-warning/10 p-4">
          <Headphones className="mt-0.5 h-4 w-4 shrink-0 text-warning" aria-hidden />
          <p className="text-sm text-navy">
            We&apos;ve assigned extra{' '}
            <span className="font-semibold">
              {weakAccents.map((a) => a.label || accentLabel(a.accent)).join(', ')}
            </span>{' '}
            practice to your daily plan to bring this above 60%.
          </p>
        </div>
      )}
    </section>
  );
}

function NoteTakingSection({ result }: { result: DiagnosticResult }) {
  const { noteTakingStats } = result;
  return (
    <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
      <div className="mb-3 flex items-center gap-2">
        <Pencil className="h-4 w-4 text-primary" aria-hidden />
        <h2 className="text-base font-bold text-navy">Note-taking analysis</h2>
      </div>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div className="rounded-xl bg-background-light p-4">
          <p className="text-xs uppercase tracking-wide text-muted">You typed</p>
          <p className="mt-1 text-2xl font-extrabold text-navy">
            {noteTakingStats.charactersTyped}
          </p>
          <p className="text-xs text-muted">characters across Part A</p>
        </div>
        <div className="rounded-xl bg-background-light p-4">
          <p className="text-xs uppercase tracking-wide text-muted">Typical range</p>
          <p className="mt-1 text-2xl font-extrabold text-navy">
            {noteTakingStats.typicalRangeLow}–{noteTakingStats.typicalRangeHigh}
          </p>
          <p className="text-xs text-muted">for learners at your level</p>
        </div>
      </div>
      {noteTakingStats.droppedDetails.length > 0 && (
        <div className="mt-4">
          <p className="mb-2 text-sm font-semibold text-navy">Details you missed</p>
          <ul className="space-y-1 text-sm text-muted">
            {noteTakingStats.droppedDetails.map((detail, idx) => (
              <li key={`${detail}-${idx}`} className="flex gap-2">
                <span aria-hidden>•</span>
                <span>{detail}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}

function SpellingSection({ result }: { result: DiagnosticResult }) {
  const { spellingStats } = result;
  if (
    spellingStats.meaningCorrectSpellingWrong === 0 &&
    spellingStats.examples.length === 0
  ) {
    return null;
  }
  return (
    <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
      <div className="mb-3 flex items-center gap-2">
        <SpellCheck2 className="h-4 w-4 text-primary" aria-hidden />
        <h2 className="text-base font-bold text-navy">Spelling tolerance</h2>
      </div>
      <p className="text-sm text-muted">
        <span className="font-bold text-navy">{spellingStats.meaningCorrectSpellingWrong}</span>{' '}
        answers had the right meaning but were marked wrong on spelling.
      </p>
      {spellingStats.examples.length > 0 && (
        <ul className="mt-3 space-y-2 text-sm">
          {spellingStats.examples.map((example, idx) => (
            <li
              key={`${example.wrong}-${idx}`}
              className="flex flex-wrap items-center gap-2 rounded-xl bg-background-light px-3 py-2"
            >
              <span className="rounded-md bg-danger/10 px-2 py-0.5 font-medium text-danger line-through">
                {example.wrong}
              </span>
              <span aria-hidden className="text-muted">
                →
              </span>
              <span className="rounded-md bg-success/10 px-2 py-0.5 font-medium text-success">
                {example.right}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function TimeAnalysisSection({ result }: { result: DiagnosticResult }) {
  const { timeAnalysis } = result;
  return (
    <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
      <div className="mb-3 flex items-center gap-2">
        <Clock className="h-4 w-4 text-primary" aria-hidden />
        <h2 className="text-base font-bold text-navy">Time analysis</h2>
      </div>
      <div className="grid grid-cols-3 gap-3">
        {[
          { label: 'Part A', value: timeAnalysis.partABreakdown },
          { label: 'Part B', value: timeAnalysis.partBBreakdown },
          { label: 'Part C', value: timeAnalysis.partCBreakdown },
        ].map((entry) => (
          <div key={entry.label} className="rounded-xl bg-background-light p-3 text-center">
            <p className="text-xs font-semibold uppercase text-muted">{entry.label}</p>
            <p className="mt-1 text-lg font-extrabold text-navy">
              {formatSeconds(entry.value)}
            </p>
          </div>
        ))}
      </div>
      {timeAnalysis.hesitationFlags.length > 0 && (
        <div className="mt-4">
          <p className="mb-2 flex items-center gap-1 text-sm font-semibold text-warning">
            <AlertTriangle className="h-3.5 w-3.5" aria-hidden />
            Hesitation flags
          </p>
          <ul className="space-y-1 text-sm text-muted">
            {timeAnalysis.hesitationFlags.map((flag, idx) => (
              <li key={`${flag}-${idx}`} className="flex gap-2">
                <span aria-hidden>•</span>
                <span>{flag}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}

function RoadmapWeekCard({ week }: { week: RoadmapWeek }) {
  const tone = phaseTone(week.phase);
  return (
    <div className="flex flex-col gap-2 rounded-xl border border-border bg-surface p-4 shadow-sm">
      <div className="flex items-center justify-between">
        <span className="text-xs font-bold uppercase tracking-wide text-muted">
          Week {week.weekNumber}
        </span>
        <span
          className={`inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-[10px] font-bold uppercase ring-1 ${tone.bg} ${tone.text} ${tone.ring}`}
        >
          {week.phase}
        </span>
      </div>
      {week.focusSkills.length > 0 && (
        <div className="flex flex-wrap gap-1">
          {week.focusSkills.map((skill) => (
            <span
              key={skill}
              className="rounded-md bg-lavender px-1.5 py-0.5 text-[10px] font-semibold text-primary"
            >
              {skill}
            </span>
          ))}
        </div>
      )}
      {week.focusAccents.length > 0 && (
        <div className="flex flex-wrap gap-1">
          {week.focusAccents.map((accent) => (
            <span
              key={accent}
              className="rounded-md bg-warning/10 px-1.5 py-0.5 text-[10px] font-medium text-warning"
            >
              {accentLabel(accent)}
            </span>
          ))}
        </div>
      )}
      <div className="mt-1 flex items-center justify-between text-[11px] text-muted">
        <span>{week.dailyMinutes} min / day</span>
        {week.mockAtEndOfWeek && (
          <span className="inline-flex items-center gap-1 rounded-full bg-success/10 px-1.5 py-0.5 font-semibold text-success">
            <Trophy className="h-2.5 w-2.5" aria-hidden />
            Mock
          </span>
        )}
      </div>
    </div>
  );
}

function RoadmapSection({ result }: { result: DiagnosticResult }) {
  if (result.roadmap.length === 0) return null;
  return (
    <section className="rounded-2xl border border-border bg-lavender/30 p-6">
      <div className="mb-4 flex items-center gap-2">
        <CalendarDays className="h-4 w-4 text-primary" aria-hidden />
        <h2 className="text-base font-bold text-navy">Your 12-week roadmap</h2>
      </div>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4">
        {result.roadmap.map((week) => (
          <RoadmapWeekCard key={week.weekNumber} week={week} />
        ))}
      </div>
    </section>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Page
// ─────────────────────────────────────────────────────────────────────────────

export default function ListeningDiagnosticResultsPage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const router = useRouter();
  const searchParams = useSearchParams();

  const [result, setResult] = useState<DiagnosticResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!authLoading && !isAuthenticated) {
      router.replace('/sign-in');
    }
  }, [authLoading, isAuthenticated, router]);

  useEffect(() => {
    let cancelled = false;
    const sessionId = searchParams?.get('sessionId') ?? null;
    if (!sessionId) {
      setError('Missing diagnostic session.');
      return;
    }
    (async () => {
      try {
        const data = await getDiagnosticResults(sessionId);
        if (!cancelled) setResult(data);
      } catch {
        if (!cancelled) setError('Results not available.');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [searchParams]);

  if (authLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-lavender border-t-primary" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center gap-6 px-4">
        <p className="text-muted">{error}</p>
        <button
          type="button"
          onClick={() => router.push('/listening')}
          className="rounded-xl bg-primary px-6 py-3 text-sm font-bold text-white transition-[color,background-color,transform] duration-200 hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
        >
          Go to Listening home
        </button>
      </div>
    );
  }

  if (!result) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-lavender border-t-primary" />
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-start bg-background-light px-4 py-12">
      <div className="w-full max-w-3xl space-y-6">
        <HeroSection result={result} />
        <SkillRadarSection result={result} />
        <AccentSection result={result} />
        <NoteTakingSection result={result} />
        <SpellingSection result={result} />
        <TimeAnalysisSection result={result} />
        <RoadmapSection result={result} />

        <div className="flex flex-col items-center gap-3 py-4">
          <button
            type="button"
            onClick={() => router.push('/listening/pathway')}
            className="inline-flex items-center gap-2 rounded-full bg-primary px-10 py-4 text-base font-bold text-white shadow-lg transition-colors hover:bg-primary-dark dark:bg-violet-700 dark:hover:bg-violet-600 active:scale-95"
          >
            <Sparkles className="h-4 w-4" aria-hidden />
            Start your plan
            <ChevronRight className="h-4 w-4" aria-hidden />
          </button>
          <button
            type="button"
            onClick={() => router.push('/listening')}
            className="text-sm font-medium text-primary hover:underline"
          >
            Or head to the listening dashboard
          </button>
        </div>
      </div>
    </div>
  );
}
