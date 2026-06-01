'use client';

import { useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useAuth } from '@/contexts/auth-context';
import { getDiagnosticResult, type DiagnosticResultDto } from '@/lib/reading-pathway-api';
import { isListeningReadingPassByScaled, oetGradeFromScaled } from '@/lib/scoring';

function ScoreBadge({ score }: { score: number }) {
  const grade = oetGradeFromScaled(score);
  const isPass = isListeningReadingPassByScaled(score);
  const isDeveloping = grade === 'C+';
  const tone = isPass
    ? 'bg-success/10 text-success'
    : isDeveloping
      ? 'bg-warning/10 text-warning'
      : 'bg-danger/10 text-danger';
  const label = isPass ? 'Strong' : isDeveloping ? 'Developing' : 'Needs Work';

  return (
    <span className={`inline-flex items-center rounded-full px-4 py-1 text-sm font-bold ${tone}`}>
      {score} · {label}
    </span>
  );
}

function SkillBar({ code, score }: { code: string; score: number }) {
  const pct = Math.min(100, Math.round((score / 10) * 100));
  const color =
    score >= 7 ? 'bg-success' : score >= 4 ? 'bg-warning' : 'bg-danger';
  return (
    <div className="flex items-center gap-3">
      <span className="w-8 shrink-0 text-xs font-bold text-muted uppercase">{code}</span>
      <div className="flex-1 overflow-hidden rounded-full bg-border h-2.5">
        <div
          className={`h-full rounded-full transition-[width,background-color] duration-300 ${color}`}
          style={{ width: `${pct}%` }}
        />
      </div>
      <span className="w-8 text-right text-xs font-semibold text-navy">{score}/10</span>
    </div>
  );
}

function isDiagnosticResult(value: unknown, sessionId: string | null): value is DiagnosticResultDto {
  if (!value || typeof value !== 'object') return false;
  const result = value as Partial<DiagnosticResultDto>;

  if (typeof result.sessionId !== 'string') return false;
  if (sessionId && result.sessionId !== sessionId) return false;

  return typeof result.score === 'number'
    && typeof result.totalQuestions === 'number'
    && typeof result.estimatedScaledScore === 'number'
    && typeof result.estimatedOetBand === 'string'
    && typeof result.roadmapWeeks === 'number'
    && typeof result.skillScores === 'object'
    && result.skillScores !== null;
}

function readCachedDiagnosticResult(): string | null {
  if (typeof window === 'undefined') return null;

  try {
    return window.sessionStorage.getItem('diagnostic_result');
  } catch {
    return null;
  }
}

function clearCachedDiagnosticResult(): void {
  if (typeof window === 'undefined') return;

  try {
    window.sessionStorage.removeItem('diagnostic_result');
  } catch {
    // The API fallback below keeps the page usable if storage is blocked.
  }
}

