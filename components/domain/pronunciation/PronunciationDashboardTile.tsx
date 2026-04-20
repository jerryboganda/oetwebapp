'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { Mic, ChevronRight } from 'lucide-react';
import { fetchPronunciationProfile, fetchPronunciationDueDrills } from '@/lib/api';

type Profile = {
  overallScore: number;
  projectedSpeakingScaled: number;
  projectedSpeakingGrade: string;
  projectedSpeakingPassed: boolean;
  totalAssessments: number;
  weakPhonemes: Array<{ phonemeCode: string; averageScore: number; attemptCount: number }>;
};

type DueDrill = {
  id: string;
  label: string;
  targetPhoneme: string;
  difficulty: string;
};

/**
 * Dashboard pronunciation tile — mounted in the learner dashboard.
 * Surfaces:
 *   - Current projected Speaking band from pronunciation attempts
 *   - Top 3 weakest phonemes
 *   - "Due today" count from the spaced-repetition scheduler
 */
export function PronunciationDashboardTile() {
  const [profile, setProfile] = useState<Profile | null>(null);
  const [due, setDue] = useState<DueDrill[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setLoading(true);
      const [pr, dr] = await Promise.allSettled([
        fetchPronunciationProfile(),
        fetchPronunciationDueDrills(3),
      ]);
      if (cancelled) return;
      if (pr.status === 'fulfilled') setProfile(pr.value as Profile);
      if (dr.status === 'fulfilled') setDue(dr.value as DueDrill[]);
      setLoading(false);
    })();
    return () => { cancelled = true; };
  }, []);

  return (
    <Link
      href="/pronunciation"
      className="group block rounded-3xl border border-border bg-surface p-5 shadow-sm transition hover:border-primary/50 hover:shadow-clinical"
      aria-label="Open pronunciation practice"
    >
      <div className="flex items-start gap-3">
        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-rose-50 text-rose-600 dark:bg-rose-900/30 dark:text-rose-400">
          <Mic className="h-5 w-5" aria-hidden />
        </div>
        <div className="flex-1 min-w-0">
          <div className="text-xs uppercase tracking-[0.15em] text-muted">Learn</div>
          <div className="flex items-center gap-2">
            <h3 className="text-base font-semibold text-navy dark:text-white">Pronunciation</h3>
            <ChevronRight className="h-4 w-4 text-muted transition group-hover:translate-x-0.5" aria-hidden />
          </div>
          {loading ? (
            <div className="mt-2 h-4 w-40 animate-pulse rounded bg-background-light" />
          ) : profile && profile.totalAssessments > 0 ? (
            <>
              <div className="mt-1 text-xs text-muted">
                Projected Speaking band:{' '}
                <span className="font-semibold text-navy dark:text-white">
                  {profile.projectedSpeakingScaled}/500 · Grade {profile.projectedSpeakingGrade}
                </span>
                {profile.projectedSpeakingPassed ? ' · pass' : ' · below pass'}
              </div>
              {profile.weakPhonemes.length > 0 && (
                <div className="mt-2 flex flex-wrap gap-1">
                  {profile.weakPhonemes.slice(0, 3).map((p) => (
                    <span
                      key={p.phonemeCode}
                      className="rounded-full bg-rose-50 px-2 py-0.5 font-mono text-[11px] text-rose-700 dark:bg-rose-900/30 dark:text-rose-300"
                      title={`${p.phonemeCode} · ${Math.round(p.averageScore)}% avg`}
                    >
                      /{p.phonemeCode}/
                    </span>
                  ))}
                </div>
              )}
            </>
          ) : (
            <p className="mt-1 text-xs text-muted">
              Record your first drill to get a phoneme-level AI score and a projected Speaking band.
            </p>
          )}
          {due.length > 0 && (
            <div className="mt-2 text-xs text-primary">
              {due.length} drill{due.length === 1 ? '' : 's'} due today
            </div>
          )}
        </div>
      </div>
    </Link>
  );
}
