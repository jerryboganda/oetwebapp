'use client';

import { useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Mic, ArrowLeft, Volume2, Play } from 'lucide-react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchMyPronunciationProgress, fetchPronunciationDrill } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { sanitizeRichHtml } from '@/lib/sanitize-html';

type PronunciationDrill = {
  id: string;
  targetPhoneme: string;
  label: string;
  exampleWordsJson: string;
  minimalPairsJson: string;
  sentencesJson: string;
  audioModelUrl: string | null;
  tipsHtml: string;
  difficulty: string;
};

type PronunciationProgress = {
  phonemeCode: string;
  averageScore: number;
  attemptCount: number;
  lastPracticedAt: string | null;
};

type MinimalPair = { a: string; b: string };

function parseStringArray(value: string | null | undefined): string[] {
  if (!value) return [];

  try {
    const parsed = JSON.parse(value) as unknown;
    return Array.isArray(parsed) ? parsed.filter((item): item is string => typeof item === 'string') : [];
  } catch {
    return [];
  }
}

function parseMinimalPairs(value: string | null | undefined): MinimalPair[] {
  if (!value) return [];

  try {
    const parsed = JSON.parse(value) as unknown;
    if (!Array.isArray(parsed)) return [];

    return parsed.filter((pair): pair is MinimalPair => {
      if (!pair || typeof pair !== 'object') return false;
      const item = pair as Record<string, unknown>;
      return typeof item.a === 'string' && typeof item.b === 'string';
    });
  } catch {
    return [];
  }
}

