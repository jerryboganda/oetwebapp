'use client';

import { useState } from 'react';
import { ClipboardCheck, FileText, Clock, MapPin, CheckCircle2, Circle, BookOpen, AlertTriangle, ArrowRight } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { CardLink } from '@/components/ui/card-link';
import { ProgressBar } from '@/components/ui/progress';
import { analytics } from '@/lib/analytics';
import type { LucideIcon } from 'lucide-react';

interface ChecklistItem {
  id: string;
  label: string;
  description: string;
  category: 'documents' | 'logistics' | 'preparation' | 'on_the_day';
}

const CHECKLIST: ChecklistItem[] = [
  // Documents
  { id: 'd1', label: 'Valid photo ID (passport preferred)', description: 'Must match your OET registration name exactly.', category: 'documents' },
  { id: 'd2', label: 'OET confirmation email printed', description: 'Bring a printout or have it accessible on your phone.', category: 'documents' },
  { id: 'd3', label: 'Candidate number noted', description: 'You\'ll need this to check in at the test centre.', category: 'documents' },
  // Logistics
  { id: 'l1', label: 'Test centre address confirmed', description: 'Check the venue location and plan your transport the day before.', category: 'logistics' },
  { id: 'l2', label: 'Travel route and timing', description: 'Arrive at least 30 minutes early. Account for traffic.', category: 'logistics' },
  { id: 'l3', label: 'Accommodation booked (if travelling)', description: 'Ensure a good night\'s sleep before test day.', category: 'logistics' },
  // Preparation
  { id: 'p1', label: 'Review top writing templates', description: 'Refresh referral letter structures and common phrases.', category: 'preparation' },
  { id: 'p2', label: 'Practice one timed writing task', description: 'Keep it to 45 minutes to build confidence.', category: 'preparation' },
  { id: 'p3', label: 'Listen to 2-3 practice recordings', description: 'Tune your ear to different accents.', category: 'preparation' },
  { id: 'p4', label: 'Review common clinical vocabulary', description: 'Focus on your profession\'s frequently tested terms.', category: 'preparation' },
  // On the day
  { id: 'o1', label: 'Light, balanced breakfast', description: 'Avoid heavy meals. Stay hydrated.', category: 'on_the_day' },
  { id: 'o2', label: 'Arrive 30 minutes early', description: 'Registration can take time. Don\'t add stress.', category: 'on_the_day' },
  { id: 'o3', label: 'Bring water (clear bottle) and snacks', description: 'Most centres allow clear water bottles.', category: 'on_the_day' },
  { id: 'o4', label: 'Read all instructions carefully', description: 'Don\'t rush through the rubric. Note word limits and task requirements.', category: 'on_the_day' },
];

const CATEGORY_META: Record<ChecklistItem['category'], { label: string; icon: LucideIcon }> = {
  documents: { label: 'Documents', icon: FileText },
  logistics: { label: 'Logistics', icon: MapPin },
  preparation: { label: 'Final Preparation', icon: BookOpen },
  on_the_day: { label: 'On the Day', icon: Clock },
};

export default function TestDayPrepPage() {
  const [checked, setChecked] = useState<Set<string>>(new Set());

  function toggleItem(id: string) {
    setChecked((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
    analytics.track('test_day_checklist_toggle', { itemId: id });
  }

  const categories = ['documents', 'logistics', 'preparation', 'on_the_day'] as const;
  const totalItems = CHECKLIST.length;
  const completedItems = checked.size;
  const progressPct = totalItems > 0 ? Math.round((completedItems / totalItems) * 100) : 0;

  return (
    <>
      <LearnerPageHero
        title="Test-Day Preparation"
        description="Your comprehensive checklist and tips for OET exam day success."
        icon={ClipboardCheck}
      />

      <MotionSection>
        <Card>
          <div className="mb-2 flex items-center justify-between gap-3">
            <h2 className="text-sm font-semibold text-navy">Preparation Progress</h2>
            <span className="text-sm font-medium tabular-nums text-primary">{completedItems}/{totalItems}</span>
          </div>
          <ProgressBar value={progressPct} size="md" ariaLabel="Test-day preparation progress" />
          {progressPct === 100 && (
            <p className="mt-2 text-sm font-medium text-success-strong" role="status">
              All done! You&apos;re ready for test day.
            </p>
          )}
        </Card>
      </MotionSection>

      {categories.map((cat, categoryIndex) => {
        const meta = CATEGORY_META[cat];
        const items = CHECKLIST.filter((i) => i.category === cat);
        return (
          <MotionSection key={cat} delayIndex={Math.min(categoryIndex + 1, 5)}>
            <LearnerSurfaceSectionHeader icon={meta.icon} title={meta.label} />
            <div className="mt-3 space-y-2">
              {items.map((item, index) => {
                const isChecked = checked.has(item.id);
                return (
                  <MotionItem key={item.id} delayIndex={Math.min(index, 5)}>
                    <button
                      type="button"
                      aria-pressed={isChecked}
                      onClick={() => toggleItem(item.id)}
                      className={`flex w-full items-start gap-3 rounded-xl border p-3 text-start transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${
                        isChecked
                          ? 'border-success/30 bg-success/10'
                          : 'border-border bg-surface hover:border-primary/30'
                      }`}
                    >
                      {isChecked
                        ? <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-success-strong" aria-hidden="true" />
                        : <Circle className="mt-0.5 h-5 w-5 shrink-0 text-muted/60" aria-hidden="true" />
                      }
                      <div className="min-w-0">
                        <p className={`text-sm font-medium ${isChecked ? 'text-muted line-through' : 'text-navy'}`}>
                          {item.label}
                        </p>
                        <p className="mt-0.5 text-xs text-muted">{item.description}</p>
                      </div>
                    </button>
                  </MotionItem>
                );
              })}
            </div>
          </MotionSection>
        );
      })}

      <MotionSection delayIndex={5}>
        <LearnerSurfaceSectionHeader
          icon={AlertTriangle}
          title="Exam-day strategies"
          description="Subtest-specific pacing, scanning, and rapport techniques used by high-scorers."
        />
        <MotionItem className="mt-3">
          <CardLink href="/strategies" className="group">
            <p className="inline-flex items-center gap-1.5 text-sm font-semibold text-primary">
              View detailed strategies <ArrowRight className="h-3.5 w-3.5 rtl:rotate-180" aria-hidden="true" />
            </p>
            <p className="mt-1 text-xs text-muted">Listening, Reading, Writing, and Speaking tactical guides.</p>
          </CardLink>
        </MotionItem>
      </MotionSection>
    </>
  );
}
