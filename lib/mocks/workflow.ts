import type { MockReport, MockSession, MockSessionSection } from '@/lib/mock-data';
import { oetGradeFromScaled } from '@/lib/scoring';

export type MockMode = 'exam' | 'practice';
export type MockSubtestCode = 'listening' | 'reading' | 'writing' | 'speaking';
export type MockReadinessLevel = 'red' | 'amber' | 'green' | 'dark-green' | 'pending';

export const MOCK_SUBTEST_ORDER: readonly MockSubtestCode[] = ['listening', 'reading', 'writing', 'speaking'] as const;

export const MOCK_EXAM_FLOW_STAGES = [
  {
    id: 'system-check',
    label: 'System check / instructions',
    duration: '5–10 min',
    description: 'Confirm audio, microphone, environment, and integrity rules before starting.',
  },
  {
    id: 'listening',
    label: 'Listening',
    duration: 'About 40 min',
    description: 'One-play audio, hidden transcript, no free skipping in strict mock mode.',
  },
  {
    id: 'reading-a',
    label: 'Reading Part A',
    duration: '15 min',
    description: 'Strict Part A window; in exam mode learners cannot return after it locks.',
  },
  {
    id: 'break',
    label: 'Optional short break',
    duration: 'Configurable',
    description: 'Academy-controlled break after Reading Part A for official-style LRW mocks.',
  },
  {
    id: 'reading-bc',
    label: 'Reading Parts B & C',
    duration: '45 min',
    description: 'Shared B/C timer with review only after submission.',
  },
  {
    id: 'writing',
    label: 'Writing',
    duration: '5 + 40 min',
    description: 'Five-minute reading window followed by a plain 40-minute writing editor.',
  },
  {
    id: 'speaking',
    label: 'Speaking',
    duration: 'Separate session',
    description: 'Two role-plays with tutor/interlocutor; may be scheduled separately from LRW.',
  },
] as const;

export const MOCK_REVIEW_RELEASE_STEPS = [
  'Student finishes the mock attempt under the selected timing policy.',
  'Listening and Reading auto-scored evidence can appear immediately when available.',
  'Writing scripts selected for review enter the teacher marking queue.',
  'Speaking recordings selected for review enter the tutor/interlocutor queue.',
  'The final readiness report is complete once teacher-marked sections are returned.',
  'The report ends with targeted remediation and retake advice.',
] as const;

export interface MockModePolicy {
  mode: MockMode;
  label: string;
  description: string;
  strictTimerRequired: boolean;
  listeningReplayAllowed: boolean;
  pauseAllowed: boolean;
  hintsAllowed: boolean;
  transcriptDuringAttemptAllowed: boolean;
  writingAssistantAllowed: boolean;
  speakingCoachingAllowed: boolean;
  reviewAfterSubmission: boolean;
}

export interface MockSectionPolicy {
  subtest: MockSubtestCode;
  label: string;
  timing: string;
  examRule: string;
  reviewRule: string;
}

export interface MockSubmissionReadiness {
  canSubmit: boolean;
  completedCount: number;
  totalCount: number;
  incompleteSections: MockSessionSection[];
  message: string;
}

export interface MockRemediationAction {
  day: string;
  title: string;
  description: string;
  route: string;
}

export interface MockReadinessDecision {
  level: MockReadinessLevel;
  label: string;
  description: string;
  variant: 'danger' | 'warning' | 'success' | 'info' | 'muted';
}

export function getMockModePolicy(mode: MockMode): MockModePolicy {
  if (mode === 'practice') {
    return {
      mode,
      label: 'Practice mode teaches',
      description: 'Flexible timing, review support, and learning aids are allowed so the learner can improve before a strict mock.',
      strictTimerRequired: false,
      listeningReplayAllowed: true,
      pauseAllowed: true,
      hintsAllowed: true,
      transcriptDuringAttemptAllowed: true,
      writingAssistantAllowed: true,
      speakingCoachingAllowed: true,
      reviewAfterSubmission: true,
    };
  }

  return {
    mode,
    label: 'Mock mode tests',
    description: 'Strict timing, one-play Listening, locked review aids, and teacher-marked productive skills simulate the real OET experience.',
    strictTimerRequired: true,
    listeningReplayAllowed: false,
    pauseAllowed: false,
    hintsAllowed: false,
    transcriptDuringAttemptAllowed: false,
    writingAssistantAllowed: false,
    speakingCoachingAllowed: false,
    reviewAfterSubmission: true,
  };
}

export function getMockSectionPolicy(subtest: string | undefined, mode: MockMode): MockSectionPolicy {
  const normalized = normalizeMockSubtest(subtest);
  const examMode = mode === 'exam';

  switch (normalized) {
    case 'listening':
      return {
        subtest: 'listening',
        label: 'Listening',
        timing: 'About 40 minutes',
        examRule: examMode ? 'Audio is one-play only; pause/replay/transcript stay locked.' : 'Replay and transcript support may be used for learning.',
        reviewRule: 'Auto-scored, with transcript and answer review released after submission.',
      };
    case 'reading':
      return {
        subtest: 'reading',
        label: 'Reading',
        timing: '15 minutes for Part A + 45 minutes for Parts B/C',
        examRule: examMode ? 'Part A locks after its window; review is held until full submission.' : 'Flexible practice can focus on individual parts before a full mock.',
        reviewRule: 'Auto-scored with part-level error analysis and timing review.',
      };
    case 'writing':
      return {
        subtest: 'writing',
        label: 'Writing',
        timing: '5-minute reading window + 40-minute writing window',
        examRule: examMode ? 'No grammar assistant; plain editor and autosave only.' : 'Learning support can be used before the final timed rewrite.',
        reviewRule: 'Teacher-marked against Purpose, Content, Conciseness, Genre/Style, Organisation, and Language.',
      };
    case 'speaking':
      return {
        subtest: 'speaking',
        label: 'Speaking',
        timing: 'Two role-plays, each with 3-minute preparation and about 5 minutes interaction',
        examRule: examMode ? 'No coaching during the interaction; recording is saved for tutor marking.' : 'Practice can include coaching before or after the recording.',
        reviewRule: 'Tutor/interlocutor feedback covers linguistic and clinical communication criteria.',
      };
  }
}

