'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { getSkillScores, getAccentProgress, getListeningPathway } from '@/lib/listening-pathway-api';
import type { SkillScore, AccentProgress, Pathway } from '@/lib/listening-pathway-api';
import { SkillRadarChart } from '@/components/listening/SkillRadarChart';
import { AccentBarChart } from '@/components/listening/AccentBarChart';

export default function ListeningStatsPage() {
  const [skills, setSkills] = useState<SkillScore[]>([]);
  const [accents, setAccents] = useState<AccentProgress[]>([]);
  const [pathway, setPathway] = useState<Pathway | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    Promise.allSettled([getSkillScores(), getAccentProgress(), getListeningPathway()])
      .then(([s, a, p]) => {
        if (cancelled) return;
        if (s.status === 'fulfilled') setSkills(s.value);
        if (a.status === 'fulfilled') setAccents(a.value);
        if (p.status === 'fulfilled') setPathway(p.value);
        const firstErr = [s, a, p].find((r) => r.status === 'rejected');
        if (firstErr && firstErr.status === 'rejected') {
          setError(firstErr.reason instanceof Error ? firstErr.reason.message : String(firstErr.reason));
        }
        setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (loading) {
    return (
      <main className="mx-auto max-w-6xl px-4 py-12">
        <div className="text-slate-500">Loading your Listening stats…</div>
      </main>
    );
  }

  return (
    <main className="mx-auto max-w-6xl px-4 py-12 space-y-10">
      <header className="space-y-2">
        <h1 className="text-3xl font-bold tracking-tight">Listening Analytics</h1>
        <p className="text-slate-600">
          Track your sub-skill mastery (L1–L8) and accent confidence over time.
        </p>
        {error && (
          <p className="text-sm text-amber-600">
            Some metrics could not be loaded: {error}. Take the diagnostic if you haven&apos;t already.
          </p>
        )}
      </header>

      <section className="grid gap-8 md:grid-cols-2">
        <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
          <h2 className="text-lg font-semibold mb-4">Sub-skill mastery (L1–L8)</h2>
          {skills.length > 0 ? (
            <>
              <SkillRadarChart scores={skills} />
              <ul className="mt-4 space-y-1 text-sm text-slate-600">
                {skills.map((s) => (
                  <li key={s.skillCode} className="flex justify-between">
                    <span>{s.label}</span>
                    <span className="font-mono">{s.currentScore.toFixed(1)} / 10</span>
                  </li>
                ))}
              </ul>
            </>
          ) : (
            <p className="text-sm text-slate-500">
              Skill scores will appear after your first diagnostic.
            </p>
          )}
        </div>

        <div className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
          <h2 className="text-lg font-semibold mb-4">Accent confidence</h2>
          {accents.length > 0 ? (
            <AccentBarChart accents={accents} />
          ) : (
            <p className="text-sm text-slate-500">
              Accent breakdown will appear after your first diagnostic.
            </p>
          )}
        </div>
      </section>

      <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
        <h2 className="text-lg font-semibold mb-4">Your 12-week roadmap</h2>
        {pathway && pathway.weeks.length > 0 ? (
          <div className="grid gap-3 md:grid-cols-3 lg:grid-cols-4">
            {pathway.weeks.map((w) => (
              <article
                key={w.weekNumber}
                className="rounded-lg border border-slate-200 bg-slate-50 p-3 text-sm"
              >
                <div className="flex items-center justify-between mb-1">
                  <span className="font-semibold">Week {w.weekNumber}</span>
                  <span className="text-xs uppercase tracking-wide text-slate-500">{w.phase}</span>
                </div>
                <p className="text-slate-700">{w.notes}</p>
                <p className="mt-1 text-xs text-slate-500">~{w.dailyMinutes} min/day</p>
              </article>
            ))}
          </div>
        ) : (
          <p className="text-sm text-slate-500">
            Your roadmap will be generated after the diagnostic.{' '}
            <Link href="/listening/diagnostic" className="text-blue-600 underline">
              Take the diagnostic
            </Link>
            .
          </p>
        )}
      </section>

      <nav className="flex flex-wrap gap-3 text-sm">
        <Link href="/listening" className="rounded-md bg-slate-900 px-4 py-2 text-white">
          Back to dashboard
        </Link>
        <Link
          href="/listening/pathway"
          className="rounded-md border border-slate-300 px-4 py-2 text-slate-700"
        >
          View full pathway
        </Link>
      </nav>
    </main>
  );
}
