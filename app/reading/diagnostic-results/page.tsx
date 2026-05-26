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
    ? 'bg-emerald-100 text-emerald-700'
    : isDeveloping
      ? 'bg-amber-100 text-amber-700'
      : 'bg-red-100 text-red-700';
  const label = isPass ? 'Strong' : isDeveloping ? 'Developing' : 'Needs Work';

  return (
    <span className={`inline-flex items-center rounded-full px-4 py-1 text-sm font-bold ${tone}`}>
      {score} — {label}
    </span>
  );
}

function SkillBar({ code, score }: { code: string; score: number }) {
  const pct = Math.min(100, Math.round((score / 10) * 100));
  const color =
    score >= 7 ? 'bg-emerald-500' : score >= 4 ? 'bg-amber-400' : 'bg-red-400';
  return (
    <div className="flex items-center gap-3">
      <span className="w-8 shrink-0 text-xs font-bold text-gray-500 uppercase">{code}</span>
      <div className="flex-1 overflow-hidden rounded-full bg-gray-100 h-2.5">
        <div
          className={`h-full rounded-full transition-all ${color}`}
          style={{ width: `${pct}%` }}
        />
      </div>
      <span className="w-8 text-right text-xs font-semibold text-gray-700">{score}/10</span>
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

      if (typeof window !== 'undefined') {
        const raw = sessionStorage.getItem('diagnostic_result');
        if (raw) {
          try {
            const parsed = JSON.parse(raw) as unknown;
            if (isDiagnosticResult(parsed, sessionId)) {
              if (!cancelled) setResult(parsed);
              return;
            }

            sessionStorage.removeItem('diagnostic_result');
          } catch {
            sessionStorage.removeItem('diagnostic_result');
          }
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
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-violet-200 border-t-violet-600" />
      </div>
    );
  }

  if (notFound) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center gap-6 px-4">
        <p className="text-gray-600">Results not available.</p>
        <button
          type="button"
          onClick={() => router.push('/reading')}
          className="rounded-xl bg-violet-600 px-6 py-3 text-sm font-bold text-white hover:bg-violet-700"
        >
          Go to Reading Home
        </button>
      </div>
    );
  }

  if (!result) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-violet-200 border-t-violet-600" />
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
    <div className="flex min-h-screen flex-col items-center justify-start bg-gradient-to-b from-violet-50 to-white px-4 py-12">
      <div className="w-full max-w-xl space-y-6">
        {/* Hero score card */}
        <div className="rounded-2xl border border-violet-100 bg-white p-8 text-center shadow-sm">
          <p className="mb-2 text-sm font-semibold uppercase tracking-widest text-violet-500">
            Diagnostic Complete
          </p>
          <h1 className="mb-4 text-xl font-extrabold text-gray-900">
            Your estimated OET Reading score
          </h1>
          <div className="mb-2 text-5xl font-extrabold text-gray-900">
            {result.estimatedScaledScore}
          </div>
          <div className="mt-2">
            <ScoreBadge score={result.estimatedScaledScore} />
          </div>
          <p className="mt-3 text-sm text-gray-500">
            Raw score: {result.score}/{result.totalQuestions} · Estimated grade {result.estimatedOetBand}
          </p>
        </div>

        {/* Sub-skill breakdown */}
        {skillEntries.length > 0 && (
          <div className="rounded-2xl border border-gray-100 bg-white p-6 shadow-sm">
            <h2 className="mb-4 text-base font-bold text-gray-900">Skill Breakdown</h2>
            <div className="space-y-3">
              {skillEntries.map(([code, score]) => (
                <SkillBar key={code} code={code} score={score} />
              ))}
            </div>
          </div>
        )}

        {/* Time analysis */}
        <div className="rounded-2xl border border-gray-100 bg-white p-6 shadow-sm">
          <h2 className="mb-3 text-base font-bold text-gray-900">Diagnostic Timing</h2>
          <p className="text-2xl font-extrabold text-gray-900">{durationLabel}</p>
          <p className="mt-1 text-sm text-gray-500">A 25-minute diagnostic is the target pace.</p>
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
              if (typeof window !== 'undefined') {
                sessionStorage.removeItem('diagnostic_result');
              }
              router.push('/reading/pathway');
            }}
            className="inline-flex items-center gap-2 rounded-full bg-violet-600 px-10 py-4 text-base font-bold text-white shadow-lg transition hover:bg-violet-700 active:scale-95"
          >
            Start My Learning Plan
          </button>
        </div>
      </div>
    </div>
  );
}