export function getMockSubmissionReadiness(session: MockSession): MockSubmissionReadiness {
  const sections = session.sectionStates;
  const incompleteSections = sections.filter((section) => !isMockSectionComplete(section));
  const completedCount = sections.length - incompleteSections.length;
  const canSubmit = sections.length > 0 && incompleteSections.length === 0;

  return {
    canSubmit,
    completedCount,
    totalCount: sections.length,
    incompleteSections,
    message: canSubmit
      ? 'All mock sections are recorded. Submit to release auto-scored evidence and queue any teacher-marked sections.'
      : `Complete ${incompleteSections.length} remaining section${incompleteSections.length === 1 ? '' : 's'} before final report generation.`,
  };
}

export function getMockReadinessDecision(report: MockReport): MockReadinessDecision {
  const score = parseScoreValue(report.overallScore);
  if (score === null) {
    return {
      level: 'pending',
      label: 'Pending evidence',
      description: 'Finish scored sections and teacher-marked reviews before using this report for booking decisions.',
      variant: 'muted',
    };
  }
  const grade = oetGradeFromScaled(score);
  const meetsCanonicalGradeB = grade === 'A' || grade === 'B';

  if (score >= 400) {
    return {
      level: 'dark-green',
      label: 'Strong readiness signal',
      description: 'This mock suggests strong readiness, but it is still an academy estimate rather than an official OET result.',
      variant: 'success',
    };
  }
  if (meetsCanonicalGradeB) {
    return {
      level: 'green',
      label: 'Exam-ready signal',
      description: 'This mock is at or above the usual readiness target. Confirm consistency across repeated mocks before booking.',
      variant: 'success',
    };
  }
  if (score >= 320) {
    return {
      level: 'amber',
      label: 'Borderline readiness',
      description: 'You are close, but should complete targeted remediation and retake a mock before booking the real exam.',
      variant: 'warning',
    };
  }

  return {
    level: 'red',
    label: 'Not ready yet',
    description: 'Major weaknesses remain. Prioritise the assigned practice plan before another full exam-readiness mock.',
    variant: 'danger',
  };
}

export function buildMockRemediationPlan(report: MockReport): MockRemediationAction[] {
  const weakSubtest = report.weakestCriterion.subtest.toLowerCase();
  const weakCriterion = report.weakestCriterion.criterion;
  const weakDescription = report.weakestCriterion.description;
  const route = routeForWeakness(weakSubtest);

  return [
    {
      day: 'Day 1',
      title: 'Review every lost mark',
      description: 'Compare the report, answer review, teacher comments, and timing notes before attempting new work.',
      route: `/mocks/report/${report.id}`,
    },
    {
      day: 'Day 2',
      title: `Repair ${weakCriterion}`,
      description: weakDescription,
      route,
    },
    {
      day: 'Day 3',
      title: 'Complete a targeted micro-drill',
      description: `Focus on ${weakSubtest || 'the weakest sub-test'} without full-exam pressure first.`,
      route,
    },
    {
      day: 'Day 4',
      title: 'Attempt a sectional mock',
      description: 'Use a single sub-test mock to check whether the repair is transferring under timed conditions.',
      route: weakSubtest ? `/mocks/setup?type=sub&subtest=${encodeURIComponent(weakSubtest)}` : '/mocks/setup?type=sub',
    },
    {
      day: 'Day 5–7',
      title: 'Book tutor review or retake',
      description: 'If Writing or Speaking is involved, request tutor feedback before another full readiness mock.',
      route: '/mocks/setup',
    },
  ];
}

export function isTeacherMarkedSubtest(subtest: string | undefined): boolean {
  const normalized = normalizeMockSubtest(subtest);
  return normalized === 'writing' || normalized === 'speaking';
}

export function normalizeMockSubtest(subtest: string | undefined): MockSubtestCode {
  const normalized = subtest?.toLowerCase();
  if (normalized === 'listening' || normalized === 'reading' || normalized === 'writing' || normalized === 'speaking') {
    return normalized;
  }
  return 'reading';
}

function isMockSectionComplete(section: MockSessionSection): boolean {
  const state = section.state.toLowerCase().replace(/_/g, '-');
  return state === 'completed' || state === 'submitted' || state === 'evaluating' || Boolean(section.completedAt || section.submittedAt);
}

function parseScoreValue(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed || /pending|n\/a/i.test(trimmed)) return null;
  const numeric = Number(trimmed.replace(/[^0-9.]/g, ''));
  if (!Number.isFinite(numeric)) return null;
  if (trimmed.includes('%')) return Math.round(numeric * 5);
  return numeric;
}

function routeForWeakness(subtest: string): string {
  if (subtest.includes('listening')) return '/listening';
  if (subtest.includes('reading')) return '/reading/practice';
  if (subtest.includes('writing')) return '/writing/library';
  if (subtest.includes('speaking')) return '/speaking/selection';
  return '/practice';
}
