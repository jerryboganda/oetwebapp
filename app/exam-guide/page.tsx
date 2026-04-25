'use client';

import { BookOpen, Clock, Headphones, PenLine, Mic } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { PageViewBeacon } from '@/components/analytics/page-view-beacon';

const EXAM_SECTIONS = [
  { icon: Headphones, title: 'Listening', duration: '~42 minutes', parts: 'Part A (consultation extract) + Part B (short workplace extracts)', scoring: '0–500 per subtest', tips: ['Listen for specific information and gist', 'Part A has one long dialogue, Part B has 6 short extracts', 'Read questions before audio plays'] },
  { icon: BookOpen, title: 'Reading', duration: '60 minutes', parts: 'Part A (expeditious reading) + Part B (careful reading) + Part C (careful reading)', scoring: '0–500 per subtest', tips: ['Part A requires fast scanning of 3–4 texts', 'Part B tests understanding of a single text', 'Part C has 2 longer texts — manage your time'] },
  { icon: PenLine, title: 'Writing', duration: '45 minutes', parts: '1 writing task (referral/discharge/transfer letter)', scoring: '6 criteria, 0–500 overall', tips: ['Read the case notes carefully — 5 minutes reading time', 'Use appropriate clinical register', 'Address ALL relevant points from case notes', 'Structure: Opening → Body (key findings) → Request/Action'] },
  { icon: Mic, title: 'Speaking', duration: '~20 minutes', parts: '2 role-plays with an interlocutor (actor, not assessor); a separate OET assessor grades the recording after the exam', scoring: '9 criteria (4 linguistic 0–6 + 5 clinical-communication 0–3), 0–500 overall', tips: ['Computer-based Speaking is always taken at home — never at a test centre', 'Single monitor, wired or built-in webcam, no Bluetooth audio, no headsets', 'Plug device directly into power; disable VPN/VM; unplug extra screens; be completely alone', 'You may use one blank paper and a pen for notes — the role-play card cannot be annotated on screen', 'At the end of the exam you must tear or cut the paper in front of the camera', 'Demonstrate clinical communication, not medical knowledge; show empathy and active listening'] },
];

const SCORING_GUIDE = [
  { grade: 'A', range: '450–500', level: 'Superior', description: 'Can communicate very effectively in a health professional context.' },
  { grade: 'B', range: '350–440', level: 'Advanced', description: 'Can communicate effectively in a health professional context.' },
  { grade: 'C+', range: '300–340', level: 'Good', description: 'Can communicate adequately in most health professional contexts.' },
  { grade: 'C', range: '200–290', level: 'Adequate', description: 'Can communicate adequately in familiar health professional contexts.' },
  { grade: 'D', range: '100–190', level: 'Limited', description: 'Communication is restricted with frequent errors.' },
  { grade: 'E', range: '0–90', level: 'Very Limited', description: 'Very limited communication ability.' },
];

export default function ExamGuidePage() {
  return (
    <LearnerDashboardShell>
      <PageViewBeacon event="exam_guide_viewed" />
      <LearnerPageHero title="OET Exam Guide" description="Everything you need to know about the OET exam format, timing, scoring, and strategies." />

      <MotionSection className="space-y-8 max-w-4xl mx-auto">
        <LearnerSurfaceSectionHeader title="Exam Structure" />
        <div className="space-y-4">
          {EXAM_SECTIONS.map(section => (
            <MotionItem key={section.title}>
              <Card className="p-5">
                <div className="flex items-start gap-4">
                  <section.icon className="w-6 h-6 text-primary flex-shrink-0 mt-1" />
                  <div className="flex-1">
                    <div className="flex items-center justify-between"><h3 className="text-lg font-semibold">{section.title}</h3><Badge variant="outline"><Clock className="w-3 h-3 inline mr-1" />{section.duration}</Badge></div>
                    <p className="text-sm text-muted-foreground mt-1">{section.parts}</p>
                    <p className="text-sm text-muted-foreground">Scoring: {section.scoring}</p>
                    <div className="mt-3 space-y-1">{section.tips.map((tip, i) => <p key={i} className="text-sm">• {tip}</p>)}</div>
                  </div>
                </div>
              </Card>
            </MotionItem>
          ))}
        </div>

        <LearnerSurfaceSectionHeader title="Scoring & Grades" />
        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
          {SCORING_GUIDE.map(s => (
            <MotionItem key={s.grade}>
              <Card className="p-4 flex items-center gap-3">
                <div className="w-12 h-12 rounded-full bg-primary/10 flex items-center justify-center font-bold text-primary">{s.grade}</div>
                <div><p className="font-semibold">{s.level} <span className="text-muted-foreground font-normal text-sm">({s.range})</span></p><p className="text-sm text-muted-foreground">{s.description}</p></div>
              </Card>
            </MotionItem>
          ))}
        </div>

        <LearnerSurfaceSectionHeader title="Key Facts" />
        <Card className="p-5 space-y-2">
          <p className="text-sm">• <strong>12 healthcare professions</strong> supported: Medicine, Nursing, Dentistry, Pharmacy, Physiotherapy, and more</p>
          <p className="text-sm">• <strong>Delivery modes</strong>: Paper-based, Computer-based, OET@Home</p>
          <p className="text-sm">• <strong>Results</strong>: 6 business days (computer/home), 12 days (paper)</p>
          <p className="text-sm">• <strong>Validity</strong>: Results valid for 2 years</p>
          <p className="text-sm">• <strong>Required score</strong>: Most regulatory bodies require minimum B (350+) in all subtests</p>
        </Card>
      </MotionSection>
    </LearnerDashboardShell>
  );
}
