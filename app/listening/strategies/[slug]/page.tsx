'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';

interface StrategyDetail {
  id: string;
  slug: string;
  title: string;
  category: string;
  applicableParts: string[];
  estimatedReadMinutes: number;
  bodyMarkdownEn: string;
  videoUrl: string | null;
  audioUrl: string | null;
  progress?: {
    markedAsRead: boolean;
    favorited: boolean;
  };
}

export default function ListeningStrategyDetailPage() {
  const params = useParams<{ slug: string }>();
  const slug = params?.slug ?? '';
  const [strategy, setStrategy] = useState<StrategyDetail | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    fetch(`/v1/listening-pathway/strategies/${encodeURIComponent(slug)}`)
      .then((res) => (res.ok ? res.json() : null))
      .then((d: StrategyDetail | null) => {
        if (!cancelled) {
          setStrategy(d);
          setLoading(false);
        }
      })
      .catch(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [slug]);

  async function markRead() {
    if (!strategy) return;
    await fetch(`/v1/listening-pathway/strategies/${strategy.id}/mark-read`, {
      method: 'POST',
    });
    setStrategy({ ...strategy, progress: { ...strategy.progress!, markedAsRead: true } });
  }

  async function toggleFavorite() {
    if (!strategy) return;
    await fetch(`/v1/listening-pathway/strategies/${strategy.id}/favorite`, {
      method: 'POST',
    });
    setStrategy({
      ...strategy,
      progress: { ...strategy.progress!, favorited: !strategy.progress?.favorited },
    });
  }

  if (loading) {
    return (
      <main className="mx-auto max-w-3xl px-4 py-12">
        <p className="text-slate-500">Loading strategy…</p>
      </main>
    );
  }

  if (!strategy) {
    return (
      <main className="mx-auto max-w-3xl px-4 py-12 space-y-4">
        <h1 className="text-2xl font-bold">Strategy not found</h1>
        <Link href="/listening/strategies" className="rounded-md bg-slate-900 px-4 py-2 text-white text-sm inline-block">
          Back to library
        </Link>
      </main>
    );
  }

  return (
    <main className="mx-auto max-w-3xl px-4 py-12 space-y-6">
      <header>
        <span className="text-xs uppercase tracking-wide text-slate-500">
          {strategy.category.replace('_', ' ')}
        </span>
        <h1 className="text-3xl font-bold tracking-tight">{strategy.title}</h1>
        <p className="mt-1 text-sm text-slate-500">~{strategy.estimatedReadMinutes} min read</p>
      </header>

      <article className="prose prose-slate max-w-none rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
        <pre className="whitespace-pre-wrap font-sans">{strategy.bodyMarkdownEn}</pre>
      </article>

      <div className="flex gap-3 text-sm">
        <button
          onClick={markRead}
          disabled={strategy.progress?.markedAsRead}
          className="rounded-md bg-slate-900 px-4 py-2 text-white disabled:opacity-60"
        >
          {strategy.progress?.markedAsRead ? '✓ Marked as read' : 'Mark as read'}
        </button>
        <button
          onClick={toggleFavorite}
          className="rounded-md border border-slate-300 px-4 py-2 text-slate-700"
        >
          {strategy.progress?.favorited ? '⭐ Favorited' : '☆ Favorite'}
        </button>
      </div>

      <Link href="/listening/strategies" className="text-sm text-blue-700 underline">
        ← All strategies
      </Link>
    </main>
  );
}
