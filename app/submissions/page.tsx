'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import {
  FileText,
  Headphones,
  PenTool,
  Mic,
  MessageSquare,
  GitCompare,
  Send,
  Clock,
  CheckCircle2,
  AlertCircle
} from 'lucide-react';
import { AppShell } from '@/components/layout/app-shell';
import { fetchSubmissions } from '@/lib/api';
import type { Submission, SubTest, ReviewStatus } from '@/lib/mock-data';
import { analytics } from '@/lib/analytics';
import { InlineAlert } from '@/components/ui/alert';

const SUBTEST_STYLE: Record<SubTest, { icon: React.ElementType; badge: string }> = {
  Reading:   { icon: FileText,   badge: 'bg-blue-100 text-blue-700' },
  Listening: { icon: Headphones, badge: 'bg-indigo-100 text-indigo-700' },
  Writing:   { icon: PenTool,    badge: 'bg-rose-100 text-rose-700' },
  Speaking:  { icon: Mic,        badge: 'bg-purple-100 text-purple-700' },
};

import React from 'react';

function ReviewBadge({ status }: { status: ReviewStatus }) {
  if (status === 'reviewed') return (
    <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-bold bg-green-100 text-green-700">
      <CheckCircle2 className="w-3.5 h-3.5" /> Reviewed
    </span>
  );
  if (status === 'pending') return (
    <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-bold bg-amber-100 text-amber-700">
      <Clock className="w-3.5 h-3.5" /> Pending
    </span>
  );
  return (
    <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-bold bg-gray-100 text-gray-600">
      <AlertCircle className="w-3.5 h-3.5" /> Not Requested
    </span>
  );
}

export default function SubmissionHistory() {
  const [submissions, setSubmissions] = useState<Submission[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('evaluation_viewed', { type: 'submissions' });
    fetchSubmissions()
      .then(data => { setSubmissions(data); setLoading(false); })
      .catch(() => { setError('Failed to load submissions. Please try again.'); setLoading(false); });
  }, []);

  return (
    <AppShell
      pageTitle="Submission History"
      subtitle="Review your past work and follow up on feedback"
      backHref="/"
    >
      <div className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8 py-8">

        {loading && (
          <div className="space-y-4">
            {[1, 2, 3].map(i => (
              <div key={i} className="h-36 rounded-[24px] bg-gray-100 animate-pulse" />
            ))}
          </div>
        )}

        {!loading && error && (
          <InlineAlert variant="error">{error}</InlineAlert>
        )}

        {!loading && !error && submissions.length === 0 && (
          <div className="text-center text-muted py-24">No submissions yet.</div>
        )}

        {!loading && !error && submissions.length > 0 && (
          <div className="space-y-4">
            {submissions.map((sub, idx) => {
              const meta = SUBTEST_STYLE[sub.subTest] ?? SUBTEST_STYLE.Writing;
              const Icon = meta.icon;
              const canRequest = sub.reviewStatus === 'not_requested';
              return (
                <motion.div
                  key={sub.id}
                  initial={{ opacity: 0, y: 20 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ delay: idx * 0.05 }}
                  className="bg-surface rounded-[24px] border border-gray-200 p-5 sm:p-6 shadow-sm flex flex-col md:flex-row gap-6 justify-between hover:border-gray-300 transition-colors"
                >
                  <div className="flex-1 space-y-4">
                    <div className="flex flex-col sm:flex-row sm:items-start justify-between gap-4">
                      <div>
                        <div className="flex items-center gap-3 mb-2">
                          <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-xs font-black uppercase tracking-widest ${meta.badge}`}>
                            <Icon className="w-4 h-4" />
                            {sub.subTest}
                          </span>
                          <span className="text-sm text-muted font-medium">{sub.attemptDate}</span>
                        </div>
                        <h2 className="text-lg font-bold text-navy leading-tight">{sub.taskName}</h2>
                      </div>
                      <div className="sm:text-right bg-background-light sm:bg-transparent p-3 sm:p-0 rounded-xl border border-gray-100 sm:border-none">
                        <div className="text-xs font-bold text-muted uppercase tracking-widest mb-1">Score Estimate</div>
                        <div className={`text-xl font-black ${sub.scoreEstimate === 'Pending' ? 'text-muted' : 'text-navy'}`}>
                          {sub.scoreEstimate}
                        </div>
                      </div>
                    </div>
                    <div className="flex items-center gap-3 pt-2 border-t border-gray-50">
                      <span className="text-sm font-medium text-muted">Review Status:</span>
                      <ReviewBadge status={sub.reviewStatus} />
                    </div>
                  </div>

                  <div className="flex flex-col gap-2 md:w-48 shrink-0 border-t md:border-t-0 md:border-l border-gray-100 pt-5 md:pt-0 md:pl-6 justify-center">
                    <button className="w-full flex items-center justify-center gap-2 px-4 py-2.5 bg-white border border-gray-200 text-navy text-sm font-bold rounded-xl hover:bg-background-light transition-colors">
                      <MessageSquare className="w-4 h-4" />
                      Reopen Feedback
                    </button>
                    <button className="w-full flex items-center justify-center gap-2 px-4 py-2.5 bg-white border border-gray-200 text-navy text-sm font-bold rounded-xl hover:bg-background-light transition-colors">
                      <GitCompare className="w-4 h-4" />
                      Compare Attempts
                    </button>
                    <button
                      disabled={!canRequest}
                      className="w-full flex items-center justify-center gap-2 px-4 py-2.5 bg-navy text-white text-sm font-bold rounded-xl hover:bg-navy/90 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                      <Send className="w-4 h-4" />
                      Request Review
                    </button>
                  </div>
                </motion.div>
              );
            })}
          </div>
        )}

      </div>
    </AppShell>
  );
}

