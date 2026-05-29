'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { BookOpen, Headphones, Mic, PenLine } from 'lucide-react';
import { AnimatePresence, motion } from 'motion/react';
import { apiClient } from '@/lib/api';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';

interface ReadinessBlocker {
  subtestCode: string;
  criterionCode: string;
  currentScore: number;
  targetScore: number;
  gap: number;
  recommendation: string;
  actionRoute: string;
}

interface LearnerReadinessBlockersResponse {
  blockers: ReadinessBlocker[];
  overallReadiness: number;
  generatedAt: string;
}

const subtestIcons: Record<string, typeof BookOpen> = {
  reading: BookOpen,
  listening: Headphones,
  writing: PenLine,
  speaking: Mic,
};

function getReadinessColor(readiness: number) {
  if (readiness >= 70) return { ring: 'text-emerald-500', bg: 'stroke-emerald-500', label: 'text-emerald-700 dark:text-emerald-400' };
  if (readiness >= 40) return { ring: 'text-amber-500', bg: 'stroke-amber-500', label: 'text-amber-700 dark:text-amber-400' };
  return { ring: 'text-red-500', bg: 'stroke-red-500', label: 'text-red-700 dark:text-red-400' };
}

function CircularProgress({ value }: { value: number }) {
  const radius = 36;
  const circumference = 2 * Math.PI * radius;
  const offset = circumference - (value / 100) * circumference;
  const colors = getReadinessColor(value);

  return (
    <div className="relative flex h-24 w-24 items-center justify-center">
      <svg className="h-24 w-24 -rotate-90" viewBox="0 0 80 80">
        <circle
          cx="40"
          cy="40"
          r={radius}
          fill="none"
          strokeWidth="6"
          className="stroke-border"
        />
        <circle
          cx="40"
          cy="40"
          r={radius}
          fill="none"
          strokeWidth="6"
          strokeLinecap="round"
          strokeDasharray={circumference}
          strokeDashoffset={offset}
          className={colors.bg}
        />
      </svg>
      <span className={`absolute text-lg font-bold ${colors.label}`}>
        {value}%
      </span>
    </div>
  );
}

export function ReadinessBlockers() {
  const [data, setData] = useState<LearnerReadinessBlockersResponse | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    apiClient
      .get<LearnerReadinessBlockersResponse>('/v1/learner/readiness/blockers')
      .then((res) => {
        if (!cancelled) setData(res);
      })
      .catch(() => {
        if (!cancelled) setData(null);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, []);

  if (loading) {
    return (
      <Card padding="md">
        <div className="flex items-center gap-6">
          <Skeleton variant="circle" className="h-24 w-24 shrink-0" />
          <div className="flex-1 space-y-3">
            <Skeleton className="h-4 w-full" />
            <Skeleton className="h-4 w-5/6" />
            <Skeleton className="h-4 w-4/6" />
          </div>
        </div>
      </Card>
    );
  }

  if (!data) {
    return (
      <Card padding="md">
        <p className="text-sm text-muted text-center py-4">Unable to load readiness data.</p>
      </Card>
    );
  }

  const topBlockers = data.blockers.slice(0, 3);

  return (
    <AnimatePresence mode="wait">
      <motion.div
        initial={{ opacity: 0, y: 8 }}
        animate={{ opacity: 1, y: 0 }}
        exit={{ opacity: 0, y: -8 }}
        transition={{ duration: 0.2 }}
      >
        <Card padding="md">
          <div className="flex flex-col gap-5 sm:flex-row sm:items-start">
            <div className="flex shrink-0 justify-center">
              <CircularProgress value={data.overallReadiness} />
            </div>
            <div className="min-w-0 flex-1">
              <h3 className="text-base font-semibold text-navy">Exam Readiness</h3>
              {topBlockers.length === 0 ? (
                <p className="mt-2 text-sm text-muted">No blockers. You&apos;re on track!</p>
              ) : (
                <ul className="mt-3 space-y-3">
                  {topBlockers.map((blocker) => {
                    const Icon = subtestIcons[blocker.subtestCode.toLowerCase()] ?? BookOpen;
                    return (
                      <li key={`${blocker.subtestCode}-${blocker.criterionCode}`} className="flex items-start gap-3">
                        <Icon className="mt-0.5 h-4 w-4 shrink-0 text-muted" />
                        <div className="min-w-0 flex-1">
                          <p className="text-sm text-navy leading-snug">{blocker.recommendation}</p>
                        </div>
                        <Button asChild variant="ghost" size="sm" className="shrink-0">
                          <Link href={blocker.actionRoute}>
                            Fix it
                          </Link>
                        </Button>
                      </li>
                    );
                  })}
                </ul>
              )}
            </div>
          </div>
        </Card>
      </motion.div>
    </AnimatePresence>
  );
}
