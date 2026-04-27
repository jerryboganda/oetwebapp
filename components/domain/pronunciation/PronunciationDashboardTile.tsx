'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { Mic, ChevronRight } from 'lucide-react';
import { fetchPronunciationProfile, fetchPronunciationDueDrills } from '@/lib/api';
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card';

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
 * Uses the canonical DESIGN.md Card primitive (rounded-2xl, border + shadow,
 * Surface White, Manrope, navy headings, lavender icon chip).
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
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <Link
      href="/pronunciation"
      aria-label="Open pronunciation practice"
      className="group block rounded-2xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
    >
      <Card hoverable padding="md" className="h-full">
        <CardHeader className="mb-3 flex items-center justify-between gap-3">
          <div className="flex items-center gap-3 min-w-0">
            <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-lavender text-primary">
              <Mic className="h-4.5 w-4.5" aria-hidden />
            </span>
            <CardTitle className="truncate">Pronunciation</CardTitle>
          </div>
          <ChevronRight
            className="h-4 w-4 shrink-0 text-muted transition group-hover:translate-x-0.5 group-hover:text-primary"
            aria-hidden
          />
        </CardHeader>

        <CardContent className="space-y-2">
          {loading ? (
            <div className="h-4 w-40 animate-pulse rounded bg-background-light" />
          ) : profile && profile.totalAssessments > 0 ? (
            <>
              <p className="text-sm text-muted">
                Projected Speaking band:{' '}
                <span className="font-semibold text-navy">
                  {profile.projectedSpeakingScaled}/500 · Grade {profile.projectedSpeakingGrade}
                </span>
                {profile.projectedSpeakingPassed ? ' · pass' : ' · below pass'}
              </p>
              {profile.weakPhonemes.length > 0 && (
                <div className="flex flex-wrap gap-1.5 pt-1">
                  {profile.weakPhonemes.slice(0, 3).map((p) => (
                    <span
                      key={p.phonemeCode}
                      className="rounded-full bg-rose-50 px-2 py-0.5 font-mono text-[11px] text-rose-700"
                      title={`${p.phonemeCode} · ${Math.round(p.averageScore)}% avg`}
                    >
                      /{p.phonemeCode}/
                    </span>
                  ))}
                </div>
              )}
            </>
          ) : (
            <p className="text-sm text-muted">
              Record your first drill to get a phoneme-level AI score and a projected Speaking band.
            </p>
          )}
          {due.length > 0 && (
            <p className="pt-1 text-xs font-medium text-primary">
              {due.length} drill{due.length === 1 ? '' : 's'} due today
            </p>
          )}
        </CardContent>
      </Card>
    </Link>
  );
}
