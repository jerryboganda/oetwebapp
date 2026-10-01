import type { Metadata } from 'next';
import Link from 'next/link';
import { BookOpen, Clock, Headphones, PenLine, Mic, ArrowRight } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { PageViewBeacon } from '@/components/analytics/page-view-beacon';

// `tone` is the sub-test's identity colour (DESIGN.md §2 skill tokens).
const EXAM_SECTIONS = [
  { icon: Headphones, title: 'Listening', tone: 'text-skill-listening', href: '/listening', duration: '~45–50 minutes', parts: '42 questions: Part A (24 note-completion marks), Part B (6 MCQs), and Part C (12 MCQs)', scoring: '0–500 per subtest', tips: ['Listen for specific information and gist', 'Part A has two consultation extracts; Part B has 6 short extracts; Part C has 2 continuous extracts', 'Read questions before audio plays', 'Exam-day technical requirements are guidance only here: use reliable audio, a stable connection, and a comfortable display; the AI practice platform does not hard-block on resolution, headset type, display scale, VPN, or VM signals'] },
  { icon: BookOpen, title: 'Reading', tone: 'text-skill-reading', href: '/reading', duration: '60 minutes', parts: '42 questions: Part A (20 marks, four related texts) plus Parts B (6 MCQs) and C (16 MCQs) in one shared 45-minute block', scoring: '0–500 per subtest', tips: ['Part A has a strict 15-minute block across four related texts', 'Part B has 6 independent short texts', 'Part C has 2 longer texts with 8 questions each; manage the shared B+C time'] },
  { icon: PenLine, title: 'Writing', tone: 'text-skill-writing', href: '/writing', duration: '45 minutes', parts: '1 writing task (referral/discharge/transfer letter)', scoring: '6 criteria, 0–500 overall', tips: ['Read the case notes carefully in the 5 minutes reading time', 'Use appropriate clinical register', 'Address ALL relevant points from case notes', 'Structure: Opening → Body (key findings) → Request/Action'] },
  { icon: Mic, title: 'Speaking', tone: 'text-skill-speaking', href: '/speaking', duration: '~20 minutes', parts: '2 role-plays with an interlocutor (actor, not assessor); a separate OET assessor grades the recording after the exam', scoring: '9 criteria (4 linguistic 0–6 + 5 clinical-communication 0–3), 0–500 overall', tips: ['Computer-based Speaking is always taken at home, never at a test centre', 'Single monitor, wired or built-in webcam, no Bluetooth audio, no headsets', 'Plug device directly into power; disable VPN/VM; unplug extra screens; be completely alone', 'You may use one blank paper and a pen for notes; the role-play card cannot be annotated on screen', 'At the end of the exam you must tear or cut the paper in front of the camera', 'Demonstrate clinical communication, not medical knowledge; show empathy and active listening'] },
];

const SCORING_GUIDE = [
  { grade: 'A', range: '450–500', level: 'Superior', description: 'Can communicate very effectively in a health professional context.' },
  { grade: 'B', range: '350–440', level: 'Advanced', description: 'Can communicate effectively in a health professional context.' },
  { grade: 'C+', range: '300–340', level: 'Good', description: 'Can communicate adequately in most health professional contexts.' },
  { grade: 'C', range: '200–290', level: 'Adequate', description: 'Can communicate adequately in familiar health professional contexts.' },
  { grade: 'D', range: '100–190', level: 'Limited', description: 'Communication is restricted with frequent errors.' },
  { grade: 'E', range: '0–90', level: 'Very Limited', description: 'Very limited communication ability.' },
];

export const metadata: Metadata = {
  title: 'OET Exam Guide · OET with Dr Ahmed Hesham',
  description: 'OET exam format, timing, scoring bands, and strategies for Listening, Reading, Writing, and Speaking.',
};

export default function ExamGuidePage() {
  return (
    <>
      <PageViewBeacon event="exam_guide_viewed" />
      <LearnerPageHero
        icon={BookOpen}
        title="OET Exam Guide"
        description="Everything you need to know about the OET exam format, timing, scoring, and strategies."
      />

      <MotionSection>
        <LearnerSurfaceSectionHeader title="Exam Structure" className="mb-4" />
        <div className="space-y-4">
          {EXAM_SECTIONS.map((section, index) => (
            <MotionItem key={section.title} delayIndex={Math.min(index, 5)}>
              <Card padding="lg">
                <div className="flex items-start gap-4">
                  <section.icon className={`mt-1 h-6 w-6 shrink-0 ${section.tone}`} aria-hidden="true" />
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <h3 className="text-lg font-semibold text-navy">{section.title}</h3>
                      <Badge variant="outline">
                        <Clock className="me-1 h-3 w-3" aria-hidden="true" />
                        {section.duration}
                      </Badge>
                    </div>
                    {/* Reading text: capped line length inside the card. */}
                    <div className="max-w-3xl">
                      <p className="mt-1 text-sm text-muted">{section.parts}</p>
                      <p className="text-sm text-muted">Scoring: {section.scoring}</p>
                      <ul className="mt-3 list-disc space-y-1 ps-5 text-sm">
                        {section.tips.map((tip) => <li key={tip}>{tip}</li>)}
                      </ul>
                    </div>
                    <Link
                      href={section.href}
                      className="mt-2 inline-flex min-h-11 items-center gap-1 text-sm font-semibold text-primary transition-colors hover:text-primary/80"
                    >
                      Practice {section.title} <ArrowRight className="h-3.5 w-3.5 rtl:rotate-180" aria-hidden="true" />
                    </Link>
                  </div>
                </div>
              </Card>
            </MotionItem>
          ))}
        </div>
      </MotionSection>

      <MotionSection delayIndex={1}>
        <LearnerSurfaceSectionHeader title="Scoring & Grades" className="mb-4" />
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          {SCORING_GUIDE.map((s, index) => (
            <MotionItem key={s.grade} delayIndex={Math.min(index, 5)} className="h-full">
              <Card className="flex h-full items-center gap-3">
                <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-primary/10 font-bold text-primary">{s.grade}</div>
                <div className="min-w-0">
                  <p className="font-semibold text-navy">
                    {s.level} <span className="text-sm font-normal tabular-nums text-muted">({s.range})</span>
                  </p>
                  <p className="text-sm text-muted">{s.description}</p>
                </div>
              </Card>
            </MotionItem>
          ))}
        </div>
      </MotionSection>

      <MotionSection delayIndex={2}>
        <LearnerSurfaceSectionHeader title="Key Facts" className="mb-4" />
        <Card padding="lg">
          <ul className="max-w-3xl list-disc space-y-2 ps-5 text-sm">
            <li><strong>12 healthcare professions</strong> supported: Medicine, Nursing, Dentistry, Pharmacy, Physiotherapy, and more</li>
            <li><strong>Platform delivery scope</strong>: Website computer-based practice and OET@Home-style rehearsal only; paper-based exam behaviour is educational guidance, not a simulated platform mode</li>
            <li><strong>Practice results</strong>: deterministic raw and owner-table scores are shown after submission; they are not official OET results</li>
            <li><strong>Validity</strong>: Results valid for 2 years</li>
            <li><strong>Required score</strong>: Most regulatory bodies require minimum B (350+) in all subtests</li>
          </ul>
        </Card>
      </MotionSection>
    </>
  );
}