export default function PronunciationDrillPage() {
  const params = useParams<{ drillId: string }>();
  const [drill, setDrill] = useState<PronunciationDrill | null>(null);
  const [progressMap, setProgressMap] = useState<Record<string, PronunciationProgress>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!params.drillId) return;

    let cancelled = false;

    (async () => {
      setLoading(true);
      setError(null);

      const [drillResult, progressResult] = await Promise.allSettled([
        fetchPronunciationDrill(params.drillId),
        fetchMyPronunciationProgress(),
      ]);

      if (cancelled) return;

      if (drillResult.status === 'fulfilled') {
        const loadedDrill = drillResult.value as PronunciationDrill;
        setDrill(loadedDrill);
        analytics.track('pronunciation_drill_viewed', { drillId: loadedDrill.id });
      } else {
        setError('Could not load drill.');
      }

      if (progressResult.status === 'fulfilled') {
        const items = progressResult.value as PronunciationProgress[];
        setProgressMap(Object.fromEntries(items.map(item => [item.phonemeCode, item])));
      }

      setLoading(false);
    })();

    return () => {
      cancelled = true;
    };
  }, [params.drillId]);

  if (loading) {
    return (
      <LearnerDashboardShell>
        <Skeleton className="h-8 w-48 mb-4" />
        <Skeleton className="h-64 rounded-2xl" />
      </LearnerDashboardShell>
    );
  }

  if (!drill) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="warning">{error ?? 'Drill not found.'}</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  const exampleWords = parseStringArray(drill.exampleWordsJson);
  const minimalPairs = parseMinimalPairs(drill.minimalPairsJson);
  const sentences = parseStringArray(drill.sentencesJson);
  const progress = progressMap[drill.targetPhoneme] ?? null;

  return (
    <LearnerDashboardShell>
      <div className="flex items-center gap-3 mb-6">
        <Link href="/pronunciation" className="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300">
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <div>
          <div className="flex items-center gap-2 text-xs text-gray-400 mb-0.5">
            <Mic className="w-3.5 h-3.5 text-rose-500" />
            <span className="capitalize">{drill.difficulty}</span>
            <span className="font-mono bg-gray-100 dark:bg-gray-700 px-1.5 rounded text-gray-600 dark:text-gray-300">{drill.targetPhoneme}</span>
          </div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-white">{drill.label}</h1>
        </div>
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      <div className="max-w-2xl mx-auto space-y-6">
        {progress && (
          <div className="grid grid-cols-3 gap-3 rounded-xl border border-gray-200 bg-white p-4 text-center shadow-sm dark:border-gray-700 dark:bg-gray-800">
            <div>
              <div className="text-[11px] uppercase tracking-[0.18em] text-gray-400">Average score</div>
              <div className="mt-1 text-lg font-semibold text-gray-900 dark:text-white">{Math.round(progress.averageScore)}%</div>
            </div>
            <div>
              <div className="text-[11px] uppercase tracking-[0.18em] text-gray-400">Attempts</div>
              <div className="mt-1 text-lg font-semibold text-gray-900 dark:text-white">{progress.attemptCount}</div>
            </div>
            <div>
              <div className="text-[11px] uppercase tracking-[0.18em] text-gray-400">Last practiced</div>
              <div className="mt-1 text-xs font-medium text-gray-600 dark:text-gray-300">
                {progress.lastPracticedAt ? new Date(progress.lastPracticedAt).toLocaleDateString() : 'Not yet'}
              </div>
            </div>
          </div>
        )}

        {drill.audioModelUrl && (
          <div className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-4 flex items-center gap-4">
            <div className="p-3 bg-rose-100 dark:bg-rose-900/30 rounded-lg">
              <Volume2 className="w-6 h-6 text-rose-600 dark:text-rose-400" />
            </div>
            <div className="flex-1">
              <div className="text-sm font-medium text-gray-700 dark:text-gray-300">Model audio</div>
              <audio controls src={drill.audioModelUrl} className="mt-2 w-full h-8" />
            </div>
          </div>
        )}

        {exampleWords.length > 0 && (
          <div>
            <h2 className="text-lg font-semibold text-gray-900 dark:text-white mb-3">Example words</h2>
            <div className="flex flex-wrap gap-2">
              {exampleWords.map((word, i) => (
                <MotionItem
                  key={`${word}-${i}`}
                  delayIndex={i}
                  className="rounded-lg border border-gray-200 bg-white px-3 py-1.5 text-sm font-medium text-gray-800 shadow-sm dark:border-gray-700 dark:bg-gray-800 dark:text-gray-200"
                >
                  {word}
                </MotionItem>
              ))}
            </div>
          </div>
        )}

        {minimalPairs.length > 0 && (
          <div>
            <h2 className="text-lg font-semibold text-gray-900 dark:text-white mb-3">Minimal pairs</h2>
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
              {minimalPairs.map((pair, i) => (
                <div key={`${pair.a}-${pair.b}-${i}`} className="flex items-center gap-2 rounded-lg border border-gray-200 bg-white p-3 text-sm shadow-sm dark:border-gray-700 dark:bg-gray-800">
                  <span className="font-medium text-rose-600 dark:text-rose-400">{pair.a}</span>
                  <span className="text-gray-400">vs</span>
                  <span className="font-medium text-blue-600 dark:text-blue-400">{pair.b}</span>
                </div>
              ))}
            </div>
          </div>
        )}

        {sentences.length > 0 && (
          <div>
            <h2 className="text-lg font-semibold text-gray-900 dark:text-white mb-3">Practice sentences</h2>
            <div className="space-y-2">
              {sentences.map((sentence, i) => (
                <div key={`${sentence}-${i}`} className="rounded-lg border border-gray-200 bg-white p-3 text-sm italic text-gray-700 shadow-sm dark:border-gray-700 dark:bg-gray-800 dark:text-gray-300">
                  {sentence}
                </div>
              ))}
            </div>
          </div>
        )}

        {drill.tipsHtml && (
          <div className="rounded-2xl border border-rose-200 bg-rose-50/80 p-5 shadow-sm dark:border-rose-900/40 dark:bg-rose-950/20">
            <div className="mb-3 flex items-center gap-2 text-sm font-semibold text-rose-800 dark:text-rose-200">
              <Play className="h-4 w-4" />
              Pronunciation tips
            </div>
            <div className="prose prose-sm max-w-none text-rose-950 prose-p:my-2 prose-strong:text-rose-900 dark:prose-invert dark:text-rose-100" dangerouslySetInnerHTML={{ __html: sanitizeRichHtml(drill.tipsHtml) }} />
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
