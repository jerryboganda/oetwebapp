'use client';

import { useState, useEffect } from 'react';
import { motion } from 'motion/react';
import { FileText, BarChart3, ShieldAlert, ThumbsUp, AlertTriangle, ArrowRight, Edit3, Star, Info } from 'lucide-react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { AppShell } from '@/components/layout/app-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchWritingResult } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { WritingResult } from '@/lib/mock-data';

export default function WritingResultSummary() {
  const searchParams = useSearchParams();
  const resultId = searchParams?.get('id') ?? 'wr-001';
  const [result, setResult] = useState<WritingResult | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;

    const poll = async () => {
      try {
        const response = await fetchWritingResult(resultId);
        if (cancelled) return;
        if (response.evalStatus === 'completed') {
          analytics.track('evaluation_viewed', { resultId, subtest: 'writing' });
          setResult(response);
          setLoading(false);
          return;
        }
        setTimeout(() => { void poll(); }, 2000);
      } catch {
        if (!cancelled) {
          setLoading(false);
        }
      }
    };

    void poll();

    return () => { cancelled = true; };
  }, [resultId]);

  if (loading) {
    return (
      <AppShell pageTitle="Evaluation Summary">
        <div className="max-w-4xl mx-auto px-4 py-8 space-y-6">
          <Skeleton className="h-40 rounded-2xl" />
          <Skeleton className="h-32 rounded-2xl" />
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <Skeleton className="h-48 rounded-2xl" />
            <Skeleton className="h-48 rounded-2xl" />
          </div>
        </div>
      </AppShell>
    );
  }

  if (!result) return <AppShell pageTitle="Not Found"><div className="p-10 text-center text-muted">Result not found.</div></AppShell>;

  const confidenceColor = result.confidenceLabel === 'High' ? 'success' : result.confidenceLabel === 'Medium' ? 'warning' : 'danger';

  return (
    <AppShell pageTitle="Evaluation Summary">
      <header className="bg-navy text-white pt-10 pb-24 px-4 sm:px-6 lg:px-8 relative overflow-hidden">
        <div className="absolute inset-0 opacity-10 bg-[radial-gradient(circle_at_top_right,_var(--tw-gradient-stops))] from-blue-400 via-navy to-navy"></div>
        <div className="max-w-4xl mx-auto relative z-10 text-center">
          <div className="inline-flex items-center justify-center w-12 h-12 rounded-xl bg-white/10 mb-6">
            <FileText className="w-6 h-6 text-blue-300" />
          </div>
          <h1 className="text-3xl sm:text-4xl font-bold mb-4">Evaluation Summary</h1>
          <p className="text-gray-300 text-lg max-w-2xl mx-auto">{result.taskTitle}</p>
        </div>
      </header>

      <main className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8 -mt-16 relative z-20">
        {/* Disclaimer */}
        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} className="bg-blue-50 border border-blue-200 rounded-xl p-4 mb-6 flex items-start gap-3 shadow-sm">
          <Info className="w-5 h-5 text-blue-600 shrink-0 mt-0.5" />
          <p className="text-sm text-blue-800 leading-relaxed">
            <strong>AI-Generated Estimate:</strong> The scores below are estimates provided by our AI evaluation engine for directional feedback only.
          </p>
        </motion.div>

        {/* Main Score Card */}
        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.1 }}>
          <Card className="p-8 mb-8 text-center">
            <div className="grid grid-cols-1 md:grid-cols-3 gap-8 divide-y md:divide-y-0 md:divide-x divide-gray-100">
              <div className="flex flex-col items-center justify-center pt-4 md:pt-0">
                <div className="text-sm font-bold text-muted uppercase tracking-wider mb-2 flex items-center gap-1.5"><BarChart3 className="w-4 h-4" /> Estimated Score</div>
                <div className="text-4xl sm:text-5xl font-black text-navy tracking-tight">{result.estimatedScoreRange}</div>
              </div>
              <div className="flex flex-col items-center justify-center pt-8 md:pt-0">
                <div className="text-sm font-bold text-muted uppercase tracking-wider mb-2">Estimated Grade</div>
                <div className="text-4xl sm:text-5xl font-black text-primary tracking-tight">{result.estimatedGradeRange}</div>
              </div>
              <div className="flex flex-col items-center justify-center pt-8 md:pt-0">
                <div className="text-sm font-bold text-muted uppercase tracking-wider mb-2 flex items-center gap-1.5"><ShieldAlert className="w-4 h-4" /> AI Confidence</div>
                <Badge variant={confidenceColor} size="sm">{result.confidenceLabel} Confidence</Badge>
              </div>
            </div>
          </Card>
        </motion.div>

        {/* Strengths & Issues */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6 mb-8">
          <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.2 }}>
            <Card className="p-6">
              <div className="flex items-center gap-2 mb-6">
                <div className="w-8 h-8 rounded-full bg-green-100 flex items-center justify-center shrink-0"><ThumbsUp className="w-4 h-4 text-green-600" /></div>
                <h2 className="text-lg font-bold text-navy">Top Strengths</h2>
              </div>
              <ul className="space-y-4">
                {result.topStrengths.map((s, i) => (
                  <li key={i} className="flex items-start gap-3 text-gray-700"><span className="w-1.5 h-1.5 rounded-full bg-green-500 mt-2 shrink-0"></span><span className="leading-relaxed">{s}</span></li>
                ))}
              </ul>
            </Card>
          </motion.div>
          <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.3 }}>
            <Card className="p-6">
              <div className="flex items-center gap-2 mb-6">
                <div className="w-8 h-8 rounded-full bg-amber-100 flex items-center justify-center shrink-0"><AlertTriangle className="w-4 h-4 text-amber-600" /></div>
                <h2 className="text-lg font-bold text-navy">Top Issues to Fix</h2>
              </div>
              <ul className="space-y-4">
                {result.topIssues.map((s, i) => (
                  <li key={i} className="flex items-start gap-3 text-gray-700"><span className="w-1.5 h-1.5 rounded-full bg-amber-500 mt-2 shrink-0"></span><span className="leading-relaxed">{s}</span></li>
                ))}
              </ul>
            </Card>
          </motion.div>
        </div>

        {/* Next Actions CTAs */}
        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.4 }} className="grid grid-cols-1 sm:grid-cols-3 gap-4 pb-8">
          <Link href={`/writing/feedback?id=${resultId}`} className="group bg-primary hover:bg-primary/90 text-white rounded-xl p-4 flex flex-col items-center justify-center text-center transition-all shadow-sm hover:shadow-md">
            <BarChart3 className="w-6 h-6 mb-2 opacity-80 group-hover:opacity-100 transition-opacity" />
            <span className="font-bold">View Detailed Feedback</span>
            <span className="text-xs text-blue-100 mt-1">See criterion breakdown</span>
          </Link>
          <Link href={`/writing/player?taskId=${result.taskId}`} className="group bg-white border-2 border-gray-200 hover:border-primary text-navy rounded-xl p-4 flex flex-col items-center justify-center text-center transition-all shadow-sm hover:shadow-md">
            <Edit3 className="w-6 h-6 mb-2 text-gray-400 group-hover:text-primary transition-colors" />
            <span className="font-bold group-hover:text-primary transition-colors">Revise Submission</span>
            <span className="text-xs text-muted mt-1">Try improving your response</span>
          </Link>
          <Link href={`/writing/expert-request?id=${resultId}`} className="group bg-white border-2 border-gray-200 hover:border-amber-400 text-navy rounded-xl p-4 flex flex-col items-center justify-center text-center transition-all shadow-sm hover:shadow-md">
            <Star className="w-6 h-6 mb-2 text-gray-400 group-hover:text-amber-500 transition-colors" />
            <span className="font-bold group-hover:text-amber-600 transition-colors">Request Expert Review</span>
            <span className="text-xs text-muted mt-1">Get human feedback</span>
          </Link>
        </motion.div>
      </main>
    </AppShell>
  );
}