export default function DiagnosticResultsPage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const router = useRouter();
  const searchParams = useSearchParams();
  const [result, setResult] = useState<DiagnosticResultDto | null>(null);
  const [notFound, setNotFound] = useState(false);

  // Auth guard
  useEffect(() => {
    if (!authLoading && !isAuthenticated) {
      router.replace('/sign-in');
    }
  }, [authLoading, isAuthenticated, router]);

  // Read the immediate submit result from sessionStorage, then fall back to the API
  // so refresh/deep-link works after the diagnostic is complete.
  useEffect(() => {
    let cancelled = false;

    const loadResult = async () => {
      const sessionId = searchParams?.get('sessionId') ?? null;

      const raw = readCachedDiagnosticResult();
      if (raw) {
        try {
          const parsed = JSON.parse(raw) as unknown;
          if (isDiagnosticResult(parsed, sessionId)) {
            if (!cancelled) setResult(parsed);
            return;
          }

          clearCachedDiagnosticResult();
        } catch {
          clearCachedDiagnosticResult();
        }
      }

      if (!sessionId) {
        if (!cancelled) setNotFound(true);
        return;
      }

      try {
        const loaded = await getDiagnosticResult(sessionId);
        if (!cancelled) setResult(loaded);
      } catch {
        if (!cancelled) setNotFound(true);
      }
    };

    void loadResult();

    return () => {
      cancelled = true;
    };
  }, [searchParams]);

  if (authLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <div className="h-8 w-8 motion-safe:animate-spin rounded-full border-4 border-violet-200 border-t-violet-600" />
      </div>
    );
  }

  if (notFound) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center gap-6 px-4">
        <p className="text-muted">Results not available.</p>
        <button
          type="button"
          onClick={() => router.push('/reading')}
          className="rounded-xl bg-primary px-6 py-3 text-sm font-bold text-white hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
        >
          Go to Reading Home
        </button>
      </div>
    );
  }

  if (!result) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <div className="h-8 w-8 motion-safe:animate-spin rounded-full border-4 border-violet-200 border-t-violet-600" />
      </div>
    );
  }

  const skillEntries = Object.entries(result.skillScores).sort(([a], [b]) =>
    a.localeCompare(b),
  );
  const durationLabel = result.durationSeconds === null
    ? 'Not recorded'
    : `${Math.floor(result.durationSeconds / 60)}m ${result.durationSeconds % 60}s`;

  return (
    <div className="flex min-h-screen flex-col items-center justify-start bg-background-light px-4 py-12">
      <div className="w-full max-w-xl space-y-6">
        {/* Hero score card */}
        <div className="rounded-2xl border border-violet-100 bg-surface p-8 text-center shadow-sm">
          <p className="mb-2 text-sm font-semibold uppercase tracking-widest text-violet-500">
            Diagnostic Complete
          </p>
          <h1 className="mb-4 text-xl font-extrabold text-navy">
            Your estimated OET Reading score
          </h1>
          <div className="mb-2 text-5xl font-extrabold text-navy">
            {result.estimatedScaledScore}
          </div>
          <div className="mt-2">
            <ScoreBadge score={result.estimatedScaledScore} />
          </div>
          <p className="mt-3 text-sm text-muted">
            Raw score: {result.score}/{result.totalQuestions} · Estimated grade {result.estimatedOetBand}
          </p>
        </div>

        {/* Sub-skill breakdown */}
        {skillEntries.length > 0 && (
          <div className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
            <h2 className="mb-4 text-base font-bold text-navy">Skill Breakdown</h2>
            <div className="space-y-3">
              {skillEntries.map(([code, score]) => (
                <SkillBar key={code} code={code} score={score} />
              ))}
            </div>
          </div>
        )}

        {/* Time analysis */}
        <div className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
          <h2 className="mb-3 text-base font-bold text-navy">Diagnostic Timing</h2>
          <p className="text-2xl font-extrabold text-navy">{durationLabel}</p>
          <p className="mt-1 text-sm text-muted">A 25-minute diagnostic is the target pace.</p>
        </div>

        {/* Roadmap */}
        <div className="rounded-2xl border border-violet-100 bg-violet-50 p-6">
          <p className="text-sm text-violet-700">
            Based on your results, your personalised plan is{' '}
            <span className="font-extrabold text-violet-900">
              {result.roadmapWeeks} weeks
            </span>{' '}
            long.
          </p>
        </div>

        {/* CTA */}
        <div className="text-center">
          <button
            type="button"
            onClick={() => {
              clearCachedDiagnosticResult();
              router.push('/reading/pathway');
            }}
            className="inline-flex items-center gap-2 rounded-full bg-primary px-10 py-4 text-base font-bold text-white shadow-lg transition-colors hover:bg-primary-dark dark:bg-violet-700 dark:hover:bg-violet-600 active:scale-95"
          >
            Start My Learning Plan
          </button>
        </div>
      </div>
    </div>
  );
}
