'use client';

import { useState } from 'react';
import { ClipboardCheck, FileText, Clock, MapPin, CheckCircle2, Circle, BookOpen, AlertTriangle } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { analytics } from '@/lib/analytics';

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
  { id: 'o4', label: 'Read all instructions carefully', description: 'Don\'t rush through the rubric — note word limits and task requirements.', category: 'on_the_day' },
];

const CATEGORY_META: Record<string, { label: string; icon: React.ReactNode; color: string }> = {
  documents: { label: 'Documents', icon: <FileText className="w-5 h-5" />, color: 'text-info' },
  logistics: { label: 'Logistics', icon: <MapPin className="w-5 h-5" />, color: 'text-success' },
  preparation: { label: 'Final Preparation', icon: <BookOpen className="w-5 h-5" />, color: 'text-primary' },
  on_the_day: { label: 'On the Day', icon: <Clock className="w-5 h-5" />, color: 'text-warning' },
};

const TIPS = [
  'Time management is crucial — practise pacing for each subtest.',
  'In Writing, always re-read the case notes before finalising your letter.',
  'In Speaking, build rapport by using the patient\'s name and maintaining eye contact.',
  'In Listening, write answers as you listen — don\'t rely on memory alone.',
  'In Reading, scan for keywords before reading passages in detail.',
  'Stay calm if you find a section difficult — every candidate feels this way.',
];

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
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Test-Day Preparation"
        description="Your comprehensive checklist and tips for OET exam day success."
        icon={<ClipboardCheck className="w-7 h-7" />}
      />

      {/* Progress Bar */}
      <MotionSection>
        <Card className="p-5">
          <div className="flex items-center justify-between mb-2">
            <h3 className="text-sm font-semibold text-navy">Preparation Progress</h3>
            <span className="text-sm font-medium text-primary">{completedItems}/{totalItems}</span>
          </div>
          <div className="w-full bg-background-light rounded-full h-3">
            <div
              className="bg-gradient-to-r from-indigo-500 to-purple-500 h-3 rounded-full transition-all duration-500"
              style={{ width: `${progressPct}%` }}
            />
          </div>
          {progressPct === 100 && (
            <p className="mt-2 text-sm text-success font-medium">
              All done! You&apos;re ready for test day.
            </p>
          )}
        </Card>
      </MotionSection>

      {/* Checklist by Category */}
      {categories.map((cat) => {
        const meta = CATEGORY_META[cat];
        const items = CHECKLIST.filter((i) => i.category === cat);
        return (
          <MotionSection key={cat} className="mt-6">
            <LearnerSurfaceSectionHeader icon={meta.icon} title={meta.label} />
            <div className="space-y-2 mt-3">
              {items.map((item) => {
                const isChecked = checked.has(item.id);
                return (
                  <MotionItem key={item.id}>
                    <button
                      type="button"
                      onClick={() => toggleItem(item.id)}
                      className={`w-full flex items-start gap-3 p-3 rounded-lg border transition-colors text-left ${
                        isChecked
                          ? 'bg-success/10 border-success/30'
                          : 'bg-surface border-border hover:border-primary/30'
                      }`}
                    >
                      {isChecked
                        ? <CheckCircle2 className="w-5 h-5 text-success mt-0.5 flex-shrink-0" />
                        : <Circle className="w-5 h-5 text-muted/60 mt-0.5 flex-shrink-0" />
                      }
                      <div>
                        <p className={`text-sm font-medium ${isChecked ? 'line-through text-muted/60' : 'text-navy'}`}>
                          {item.label}
                        </p>
                        <p className="text-xs text-muted mt-0.5">{item.description}</p>
                      </div>
                    </button>
                  </MotionItem>
                );
              })}
            </div>
          </MotionSection>
        );
      })}

      {/* Expert Tips */}
      <MotionSection className="mt-8">
        <LearnerSurfaceSectionHeader
          icon={<AlertTriangle className="w-5 h-5" />}
          title="Expert Tips"
          description="Key advice from OET experts and past high-scorers."
        />
        <div className="grid gap-3 sm:grid-cols-2 mt-3">
          {TIPS.map((tip, i) => (
            <MotionItem key={i}>
              <Card className="p-4 bg-amber-50/60 border-warning/30">
                <p className="text-sm text-warning">{tip}</p>
              </Card>
            </MotionItem>
          ))}
        </div>
      </MotionSection>
    </LearnerDashboardShell>
  );
}
