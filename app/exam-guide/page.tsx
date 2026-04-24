import { BookOpen, Headphones, PenLine, Mic } from 'lucide-react';
import ExamGuideContent, { type ExamSection, type ScoringGuideEntry } from './_content';

const EXAM_SECTIONS: ExamSection[] = [
  { icon: <Headphones className="w-6 h-6 text-primary flex-shrink-0 mt-1" />, title: 'Listening', duration: '~42 minutes', parts: 'Part A (consultation extract) + Part B (short workplace extracts)', scoring: '0–500 per subtest', tips: ['Listen for specific information and gist', 'Part A has one long dialogue, Part B has 6 short extracts', 'Read questions before audio plays'] },
  { icon: <BookOpen className="w-6 h-6 text-primary flex-shrink-0 mt-1" />, title: 'Reading', duration: '60 minutes', parts: 'Part A (expeditious reading) + Part B (careful reading) + Part C (careful reading)', scoring: '0–500 per subtest', tips: ['Part A requires fast scanning of 3–4 texts', 'Part B tests understanding of a single text', 'Part C has 2 longer texts — manage your time'] },
  { icon: <PenLine className="w-6 h-6 text-primary flex-shrink-0 mt-1" />, title: 'Writing', duration: '45 minutes', parts: '1 writing task (referral/discharge/transfer letter)', scoring: '6 criteria, 0–500 overall', tips: ['Read the case notes carefully — 5 minutes reading time', 'Use appropriate clinical register', 'Address ALL relevant points from case notes', 'Structure: Opening → Body (key findings) → Request/Action'] },
  { icon: <Mic className="w-6 h-6 text-primary flex-shrink-0 mt-1" />, title: 'Speaking', duration: '~20 minutes', parts: '2 role-plays with an interlocutor (actor, not assessor); a separate OET assessor grades the recording after the exam', scoring: '9 criteria (4 linguistic 0–6 + 5 clinical-communication 0–3), 0–500 overall', tips: ['Computer-based Speaking is always taken at home — never at a test centre', 'Single monitor, wired or built-in webcam, no Bluetooth audio, no headsets', 'Plug device directly into power; disable VPN/VM; unplug extra screens; be completely alone', 'You may use one blank paper and a pen for notes — the role-play card cannot be annotated on screen', 'At the end of the exam you must tear or cut the paper in front of the camera', 'Demonstrate clinical communication, not medical knowledge; show empathy and active listening'] },
];

const SCORING_GUIDE: ScoringGuideEntry[] = [
  { grade: 'A', range: '450–500', level: 'Superior', description: 'Can communicate very effectively in a health professional context.' },
  { grade: 'B', range: '350–440', level: 'Advanced', description: 'Can communicate effectively in a health professional context.' },
  { grade: 'C+', range: '300–340', level: 'Good', description: 'Can communicate adequately in most health professional contexts.' },
  { grade: 'C', range: '200–290', level: 'Adequate', description: 'Can communicate adequately in familiar health professional contexts.' },
  { grade: 'D', range: '100–190', level: 'Limited', description: 'Communication is restricted with frequent errors.' },
  { grade: 'E', range: '0–90', level: 'Very Limited', description: 'Very limited communication ability.' },
];

export default function ExamGuidePage() {
  return <ExamGuideContent examSections={EXAM_SECTIONS} scoringGuide={SCORING_GUIDE} />;
}
