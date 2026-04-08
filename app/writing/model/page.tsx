'use client';

import { useState, useEffect } from 'react';
import {
  ChevronLeft,
  CheckCircle2,
  XCircle,
  BookOpen,
  Stethoscope,
  Target,
  FileCheck,
} from 'lucide-react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { LearnerDashboardShell } from '@/components/layout';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchModelAnswer } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { ModelAnswer } from '@/lib/mock-data';

export default function ModelAnswerExplainer() {
  const searchParams = useSearchParams();
  const taskId = searchParams?.get('taskId') ?? 'wt-001';
  const [model, setModel] = useState<ModelAnswer | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    analytics.track('content_view', { content: 'model_answer', taskId, subtest: 'writing' });
    fetchModelAnswer(taskId).then(setModel).finally(() => setLoading(false));
  }, [taskId]);

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Model Answer Explainer">
        <div className="space-y-6">
          <Skeleton className="h-32 rounded-2xl" />
          {[1, 2, 3].map(i => <Skeleton key={i} className="h-48 rounded-2xl" />)}
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!model) return <LearnerDashboardShell pageTitle="Not Found"><div className="p-10 text-center text-muted">Model answer not found.</div></LearnerDashboardShell>;

  return (
    <LearnerDashboardShell pageTitle="Model Answer Explainer">
      {/* Sticky header */}
      <header className="bg-white border-b border-gray-200 sticky top-0 z-20 px-4 sm:px-6 py-4 flex items-center justify-between shadow-sm">
        <div className="flex items-center gap-4">
          <Link href={`/writing/result?id=wr-001`} className="text-gray-500 hover:text-navy transition-colors"><ChevronLeft className="w-5 h-5" /></Link>
          <div>
            <h1 className="font-bold text-lg text-navy leading-tight flex items-center gap-2">
              <FileCheck className="w-5 h-5 text-primary" /> Model Answer Explainer
            </h1>
            <div className="text-xs text-muted font-medium flex items-center gap-2 mt-0.5">
              <Badge variant="info" size="sm">{model.profession}</Badge>
              <span>{model.taskTitle}</span>
            </div>
          </div>
        </div>
      </header>

      <main className="py-8">
        {/* Intro Banner */}
        <MotionSection className="bg-gradient-to-br from-navy to-primary/80 rounded-2xl p-6 sm:p-8 mb-8 text-white shadow-md">
          <h2 className="text-2xl font-bold mb-3">Why this is a strong response</h2>
          <p className="text-blue-100 leading-relaxed max-w-3xl">
            This model answer demonstrates a high-scoring response. Below, the letter is broken down paragraph by paragraph. Review the annotations to understand the <strong>rationale</strong>, <strong>scoring criteria</strong>, <strong>included / excluded details</strong>, and <strong>profession-specific language</strong> choices.
          </p>
        </MotionSection>

        {/* Paragraph Cards */}
        <div className="space-y-12">
          {model.paragraphs.map((paragraph, index) => (
            <MotionItem key={paragraph.id} delayIndex={index} className="flex flex-col lg:flex-row gap-6 lg:gap-8">

              {/* Left: Paragraph text */}
              <div className="w-full lg:w-5/12 shrink-0">
                <div className="sticky top-24">
                  <Card className="p-6 relative">
                    <div className="absolute -left-3 -top-3 w-8 h-8 bg-primary text-white rounded-full flex items-center justify-center font-bold text-sm shadow-sm border-2 border-white">{index + 1}</div>
                    <p className="text-lg leading-relaxed text-gray-800 font-serif">{paragraph.text}</p>
                  </Card>
                </div>
              </div>

              {/* Right: Annotations */}
              <div className="w-full lg:w-7/12 space-y-4">
                {/* Rationale */}
                <Card className="p-5">
                  <div className="flex flex-wrap items-center justify-between gap-4 mb-3">
                    <h3 className="text-sm font-bold text-navy uppercase tracking-wider flex items-center gap-2"><BookOpen className="w-4 h-4 text-primary" /> Rationale</h3>
                    <div className="flex flex-wrap gap-2">
                      {paragraph.criteria.map(crit => (
                        <Badge key={crit} variant="muted" size="sm"><Target className="w-3 h-3 mr-1 inline" />{crit}</Badge>
                      ))}
                    </div>
                  </div>
                  <p className="text-gray-700 leading-relaxed text-sm">{paragraph.rationale}</p>
                </Card>

                {/* Include / Exclude */}
                <Card className="p-5">
                  <h3 className="text-sm font-bold text-navy uppercase tracking-wider mb-4">Include / Exclude Logic</h3>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <div>
                      <h4 className="text-xs font-bold text-green-700 uppercase tracking-wider mb-2 flex items-center gap-1.5"><CheckCircle2 className="w-4 h-4" /> Included</h4>
                      <ul className="space-y-2">{paragraph.included.map((item, i) => (<li key={i} className="text-sm text-gray-700 flex items-start gap-2"><span className="w-1.5 h-1.5 rounded-full bg-green-500 mt-1.5 shrink-0" /><span className="leading-snug">{item}</span></li>))}</ul>
                    </div>
                    {paragraph.excluded.length > 0 && (
                      <div>
                        <h4 className="text-xs font-bold text-red-700 uppercase tracking-wider mb-2 flex items-center gap-1.5"><XCircle className="w-4 h-4" /> Excluded</h4>
                        <ul className="space-y-2">{paragraph.excluded.map((item, i) => (<li key={i} className="text-sm text-gray-700 flex items-start gap-2"><span className="w-1.5 h-1.5 rounded-full bg-red-400 mt-1.5 shrink-0" /><span className="leading-snug">{item}</span></li>))}</ul>
                      </div>
                    )}
                  </div>
                </Card>

                {/* Language Notes */}
                <div className="bg-blue-50 rounded-2xl border border-blue-100 p-5 shadow-sm">
                  <h3 className="text-sm font-bold text-blue-900 uppercase tracking-wider mb-2 flex items-center gap-2"><Stethoscope className="w-4 h-4" /> {model.profession} Language Notes</h3>
                  <p className="text-blue-800 leading-relaxed text-sm">{paragraph.languageNotes}</p>
                </div>
              </div>
            </MotionItem>
          ))}
        </div>
      </main>
    </LearnerDashboardShell>
  );
}
