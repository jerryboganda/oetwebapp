'use client';

import { useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Lightbulb, ArrowLeft, Clock } from 'lucide-react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchStrategyGuide } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type StrategyGuide = {
  id: string;
  examTypeCode: string;
  subtestCode: string | null;
  title: string;
  summary: string | null;
  contentJson: string | null;
  contentHtml: string | null;
  category: string;
  readingTimeMinutes: number;
  estimatedMinutes?: number;
};
type GuideContent = { overview?: string; tips?: Array<{ title: string; body: string }>; commonMistakes?: string[]; keyTakeaways?: string[] };

export default function StrategyGuidePage() {
  const params = useParams<{ id: string }>();
  const [guide, setGuide] = useState<StrategyGuide | null>(null);
  const [content, setContent] = useState<GuideContent | null>(null);
  const [articleHtml, setArticleHtml] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!params.id) return;
    fetchStrategyGuide(params.id).then(data => {
      const g = data as StrategyGuide;
      setGuide(g);
      if (g.contentJson) {
        try {
          setContent(JSON.parse(g.contentJson));
          setArticleHtml(null);
        } catch {
          setContent(null);
          setArticleHtml(g.contentHtml);
        }
      } else {
        setContent(null);
        setArticleHtml(g.contentHtml);
      }
      setLoading(false);
      analytics.track('strategy_guide_viewed', { guideId: g.id });
    }).catch(() => {
      setError('Could not load strategy guide.');
      setLoading(false);
    });
  }, [params.id]);

  if (loading) {
    return <LearnerDashboardShell><Skeleton className="h-8 w-48 mb-4" /><Skeleton className="h-64 rounded-2xl" /></LearnerDashboardShell>;
  }

  if (!guide) {
    return <LearnerDashboardShell><InlineAlert variant="warning">Strategy guide not found.</InlineAlert></LearnerDashboardShell>;
  }

  return (
    <LearnerDashboardShell>
      <div className="flex items-center gap-3 mb-6">
        <Link href="/strategies" className="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300">
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <div>
          <div className="flex items-center gap-2 text-xs text-gray-400 mb-0.5">
            <Lightbulb className="w-3.5 h-3.5 text-yellow-500" />
            <span className="uppercase font-medium">{guide.examTypeCode}</span>
            {guide.subtestCode && <span className="capitalize">· {guide.subtestCode}</span>}
            <span className="capitalize">· {guide.category.replace(/_/g, ' ')}</span>
            <span className="flex items-center gap-1"><Clock className="w-3 h-3" />{guide.estimatedMinutes ?? guide.readingTimeMinutes} min read</span>
          </div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-white">{guide.title}</h1>
        </div>
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      <div className="max-w-2xl mx-auto space-y-6">
        {guide.summary && (
          <div className="bg-yellow-50 dark:bg-yellow-900/20 border border-yellow-200 dark:border-yellow-800 rounded-xl p-4 text-yellow-800 dark:text-yellow-200 text-sm">
            {guide.summary}
          </div>
        )}

        {content?.overview && (
          <div>
            <h2 className="text-lg font-semibold text-gray-900 dark:text-white mb-2">Overview</h2>
            <p className="text-gray-700 dark:text-gray-300 text-sm leading-relaxed">{content.overview}</p>
          </div>
        )}

        {content?.tips && content.tips.length > 0 && (
          <div>
            <h2 className="text-lg font-semibold text-gray-900 dark:text-white mb-4">Key Strategies</h2>
            <div className="space-y-4">
              {content.tips.map((tip, i) => (
                <MotionItem
                  key={i}
                  delayIndex={i}
                  className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-4 flex gap-4"
                >
                  <div className="flex-shrink-0 w-8 h-8 rounded-full bg-yellow-100 dark:bg-yellow-900/30 flex items-center justify-center text-yellow-700 dark:text-yellow-300 font-bold text-sm">
                    {i + 1}
                  </div>
                  <div>
                    <div className="font-medium text-gray-900 dark:text-white mb-1">{tip.title}</div>
                    <div className="text-sm text-gray-600 dark:text-gray-400">{tip.body}</div>
                  </div>
                </MotionItem>
              ))}
            </div>
          </div>
        )}

        {content?.commonMistakes && content.commonMistakes.length > 0 && (
          <div>
            <h2 className="text-lg font-semibold text-gray-900 dark:text-white mb-3">Common Mistakes to Avoid</h2>
            <div className="bg-red-50 dark:bg-red-900/10 border border-red-200 dark:border-red-800 rounded-xl p-4 space-y-2">
              {content.commonMistakes.map((mistake, i) => (
                <div key={i} className="flex items-start gap-2 text-sm text-red-700 dark:text-red-300">
                  <span className="text-red-400 font-bold flex-shrink-0">✗</span>
                  <span>{mistake}</span>
                </div>
              ))}
            </div>
          </div>
        )}

        {content?.keyTakeaways && content.keyTakeaways.length > 0 && (
          <div>
            <h2 className="text-lg font-semibold text-gray-900 dark:text-white mb-3">Key Takeaways</h2>
            <div className="bg-green-50 dark:bg-green-900/10 border border-green-200 dark:border-green-800 rounded-xl p-4 space-y-2">
              {content.keyTakeaways.map((point, i) => (
                <div key={i} className="flex items-start gap-2 text-sm text-green-700 dark:text-green-300">
                  <span className="text-green-500 flex-shrink-0">✓</span>
                  <span>{point}</span>
                </div>
              ))}
            </div>
          </div>
        )}

        {articleHtml && !content && (
          <div
            className="prose prose-slate dark:prose-invert max-w-none rounded-xl border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 p-6"
            dangerouslySetInnerHTML={{ __html: articleHtml }}
          />
        )}

        {!content && !articleHtml && (
          <div className="text-center py-8 text-gray-400 text-sm">Content for this guide is being prepared.</div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
