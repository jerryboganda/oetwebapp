// ──────────────────────────────────────────────
// Centralized mock data & types for the OET Learner App
// Replace with real API responses when backend is ready
// ──────────────────────────────────────────────

// ═══════════════════ SHARED TYPES ═══════════════════

export type SubTest = 'Writing' | 'Speaking' | 'Reading' | 'Listening';
export type Difficulty = 'Easy' | 'Medium' | 'Hard';
export type TaskStatus = 'not_started' | 'in_progress' | 'completed' | 'failed';
export type ReviewStatus = 'reviewed' | 'pending' | 'not_requested';
export type EvalStatus = 'queued' | 'processing' | 'completed' | 'failed';
export type Confidence = 'High' | 'Medium' | 'Low';
export type RiskLevel = 'Low' | 'Moderate' | 'High';

export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
  profession: string;
  examDate: string | null;
  targetScores: Record<SubTest, number | null>;
  previousAttempts: number;
  weakSubTests: SubTest[];
  studyHoursPerWeek: number;
  targetCountry: string;
  onboardingComplete: boolean;
  goalsComplete: boolean;
  diagnosticComplete: boolean;
  createdAt: string;
}

export interface StudyPlanTask {
  id: string;
  title: string;
  subTest: SubTest;
  duration: string;
  rationale: string;
  dueDate: string;
  status: TaskStatus;
  section: 'today' | 'thisWeek' | 'nextCheckpoint' | 'weakSkillFocus';
  contentId?: string;
  type?: string;
}

// ═══════════════════ WRITING TYPES ═══════════════════

export interface WritingTask {
  id: string;
  title: string;
  difficulty: Difficulty;
  profession: string;
  time: string;
  criteriaFocus: string;
  scenarioType: string;
  caseNotes: string;
}

export interface ChecklistItem {
  id: number;
  text: string;
  completed: boolean;
}

export interface AnchoredComment {
  id: string;
  text: string;
  comment: string;
}

export interface CriterionFeedback {
  name: string;
  score: number;
  maxScore: number;
  grade: string;
  explanation: string;
  anchoredComments: AnchoredComment[];
  omissions: string[];
  unnecessaryDetails: string[];
  revisionSuggestions: string[];
  strengths: string[];
  issues: string[];
}

export interface WritingResult {
  id: string;
  taskId: string;
  taskTitle: string;
  estimatedScoreRange: string;
  estimatedGradeRange: string;
  confidenceLabel: Confidence;
  topStrengths: string[];
  topIssues: string[];
  criteria: CriterionFeedback[];
  submittedAt: string;
  evalStatus: EvalStatus;
}

export interface CriteriaDelta {
  name: string;
  original: number;
  revised: number;
  max: number;
}

export interface ModelParagraph {
  id: string;
  text: string;
  rationale: string;
  criteria: string[];
  included: string[];
  excluded: string[];
  languageNotes: string;
}

export interface ModelAnswer {
  taskId: string;
  taskTitle: string;
  profession: string;
  paragraphs: ModelParagraph[];
}

export interface WritingSubmission {
  id: string;
  taskId: string;
  taskTitle: string;
  content: string;
  wordCount: number;
  submittedAt: string;
  evalStatus: EvalStatus;
  scoreEstimate?: string;
  reviewStatus: ReviewStatus;
}

// ═══════════════════ SPEAKING TYPES ═══════════════════

export interface SpeakingTask {
  id: string;
  title: string;
  scenarioType: string;
  difficulty: Difficulty;
  profession: string;
  criteriaFocus: string;
  duration: string;
}

export interface RoleCard {
  id: string;
  title: string;
  profession: string;
  setting: string;
  patient: string;
  brief: string;
  tasks: string[];
  background: string;
}

export type MarkerType =
  | 'pronunciation'
  | 'fluency'
  | 'grammar'
  | 'vocabulary'
  | 'empathy'
  | 'structure';

export interface TranscriptMarker {
  id: string;
  type: MarkerType;
  startTime: number;
  endTime: number;
  text: string;
  suggestion: string;
}

export interface TranscriptLine {
  id: string;
  speaker: string;
  text: string;
  startTime: number;
  endTime: number;
  markers?: TranscriptMarker[];
}

export interface SpeakingResult {
  id: string;
  taskId: string;
  taskTitle: string;
  scoreRange: string;
  confidence: Confidence;
  strengths: string[];
  improvements: string[];
  evalStatus: EvalStatus;
  submittedAt: string;
  nextDrill?: { title: string; description: string; id: string };
}

export interface PhrasingSegment {
  id: string;
  originalPhrase: string;
  issueExplanation: string;
  strongerAlternative: string;
  drillPrompt: string;
}

// ═══════════════════ READING TYPES ═══════════════════

export interface ReadingText {
  id: string;
  title: string;
  content: string;
}

export type QuestionType = 'mcq' | 'short_answer' | 'matching' | 'gap_fill';

export interface Question {
  id: string;
  number: number;
  text: string;
  type: QuestionType;
  options?: string[];
  correctAnswer: string;
}

export interface ReadingTask {
  id: string;
  title: string;
  part: 'A' | 'B' | 'C';
  timeLimit: number;
  texts: ReadingText[];
  questions: Question[];
}

export interface ReadingResultItem {
  id: string;
  number: number;
  text: string;
  userAnswer: string;
  correctAnswer: string;
  isCorrect: boolean;
  errorType: string;
  explanation: string;
}

export interface ErrorCluster {
  type: string;
  count: number;
  total: number;
  percentage: number;
}

export interface ReadingResult {
  taskId: string;
  title: string;
  score: number;
  totalQuestions: number;
  percentage: number;
  grade: string;
  errorClusters: ErrorCluster[];
  items: ReadingResultItem[];
}

// ═══════════════════ LISTENING TYPES ═══════════════════

export interface ListeningTask {
  id: string;
  title: string;
  audioSrc: string;
  duration: number;
  audioAvailable: boolean;
  audioUnavailableReason?: string;
  transcriptPolicy?: string;
  questions: Question[];
}

export interface ListeningResultQuestion {
  id: string;
  number: number;
  text: string;
  userAnswer: string;
  correctAnswer: string;
  isCorrect: boolean;
  explanation: string;
  allowTranscriptReveal: boolean;
  transcriptExcerpt?: string;
  distractorExplanation?: string;
}

export interface ListeningResult {
  id: string;
  title: string;
  score: number;
  total: number;
  questions: ListeningResultQuestion[];
  recommendedDrill: { id: string; title: string; description: string };
}

export interface ListeningDrill {
  id: string;
  title: string;
  focusLabel: string;
  description: string;
  errorType: string;
  estimatedMinutes: number;
  highlights: string[];
  launchRoute: string;
  reviewRoute: string;
}

export interface ListeningReviewQuestionEvidence {
  id: string;
  number: number;
  text: string;
  learnerAnswer: string;
  correctAnswer: string;
  explanation: string;
  transcriptExcerpt?: string;
  distractorExplanation?: string;
}

export interface ListeningReview {
  id: string;
  title: string;
  transcriptPolicy: string;
  questions: ListeningReviewQuestionEvidence[];
  recommendedDrill?: { id: string; title: string; description: string };
}

// ═══════════════════ MOCK TYPES ═══════════════════

export interface MockConfig {
  id: string;
  title: string;
  type: 'full' | 'sub';
  subType?: SubTest;
  mode: 'practice' | 'exam';
  profession: string;
  strictTimer: boolean;
  includeReview: boolean;
  reviewSelection: 'none' | 'writing' | 'speaking' | 'writing_and_speaking' | 'current_subtest';
}

export interface MockSessionSection {
  id: string;
  title: string;
  state: string;
  reviewAvailable: boolean;
  reviewSelected: boolean;
  launchRoute: string;
}

export interface MockSession {
  sessionId: string;
  state: string;
  config: MockConfig;
  sectionStates: MockSessionSection[];
  resumeRoute: string;
  reportRoute?: string | null;
  reportId?: string | null;
}

export interface SubTestScore {
  id: string;
  name: SubTest;
  score: string;
  rawScore: string;
  color: string;
  bg: string;
}

export interface MockReport {
  id: string;
  title: string;
  date: string;
  overallScore: string;
  summary: string;
  subTests: SubTestScore[];
  weakestCriterion: { subtest: string; criterion: string; description: string };
  priorComparison: { exists: boolean; priorMockName: string; overallTrend: 'up' | 'down' | 'flat'; details: string };
}

// ═══════════════════ PROGRESS & READINESS TYPES ═══════════════════

export interface TrendPoint {
  date: string;
  reading?: number;
  listening?: number;
  writing?: number;
  speaking?: number;
  [key: string]: string | number | undefined;
}

export interface SubTestReadiness {
  id: string;
  name: SubTest;
  readiness: number;
  target: number;
  status: string;
  color: string;
  bg: string;
  barColor: string;
  isWeakest?: boolean;
}

export interface ReadinessData {
  targetDate: string;
  weeksRemaining: number;
  overallRisk: RiskLevel;
  recommendedStudyHours: number;
  weakestLink: string;
  subTests: SubTestReadiness[];
  blockers: { id: number; title: string; description: string }[];
  evidence: {
    mocksCompleted: number;
    practiceQuestions: number;
    expertReviews: number;
    recentTrend: string;
    lastUpdated: string;
  };
}

export interface Submission {
  id: string;
  contentId: string;
  taskName: string;
  subTest: SubTest;
  attemptDate: string;
  scoreEstimate: string;
  reviewStatus: ReviewStatus;
  evaluationId?: string;
  state?: string;
  comparisonGroupId?: string | null;
  canRequestReview: boolean;
  actions: {
    reopenFeedbackRoute?: string | null;
    compareRoute?: string | null;
    requestReviewRoute?: string | null;
  };
}

export interface SubmissionDetail {
  submission: Submission;
  evidenceSummary: {
    title: string;
    scoreLabel: string;
    stateLabel: string;
    reviewLabel: string;
    nextActionLabel: string;
  };
  strengths: string[];
  issues: string[];
  transcript?: TranscriptLine[];
  criteria?: CriterionFeedback[];
  questionReview?: Array<{
    id: string;
    number: number;
    text: string;
    learnerAnswer: string;
    correctAnswer: string;
    isCorrect: boolean;
    explanation: string;
    transcriptExcerpt?: string;
    distractorExplanation?: string;
  }>;
}

export interface SubmissionComparison {
  canCompare: boolean;
  reason?: string;
  left?: {
    attemptId: string;
    evaluationId?: string;
    scoreRange?: string;
    subtest: SubTest;
  };
  right?: {
    attemptId: string;
    evaluationId?: string;
    scoreRange?: string;
    subtest: SubTest;
  };
  summary?: string;
  comparisonGroupId?: string | null;
}

// ═══════════════════ BILLING TYPES ═══════════════════

export interface Invoice {
  id: string;
  date: string;
  amount: string;
  status: 'Paid' | 'Pending' | 'Failed';
  currency?: string;
  downloadUrl?: string;
}

export interface BillingEntitlements {
  productiveSkillReviewsEnabled: boolean;
  supportedReviewSubtests: string[];
  invoiceDownloadsAvailable: boolean;
}

export interface BillingPlan {
  id: string;
  label: string;
  tier: string;
  description: string;
  price: string;
  interval: string;
  reviewCredits: number;
  canChangeTo: boolean;
  changeDirection: 'current' | 'upgrade' | 'downgrade';
  badge: string;
}

export interface BillingExtra {
  id: string;
  quantity: number;
  price: string;
  description: string;
}

export interface BillingChangePreview {
  currentPlanId: string;
  targetPlanId: string;
  direction: 'upgrade' | 'downgrade';
  proratedAmount: string;
  effectiveAt: string;
  summary: string;
  currentCreditsIncluded: number;
  targetCreditsIncluded: number;
}

export interface BillingData {
  currentPlan: string;
  currentPlanId: string;
  price: string;
  interval: string;
  status: string;
  nextRenewal: string;
  reviewCredits: number;
  entitlements: BillingEntitlements;
  plans: BillingPlan[];
  extras: BillingExtra[];
  invoices: Invoice[];
}

export type SettingsSectionId = 'profile' | 'goals' | 'study' | 'privacy' | 'notifications' | 'audio' | 'accessibility';

export interface SettingsSectionData {
  section: SettingsSectionId;
  values: Record<string, unknown>;
}

export interface SpeakingTranscriptReview {
  title: string;
  date: string;
  duration: number;
  transcript: TranscriptLine[];
  audioAvailable: boolean;
  audioUrl?: string;
  waveformPeaks: number[];
}

export interface ProgressEvidenceSummary {
  reviewUsage: {
    totalRequests: number;
    completedRequests: number;
    averageTurnaroundHours: number | null;
    creditsConsumed: number;
  };
  freshness: {
    generatedAt: string;
    usesFallbackSeries: boolean;
  };
}

// ═══════════════════ EXPERT REVIEW TYPES ═══════════════════

export interface TurnaroundOption {
  id: string;
  label: string;
  time: string;
  cost: number;
  description: string;
}

export interface FocusArea {
  id: string;
  label: string;
  description: string;
}

// ═══════════════════ DIAGNOSTIC TYPES ═══════════════════

export interface DiagnosticSubTest {
  subTest: SubTest;
  status: TaskStatus;
  estimatedDuration: string;
  completedAt?: string;
}

export interface DiagnosticSession {
  id: string;
  status: 'not_started' | 'in_progress' | 'completed';
  subTests: DiagnosticSubTest[];
  startedAt?: string;
  completedAt?: string;
}

export interface DiagnosticResult {
  subTest: SubTest;
  scoreRange: string;
  confidence: Confidence;
  strengths: string[];
  issues: string[];
  readiness: number;
  criterionBreakdown: CriterionFeedback[];
}

// ══════════════════════════════════════════════════════
//                    MOCK DATA FIXTURES
// ══════════════════════════════════════════════════════

export const MOCK_USER: UserProfile = {
  id: 'mock-user-001',
  email: 'learner@oet-prep.dev',
  displayName: 'Faisal Maqsood',
  profession: 'Nursing',
  examDate: '2025-09-15',
  targetScores: { Writing: 350, Speaking: 350, Reading: 350, Listening: 350 },
  previousAttempts: 1,
  weakSubTests: ['Writing', 'Speaking'],
  studyHoursPerWeek: 10,
  targetCountry: 'Australia',
  onboardingComplete: true,
  goalsComplete: true,
  diagnosticComplete: true,
  createdAt: '2025-01-15',
};

export const PROFESSIONS = [
  'Nursing', 'Medicine', 'Dentistry', 'Pharmacy', 'Physiotherapy',
  'Occupational Therapy', 'Speech Pathology', 'Podiatry', 'Radiography',
  'Optometry', 'Veterinary Science', 'Dietetics',
] as const;

// ─── STUDY PLAN ───────────────────────────────────

export const MOCK_STUDY_PLAN: StudyPlanTask[] = [
  {
    id: 'sp-001', title: 'Writing: Discharge Summary Practice', subTest: 'Writing',
    duration: '45 mins', rationale: 'Your Conciseness & Clarity score was below target. This task focuses on writing concise discharge summaries.',
    dueDate: '2025-06-15', status: 'not_started', section: 'today', contentId: 'wt-001', type: 'practice',
  },
  {
    id: 'sp-002', title: 'Speaking: Patient Handover Role Play', subTest: 'Speaking',
    duration: '20 mins', rationale: 'Fluency was flagged in your last attempt. This role play targets smoother transitions.',
    dueDate: '2025-06-15', status: 'not_started', section: 'today', contentId: 'st-001', type: 'roleplay',
  },
  {
    id: 'sp-003', title: 'Reading: Part C — Health Policy Text', subTest: 'Reading',
    duration: '30 mins', rationale: 'Part C comprehension had the most errors in your diagnostic.',
    dueDate: '2025-06-16', status: 'not_started', section: 'thisWeek', contentId: 'rt-001', type: 'practice',
  },
  {
    id: 'sp-004', title: 'Listening: Consultation Extract Drill', subTest: 'Listening',
    duration: '15 mins', rationale: 'Distractor recognition needs improvement based on mock results.',
    dueDate: '2025-06-17', status: 'not_started', section: 'thisWeek', contentId: 'lt-001', type: 'drill',
  },
  {
    id: 'sp-005', title: 'Writing: Referral Letter (Cardiology)', subTest: 'Writing',
    duration: '45 mins', rationale: 'Scheduled for Genre & Style criterion practice.',
    dueDate: '2025-06-20', status: 'not_started', section: 'nextCheckpoint', contentId: 'wt-002', type: 'practice',
  },
  {
    id: 'sp-006', title: 'Speaking: Better Phrasing Drills', subTest: 'Speaking',
    duration: '10 mins', rationale: 'Vocabulary range was your weakest criterion last time.',
    dueDate: '2025-06-18', status: 'completed', section: 'today', contentId: 'ph-001', type: 'drill',
  },
  {
    id: 'sp-007', title: 'Mock: Full OET Mock Test #3', subTest: 'Writing',
    duration: '3 hrs', rationale: 'Scheduled checkpoint to measure overall progress.',
    dueDate: '2025-06-22', status: 'not_started', section: 'nextCheckpoint', contentId: 'mock-003', type: 'mock',
  },
];

// ─── WRITING ──────────────────────────────────────

export const MOCK_WRITING_TASKS: WritingTask[] = [
  {
    id: 'wt-001', title: 'Discharge Summary — Post-Surgical Patient',
    difficulty: 'Medium', profession: 'Nursing', time: '45 mins',
    criteriaFocus: 'Conciseness & Clarity', scenarioType: 'Discharge Summary',
    caseNotes: `Patient: Mrs. Eleanor Vance, 72 years old\nAdmitted: 3rd June 2025\nDischarged: 10th June 2025\n\nDiagnosis: Right total knee replacement\n\nHistory: Osteoarthritis both knees x 5 years. Previous left knee replacement 2023 (uncomplicated). HTN controlled on Amlodipine 5mg daily. T2DM managed with Metformin 500mg BD. NKDA.\n\nCurrent admission:\n- Elective right TKR performed 4/6 under spinal anaesthesia\n- Post-op: mobilised day 1 with physiotherapy. Weight-bearing as tolerated.\n- Wound: clean, dry, staples intact. Removal due in 14 days (GP)\n- Pain: managed with Paracetamol 1g QID + Oxycodone 5mg PRN (max 4x/day)\n- BSL: slightly elevated post-op (8-12mmol/L), stabilised by day 5\n- DVT prophylaxis: Enoxaparin 40mg SC daily x 14 days\n\nDischarge Medications:\n1. Paracetamol 1g QID\n2. Oxycodone 5mg PRN (max 4 doses/day)\n3. Enoxaparin 40mg SC daily (14 days from discharge)\n4. Amlodipine 5mg daily\n5. Metformin 500mg BD\n\nFollow-up: Orthopaedic clinic 6 weeks. GP for staple removal 14 days. Physio 2x/week.`,
  },
  {
    id: 'wt-002', title: 'Referral Letter — Cardiology Consultation',
    difficulty: 'Hard', profession: 'Nursing', time: '45 mins',
    criteriaFocus: 'Genre & Style', scenarioType: 'Referral Letter',
    caseNotes: `Patient: Mr. David Chen, 58 years old\nDate of consultation: 12th June 2025\n\nReason for referral: New onset chest pain on exertion\n\nHistory: Previously well. Non-smoker. Occasional alcohol. BMI 29.\nFamily history: Father — MI at age 62. Mother — HTN.\n\nPresenting complaint:\n- 3-week history of central chest tightness on moderate exertion\n- Radiates to left arm occasionally\n- Resolves with rest (5-10 minutes)\n- No pain at rest, no orthopnoea, no PND\n- Denies palpitations or syncope\n\nExamination: BP 148/92, HR 78 regular, SpO2 98% RA. Heart sounds dual, no murmurs. Chest clear. No peripheral oedema.\n\nInvestigations:\n- ECG: Normal sinus rhythm, no ST changes\n- Bloods: Troponin <5 (normal), Lipids: TC 6.8, LDL 4.2, HDL 1.1\n- FBE, UEC, LFTs: within normal limits\n\nCurrent Management: Aspirin 100mg daily commenced. Atorvastatin 20mg nocte commenced. Lifestyle advice given.`,
  },
  {
    id: 'wt-003', title: 'Referral Letter — Paediatric Assessment',
    difficulty: 'Easy', profession: 'Nursing', time: '45 mins',
    criteriaFocus: 'Purpose', scenarioType: 'Referral Letter',
    caseNotes: `Patient: Lily Thompson, 4 years old\nParent/Guardian: Sarah Thompson (mother)\nDate: 14th June 2025\n\nReason for referral: Delayed speech and language development\n\nBackground:\n- Lily is the second of three children\n- Normal pregnancy and delivery\n- Met motor milestones on time (walked at 12 months)\n- First words at 18 months (slightly delayed)\n- Currently uses 2-3 word phrases; peers using 4-5 word sentences\n- Good comprehension of simple instructions\n- Attends daycare 3 days/week; carers report difficulty understanding her speech\n- No hearing concerns reported by parents\n\nAssessment:\n- Hearing screen: Passed (tympanometry normal bilaterally)\n- No behavioural concerns noted\n- Cooperative during assessment\n\nRequest: Speech pathology assessment and intervention planning.`,
  },
  {
    id: 'wt-004', title: 'Transfer Letter — ICU to Ward',
    difficulty: 'Medium', profession: 'Nursing', time: '45 mins',
    criteriaFocus: 'Content', scenarioType: 'Transfer Letter',
    caseNotes: `Patient: Mr. Ahmed Hassan, 45 years old\nTransfer: ICU → Surgical Ward\nDate: 15th June 2025\n\nAdmission reason: Emergency appendectomy — perforated appendix with localised peritonitis\nSurgery: Laparoscopic appendectomy 13/6, converted to open due to adhesions\n\nICU course (2 days):\n- Post-op sepsis: Temperature 39.2°C, WCC 18.5\n- IV Piperacillin-Tazobactam commenced\n- Required supplemental O2 via NP 2L for 24hrs, now on RA\n- Pain managed with PCA morphine, transitioned to oral analgesia\n- Wound: midline incision, clean, drain removed day 2\n- Oral intake resumed day 2 — tolerating light diet\n\nCurrent status:\n- Afebrile x 24hrs\n- Pain controlled on Paracetamol 1g QID + Ibuprofen 400mg TDS\n- Mobilising with assistance\n- Continue IV antibiotics for 5 more days\n\nOutstanding: Blood cultures pending. Surgical review day 5 post-op.`,
  },
  {
    id: 'wt-005', title: 'Discharge Summary — Mental Health',
    difficulty: 'Hard', profession: 'Nursing', time: '45 mins',
    criteriaFocus: 'Organisation & Layout', scenarioType: 'Discharge Summary',
    caseNotes: `Patient: Ms. Rebecca Foster, 34 years old\nAdmitted: 1st June 2025 (voluntary admission)\nDischarged: 14th June 2025\n\nDiagnosis: Major Depressive Episode (recurrent), Generalised Anxiety Disorder\n\nBackground:\n- Previous episode 2022 — treated with Sertraline 100mg (discontinued by patient)\n- Presenting this admission: 6-week history of worsening low mood, poor sleep, reduced appetite, social withdrawal, passive suicidal ideation (no plan/intent)\n- Trigger: Relationship breakdown + job loss\n\nInpatient management:\n- Commenced Escitalopram 10mg daily, titrated to 20mg by day 10\n- Individual CBT sessions 3x/week\n- Group therapy daily\n- Sleep hygiene program\n- Occupational therapy engagement\n\nProgress:\n- Mood improved from PHQ-9 score 22 (admission) to 11 (discharge)\n- Sleep normalised by day 8\n- Appetite improved, weight stable\n- No further suicidal ideation from day 5\n- Engaged well with therapy, developing coping strategies\n\nDischarge plan:\n- Continue Escitalopram 20mg daily\n- Outpatient psychiatrist: Dr. Patel, appointment 21/6\n- Community mental health team follow-up within 48hrs\n- Crisis plan provided and discussed\n- Safety plan completed with patient`,
  },
  {
    id: 'wt-006', title: 'Referral Letter — Diabetes Management',
    difficulty: 'Medium', profession: 'Medicine', time: '45 mins',
    criteriaFocus: 'Language', scenarioType: 'Referral Letter',
    caseNotes: `Patient: Mr. George Williams, 62 years old\nDate: 16th June 2025\n\nReason for referral: Suboptimal diabetes control despite maximal oral therapy\n\nDiagnosis: Type 2 Diabetes Mellitus (diagnosed 2018)\n\nCurrent medications:\n- Metformin 1000mg BD\n- Gliclazide MR 120mg daily\n- Empagliflozin 25mg daily\n- Atorvastatin 40mg nocte\n- Perindopril 10mg daily\n\nRecent results:\n- HbA1c: 8.9% (target <7%); previous: 8.2% (3 months ago)\n- eGFR: 72 (stable)\n- Urine ACR: 5.2 (mildly elevated)\n- BMI: 32\n\nComplications:\n- Background diabetic retinopathy (stable, last ophthalmology review 3 months ago)\n- Peripheral neuropathy — bilateral feet, reduced sensation to monofilament\n\nLifestyle: Sedentary job. Reports difficulty with diet adherence. Lives alone.\n\nRequest: Review for consideration of insulin therapy or GLP-1 receptor agonist.`,
  },
];

const WRITING_CRITERIA = ['Purpose', 'Content', 'Conciseness & Clarity', 'Genre & Style', 'Organisation & Layout', 'Language'];

export const MOCK_WRITING_CHECKLIST: ChecklistItem[] = [
  { id: 1, text: 'Addressed the purpose of the letter clearly', completed: false },
  { id: 2, text: 'Included all relevant clinical information', completed: false },
  { id: 3, text: 'Used appropriate medical terminology', completed: false },
  { id: 4, text: 'Maintained professional tone throughout', completed: false },
  { id: 5, text: 'Checked letter format (date, salutation, sign-off)', completed: false },
  { id: 6, text: 'Proofread for grammar and spelling', completed: false },
];

export const MOCK_WRITING_RESULTS: Record<string, WritingResult> = {
  'wr-001': {
    id: 'wr-001', taskId: 'wt-001', taskTitle: 'Discharge Summary — Post-Surgical Patient',
    estimatedScoreRange: '330–360', estimatedGradeRange: 'B–B+',
    confidenceLabel: 'Medium',
    topStrengths: [
      'Clinical information accurately captured',
      'Medications clearly listed with dosages',
      'Follow-up plan well-structured',
    ],
    topIssues: [
      'Some unnecessary detail in surgical description',
      'Discharge summary could be more concise',
      'Minor grammatical errors in medication section',
    ],
    criteria: WRITING_CRITERIA.map((name, i) => ({
      name,
      score: [4, 5, 3, 4, 5, 4][i],
      maxScore: 6,
      grade: ['B', 'B+', 'C+', 'B', 'B+', 'B'][i],
      explanation: `Your ${name.toLowerCase()} demonstrates ${[4, 5, 3, 4, 5, 4][i] >= 4 ? 'solid competence' : 'areas for improvement'} in this criterion.`,
      anchoredComments: [
        { id: `ac-${i}-1`, text: 'Sample highlighted text from submission', comment: `This section could be improved for better ${name.toLowerCase()}.` },
      ],
      omissions: i === 1 ? ['Patient allergy status not mentioned in body'] : [],
      unnecessaryDetails: i === 2 ? ['Surgical technique details unnecessary for GP audience'] : [],
      revisionSuggestions: [`Consider revising to improve ${name.toLowerCase()}.`],
      strengths: [`Good demonstration of ${name.toLowerCase()} principles.`],
      issues: [4, 5, 3, 4, 5, 4][i] < 4 ? [`${name} needs more focused attention.`] : [],
    })),
    submittedAt: '2025-06-15T10:30:00Z',
    evalStatus: 'completed',
  },
};

export const MOCK_CRITERIA_DELTAS: CriteriaDelta[] = WRITING_CRITERIA.map((name, i) => ({
  name,
  original: [4, 5, 3, 4, 5, 4][i],
  revised: [5, 5, 4, 5, 5, 5][i],
  max: 6,
}));

export const MOCK_MODEL_ANSWER: ModelAnswer = {
  taskId: 'wt-001', taskTitle: 'Discharge Summary — Post-Surgical Patient', profession: 'Nursing',
  paragraphs: [
    {
      id: 'mp-1', text: 'Dear Dr. Patterson,\n\nRe: Mrs. Eleanor Vance, DOB: 15/03/1953\n\nI am writing to inform you of the above patient\'s recent admission and discharge from St. Mary\'s Hospital following a right total knee replacement.',
      rationale: 'Opening clearly states the purpose and identifies the patient. The "Re:" line follows standard medical correspondence format.',
      criteria: ['Purpose', 'Genre & Style'],
      included: ['Patient identification', 'Reason for correspondence', 'Hospital name'],
      excluded: ['Detailed medical history (saved for body)'],
      languageNotes: 'Formal register appropriate for GP correspondence. "I am writing to inform you" is a standard opening phrase.',
    },
    {
      id: 'mp-2', text: 'Mrs. Vance was admitted on 3rd June 2025 for an elective right TKR under spinal anaesthesia. Her background includes osteoarthritis of both knees, hypertension controlled with Amlodipine 5mg daily, and type 2 diabetes managed with Metformin 500mg BD. She has no known drug allergies. She had a previous uncomplicated left TKR in 2023.',
      rationale: 'Concise summary of relevant history and admission details. Only includes information the GP needs for ongoing management.',
      criteria: ['Content', 'Conciseness & Clarity'],
      included: ['Admission date', 'Procedure', 'Relevant comorbidities', 'Current medications', 'Allergy status', 'Previous surgery'],
      excluded: ['Non-relevant social history', 'Detailed pre-operative assessment'],
      languageNotes: 'Medical abbreviations (TKR, BD) appropriate for professional audience. Active voice used for clarity.',
    },
    {
      id: 'mp-3', text: 'Post-operatively, Mrs. Vance was mobilised on day one with physiotherapy and progressed to weight-bearing as tolerated. Her wound remained clean and dry with staples intact, due for removal by your practice in 14 days. Blood sugar levels were slightly elevated post-operatively (8-12 mmol/L) but stabilised by day five. DVT prophylaxis with Enoxaparin 40mg SC daily has been commenced for 14 days from discharge.',
      rationale: 'Post-operative course summarised with only clinically significant events. Clear handover of outstanding tasks to GP.',
      criteria: ['Content', 'Organisation & Layout'],
      included: ['Mobilisation status', 'Wound status + GP action required', 'BSL monitoring outcome', 'DVT prophylaxis plan'],
      excluded: ['Routine nursing observations', 'Detailed physio notes'],
      languageNotes: 'Passive voice used appropriately for clinical events. Time references are specific and clear.',
    },
    {
      id: 'mp-4', text: 'Discharge medications are as follows: Paracetamol 1g QID, Oxycodone 5mg PRN (maximum 4 doses daily), Enoxaparin 40mg SC daily for 14 days, Amlodipine 5mg daily, and Metformin 500mg BD.\n\nMrs. Vance has been referred to the orthopaedic clinic for review in six weeks and will attend physiotherapy twice weekly. Please arrange staple removal in 14 days.\n\nYours sincerely,\n[Nurse Name]\nRegistered Nurse, Orthopaedic Ward',
      rationale: 'Clear medication list, follow-up plan, and specific GP action. Professional sign-off follows standard format.',
      criteria: ['Organisation & Layout', 'Genre & Style', 'Language'],
      included: ['Complete medication list', 'Follow-up appointments', 'GP action item', 'Professional closing'],
      excluded: ['PRN medication usage during admission', 'Discharge condition assessment'],
      languageNotes: '"Please arrange" is a polite imperative appropriate for professional correspondence. Sign-off includes role and ward for identification.',
    },
  ],
};

export const MOCK_WRITING_SUBMISSIONS: WritingSubmission[] = [
  {
    id: 'ws-001', taskId: 'wt-001', taskTitle: 'Discharge Summary — Post-Surgical Patient',
    content: 'Dear Dr. Patterson...(draft content)', wordCount: 285,
    submittedAt: '2025-06-15T10:30:00Z', evalStatus: 'completed',
    scoreEstimate: '330–360', reviewStatus: 'not_requested',
  },
  {
    id: 'ws-002', taskId: 'wt-002', taskTitle: 'Referral Letter — Cardiology Consultation',
    content: 'Dear Cardiologist...(draft content)', wordCount: 210,
    submittedAt: '2025-06-14T14:00:00Z', evalStatus: 'completed',
    scoreEstimate: '310–340', reviewStatus: 'pending',
  },
  {
    id: 'ws-003', taskId: 'wt-003', taskTitle: 'Referral Letter — Paediatric Assessment',
    content: '(in progress...)', wordCount: 150,
    submittedAt: '2025-06-13T09:15:00Z', evalStatus: 'processing',
    reviewStatus: 'not_requested',
  },
];

// ─── SPEAKING ─────────────────────────────────────

export const MOCK_SPEAKING_TASKS: SpeakingTask[] = [
  { id: 'st-001', title: 'Patient Handover — Post-Op Recovery', scenarioType: 'Handover', difficulty: 'Medium', profession: 'Nursing', criteriaFocus: 'Fluency', duration: '20 mins' },
  { id: 'st-002', title: 'Breaking Bad News — Cancer Diagnosis', scenarioType: 'Consultation', difficulty: 'Hard', profession: 'Medicine', criteriaFocus: 'Empathy', duration: '20 mins' },
  { id: 'st-003', title: 'Medication Counselling — Warfarin', scenarioType: 'Counselling', difficulty: 'Easy', profession: 'Pharmacy', criteriaFocus: 'Vocabulary', duration: '15 mins' },
  { id: 'st-004', title: 'Discharge Planning — Elderly Patient', scenarioType: 'Discharge', difficulty: 'Medium', profession: 'Nursing', criteriaFocus: 'Structure', duration: '20 mins' },
  { id: 'st-005', title: 'Falls Assessment — Physiotherapy', scenarioType: 'Assessment', difficulty: 'Easy', profession: 'Physiotherapy', criteriaFocus: 'Pronunciation', duration: '15 mins' },
  { id: 'st-006', title: 'Informed Consent — Surgical Procedure', scenarioType: 'Consent', difficulty: 'Hard', profession: 'Medicine', criteriaFocus: 'Grammar', duration: '20 mins' },
];

export const MOCK_ROLE_CARDS: Record<string, RoleCard> = {
  'st-001': {
    id: 'st-001', title: 'Patient Handover — Post-Op Recovery',
    profession: 'Nursing', setting: 'Hospital surgical ward',
    patient: 'Mr. James Wheeler, 68, post right hip replacement (day 1)',
    brief: 'You are a registered nurse handing over care of Mr. Wheeler to the incoming night shift nurse. Provide a comprehensive handover covering his current status, medications, and any concerns.',
    tasks: [
      'Summarise the patient\'s surgical procedure and current condition',
      'Report on pain management and current PRN usage',
      'Highlight the DVT prophylaxis plan and mobilisation status',
      'Communicate any outstanding tasks or concerns for the next shift',
    ],
    background: 'Mr. Wheeler had an elective right total hip replacement this morning under general anaesthesia. He has a history of atrial fibrillation (on Apixaban, withheld pre-op) and GORD (Pantoprazole 40mg daily). He was mobilised to the chair this afternoon with physiotherapy assistance. His pain has been managed with a PCA morphine pump, transitioning to oral analgesia this evening.',
  },
  'st-002': {
    id: 'st-002', title: 'Breaking Bad News — Cancer Diagnosis',
    profession: 'Medicine', setting: 'Outpatient consultation room',
    patient: 'Mrs. Patricia Collins, 55, awaiting biopsy results',
    brief: 'You are a doctor who needs to inform Mrs. Collins that her breast biopsy has confirmed invasive ductal carcinoma. Use the SPIKES protocol to deliver the news with empathy and clarity.',
    tasks: [
      'Assess the patient\'s understanding and expectations',
      'Deliver the diagnosis clearly and compassionately',
      'Allow time for the patient to process the information',
      'Outline the next steps and provide reassurance about the treatment team',
    ],
    background: 'Mrs. Collins presented 3 weeks ago with a palpable lump in her left breast. Mammography and ultrasound showed a 2.3cm mass. Core biopsy was performed 5 days ago. Results confirm Grade 2 invasive ductal carcinoma, ER+/PR+, HER2-. She attended today expecting results and came alone.',
  },
};

export const MOCK_SPEAKING_RESULTS: Record<string, SpeakingResult> = {
  'sr-001': {
    id: 'sr-001', taskId: 'st-001', taskTitle: 'Patient Handover — Post-Op Recovery',
    scoreRange: '330–360', confidence: 'Medium',
    strengths: [
      'Clear and logical structure following ISBAR format',
      'Appropriate medical terminology used throughout',
      'Good pace and mostly natural flow',
    ],
    improvements: [
      'Some hesitation when transitioning between topics',
      'Could use more specific time references for PRN medications',
      'Register slightly informal in places',
    ],
    evalStatus: 'completed', submittedAt: '2025-06-14T16:00:00Z',
    nextDrill: { title: 'Fluency: Transition Phrases', description: 'Practice smooth topic transitions in clinical handovers', id: 'ph-002' },
  },
};

export const MOCK_TRANSCRIPT: Record<string, { title: string; date: string; duration: number; transcript: TranscriptLine[] }> = {
  'sr-001': {
    title: 'Patient Handover — Post-Op Recovery', date: '2025-06-14', duration: 312,
    transcript: [
      { id: 'tl-1', speaker: 'Nurse', text: 'Good evening, I\'m handing over the care of Mr. James Wheeler in bed 4. He\'s a 68-year-old gentleman who had a right total hip replacement this morning under general anaesthetic.', startTime: 0, endTime: 12 },
      { id: 'tl-2', speaker: 'Nurse', text: 'Um, his background includes atrial fibrillation — he\'s normally on Apixaban but that was withheld pre-op — and he takes Pantoprazole for reflux.', startTime: 12, endTime: 22, markers: [
        { id: 'm-1', type: 'fluency', startTime: 12, endTime: 13, text: 'Um, his background', suggestion: 'Avoid filler words. Try: "His relevant background includes..."' },
      ]},
      { id: 'tl-3', speaker: 'Nurse', text: 'So the surgery went well. He was mobbed to the chair this afternoon with physio. He managed about 15 minutes before wanting to go back to bed, which is actually pretty good for day one.', startTime: 22, endTime: 35, markers: [
        { id: 'm-2', type: 'vocabulary', startTime: 25, endTime: 27, text: 'He was mobbed to the chair', suggestion: '"Mobilised" is the correct clinical term, not "mobbed." Say: "He was mobilised to the chair."' },
        { id: 'm-3', type: 'structure', startTime: 31, endTime: 35, text: 'which is actually pretty good for day one', suggestion: 'This is too informal for a clinical handover. Try: "This is within expected parameters for day one post-operatively."' },
      ]},
      { id: 'tl-4', speaker: 'Nurse', text: 'Pain-wise, he\'s been on a PCA morphine pump but we\'re transitioning him to oral analgesia tonight. He\'s had Paracetamol one gram at 6pm and hasn\'t needed any breakthrough yet.', startTime: 35, endTime: 48 },
      { id: 'tl-5', speaker: 'Nurse', text: 'For the DVT thing, he needs to restart his Apixaban — I think it\'s tomorrow morning the surgical team said — and he\'s got TEDs on.', startTime: 48, endTime: 58, markers: [
        { id: 'm-4', type: 'vocabulary', startTime: 48, endTime: 51, text: 'For the DVT thing', suggestion: '"Thing" is vague. Use: "Regarding DVT prophylaxis" or "For venous thromboembolism prevention."' },
        { id: 'm-5', type: 'grammar', startTime: 52, endTime: 56, text: 'I think it\'s tomorrow morning the surgical team said', suggestion: 'Uncertain phrasing reduces confidence. Say: "The surgical team has advised restarting Apixaban tomorrow morning."' },
      ]},
      { id: 'tl-6', speaker: 'Nurse', text: 'Outstanding for tonight: keep an eye on his drain output — it was about 150 mLs last check. Neuro-vascular observations every four hours. And he\'s nil by mouth until the surgical review in the morning, just in case.', startTime: 58, endTime: 72, markers: [
        { id: 'm-6', type: 'empathy', startTime: 68, endTime: 72, text: 'just in case', suggestion: 'Vague reasoning. Specify the clinical rationale: "...as a precaution pending confirmation of surgical plan."' },
      ]},
    ],
  },
};

export const MOCK_PHRASING_DATA: Record<string, { title: string; segments: PhrasingSegment[] }> = {
  'sr-001': {
    title: 'Patient Handover — Post-Op Recovery',
    segments: [
      { id: 'ps-1', originalPhrase: 'Um, his background includes...', issueExplanation: 'Filler words ("um") reduce fluency and professional impression.', strongerAlternative: 'His relevant medical background includes...', drillPrompt: 'Practice saying "His relevant medical background includes atrial fibrillation and GORD" without hesitation.' },
      { id: 'ps-2', originalPhrase: 'He was mobbed to the chair', issueExplanation: '"Mobbed" is incorrect. The clinical term is "mobilised."', strongerAlternative: 'He was mobilised to the bedside chair', drillPrompt: 'Repeat: "The patient was mobilised to the bedside chair with physiotherapy assistance on day one."' },
      { id: 'ps-3', originalPhrase: 'which is actually pretty good for day one', issueExplanation: 'Too informal for clinical handover. Register should remain professional.', strongerAlternative: 'This is within expected parameters for day one post-operatively', drillPrompt: 'Practice: "His mobility progress is within expected parameters for the first post-operative day."' },
      { id: 'ps-4', originalPhrase: 'For the DVT thing', issueExplanation: '"Thing" is vague and unprofessional. Use specific clinical terminology.', strongerAlternative: 'Regarding DVT prophylaxis', drillPrompt: 'Practice: "Regarding DVT prophylaxis, the plan is to restart Apixaban tomorrow morning as advised by the surgical team."' },
      { id: 'ps-5', originalPhrase: 'I think it\'s tomorrow morning the surgical team said', issueExplanation: 'Uncertain phrasing ("I think") undermines handover reliability. State facts with confidence.', strongerAlternative: 'The surgical team has advised restarting Apixaban tomorrow morning', drillPrompt: 'Practice: "The surgical team has confirmed Apixaban should be restarted at 0800 tomorrow."' },
    ],
  },
};

// ─── READING ──────────────────────────────────────

export const MOCK_READING_TASKS: Record<string, ReadingTask> = {
  'rt-001': {
    id: 'rt-001', title: 'Health Policy — Hospital-Acquired Infections', part: 'C', timeLimit: 900,
    texts: [
      {
        id: 'rtxt-1', title: 'Hospital-Acquired Infections: Prevention Strategies',
        content: `Hospital-acquired infections (HAIs) remain one of the most significant challenges in modern healthcare. Despite advances in antimicrobial therapy and infection control practices, approximately 1 in 10 patients admitted to hospital will develop an HAI during their stay. The most common types include surgical site infections, catheter-associated urinary tract infections, central line-associated bloodstream infections, and ventilator-associated pneumonia.\n\nThe World Health Organization has identified hand hygiene as the single most important measure for preventing the spread of infection in healthcare settings. Studies consistently demonstrate that improved hand hygiene compliance can reduce HAI rates by 20-40%. However, compliance rates among healthcare workers remain suboptimal, typically ranging from 40-60% in most healthcare facilities.\n\nRecent evidence suggests that a multimodal approach to infection prevention is most effective. This includes not only hand hygiene programmes but also environmental cleaning protocols, antimicrobial stewardship, surveillance systems, and staff education. The concept of "bundles" — a structured way of improving care processes by combining a small number of evidence-based practices — has been particularly successful in reducing specific types of HAIs.\n\nEconomic analyses indicate that investment in infection prevention programmes typically yields a positive return, with estimated savings of $5-10 for every $1 spent on prevention measures. Beyond the financial impact, HAIs significantly increase patient morbidity and mortality, with attributable mortality rates ranging from 1% for urinary tract infections to over 25% for bloodstream infections.`,
      },
    ],
    questions: [
      { id: 'rq-1', number: 1, text: 'According to the passage, what proportion of hospital patients will develop an HAI?', type: 'short_answer', correctAnswer: 'approximately 1 in 10' },
      { id: 'rq-2', number: 2, text: 'What does the WHO identify as the most important infection prevention measure?', type: 'short_answer', correctAnswer: 'hand hygiene' },
      { id: 'rq-3', number: 3, text: 'By what percentage can improved hand hygiene compliance reduce HAI rates?', type: 'short_answer', correctAnswer: '20-40%' },
      { id: 'rq-4', number: 4, text: 'What is described as a "structured way of improving care processes"?', type: 'mcq', options: ['Antimicrobial stewardship', 'Surveillance systems', 'Bundles', 'Staff education'], correctAnswer: 'Bundles' },
      { id: 'rq-5', number: 5, text: 'What is the estimated return on investment for infection prevention programmes?', type: 'short_answer', correctAnswer: '$5-10 for every $1 spent' },
      { id: 'rq-6', number: 6, text: 'Which type of HAI has the highest attributable mortality rate?', type: 'mcq', options: ['Urinary tract infections', 'Surgical site infections', 'Bloodstream infections', 'Ventilator-associated pneumonia'], correctAnswer: 'Bloodstream infections' },
    ],
  },
};

export const MOCK_READING_RESULTS: Record<string, ReadingResult> = {
  'rt-001': {
    taskId: 'rt-001', title: 'Health Policy — Hospital-Acquired Infections',
    score: 4, totalQuestions: 6, percentage: 67, grade: 'C+',
    errorClusters: [
      { type: 'Inference', count: 1, total: 2, percentage: 50 },
      { type: 'Detail Extraction', count: 1, total: 3, percentage: 33 },
      { type: 'Vocabulary', count: 0, total: 1, percentage: 0 },
    ],
    items: [
      { id: 'ri-1', number: 1, text: 'What proportion of hospital patients will develop an HAI?', userAnswer: '1 in 10', correctAnswer: 'approximately 1 in 10', isCorrect: true, errorType: '', explanation: 'Correct. The passage states "approximately 1 in 10 patients."' },
      { id: 'ri-2', number: 2, text: 'What does the WHO identify as the most important measure?', userAnswer: 'hand hygiene', correctAnswer: 'hand hygiene', isCorrect: true, errorType: '', explanation: 'Correct. The passage clearly states this.' },
      { id: 'ri-3', number: 3, text: 'By what percentage can improved hand hygiene reduce HAI rates?', userAnswer: '40-60%', correctAnswer: '20-40%', isCorrect: false, errorType: 'Detail Extraction', explanation: '40-60% refers to compliance rates, not infection reduction rates. The passage states improved compliance can reduce HAI rates by 20-40%.' },
      { id: 'ri-4', number: 4, text: 'What is described as a "structured way of improving care processes"?', userAnswer: 'Bundles', correctAnswer: 'Bundles', isCorrect: true, errorType: '', explanation: 'Correct. Bundles are defined as "a structured way of improving care processes."' },
      { id: 'ri-5', number: 5, text: 'What is the estimated return on investment?', userAnswer: '$10 for every $1', correctAnswer: '$5-10 for every $1 spent', isCorrect: false, errorType: 'Inference', explanation: 'Partially correct but incomplete. The passage states "$5-10 for every $1 spent," giving a range, not a single figure.' },
      { id: 'ri-6', number: 6, text: 'Which HAI has the highest attributable mortality rate?', userAnswer: 'Bloodstream infections', correctAnswer: 'Bloodstream infections', isCorrect: true, errorType: '', explanation: 'Correct. The passage states attributable mortality "over 25% for bloodstream infections."' },
    ],
  },
};

// ─── LISTENING ────────────────────────────────────

export const MOCK_LISTENING_TASKS: Record<string, ListeningTask> = {
  'lt-001': {
    id: 'lt-001', title: 'Consultation: Asthma Management Review',
    audioSrc: '',
    duration: 240,
    audioAvailable: false,
    audioUnavailableReason: 'Audio is not published for this fallback listening task yet.',
    transcriptPolicy: 'Use transcript-backed review until the recorded media asset is available.',
    questions: [
      { id: 'lq-1', number: 1, text: 'What is the patient\'s main concern during this consultation?', type: 'mcq', options: ['Increasing breathlessness at night', 'Side effects of current medication', 'Difficulty using the inhaler', 'Wanting to stop treatment'], correctAnswer: 'Increasing breathlessness at night' },
      { id: 'lq-2', number: 2, text: 'How often does the patient report using their reliever inhaler?', type: 'short_answer', correctAnswer: '3-4 times per week' },
      { id: 'lq-3', number: 3, text: 'What change does the doctor recommend to the treatment plan?', type: 'mcq', options: ['Increase preventer inhaler dose', 'Add oral steroids', 'Switch to a combination inhaler', 'Refer to a specialist'], correctAnswer: 'Switch to a combination inhaler' },
    ],
  },
};

export const MOCK_LISTENING_RESULTS: Record<string, ListeningResult> = {
  'lt-001': {
    id: 'lt-001', title: 'Consultation: Asthma Management Review',
    score: 2, total: 3,
    questions: [
      { id: 'lrq-1', number: 1, text: 'What is the patient\'s main concern?', userAnswer: 'Increasing breathlessness at night', correctAnswer: 'Increasing breathlessness at night', isCorrect: true, explanation: 'The patient clearly states they have been waking up at night with shortness of breath more frequently.', allowTranscriptReveal: true, transcriptExcerpt: 'Patient: "I\'ve been waking up at night feeling quite breathless, it\'s been happening more and more over the past two weeks."' },
      { id: 'lrq-2', number: 2, text: 'How often does the patient use their reliever inhaler?', userAnswer: 'daily', correctAnswer: '3-4 times per week', isCorrect: false, explanation: 'The patient says "three or four times a week," not daily. Listen carefully for specific frequency mentions.', allowTranscriptReveal: true, transcriptExcerpt: 'Doctor: "How often would you say you\'re using your blue inhaler?" Patient: "Um, maybe three or four times a week? Sometimes more if I\'ve been walking a lot."', distractorExplanation: 'The patient mentions using it "sometimes more" when walking, which might suggest daily use, but the stated frequency is 3-4 times per week.' },
      { id: 'lrq-3', number: 3, text: 'What change does the doctor recommend?', userAnswer: 'Switch to a combination inhaler', correctAnswer: 'Switch to a combination inhaler', isCorrect: true, explanation: 'The doctor recommends a combination inhaler that includes both preventer and long-acting reliever components.', allowTranscriptReveal: false },
    ],
    recommendedDrill: { id: 'drill-001', title: 'Number & Frequency Detection Drill', description: 'Practice identifying specific numbers, frequencies, and quantities in medical consultations.' },
  },
};

// ─── MOCKS ────────────────────────────────────────

export const MOCK_REPORTS: Record<string, MockReport> = {
  'mock-001': {
    id: 'mock-001', title: 'Full OET Mock Test #1', date: '2025-06-01',
    overallScore: '340',
    summary: 'Solid performance across most sub-tests. Writing and Speaking show clear areas for improvement, particularly in conciseness and fluency respectively.',
    subTests: [
      { id: 'ms-r', name: 'Reading', score: '370', rawScore: '38/42', color: '#2563eb', bg: '#dbeafe' },
      { id: 'ms-l', name: 'Listening', score: '350', rawScore: '35/42', color: '#4f46e5', bg: '#e0e7ff' },
      { id: 'ms-w', name: 'Writing', score: '320', rawScore: '24/36', color: '#e11d48', bg: '#ffe4e6' },
      { id: 'ms-s', name: 'Speaking', score: '330', rawScore: 'N/A', color: '#7c3aed', bg: '#ede9fe' },
    ],
    weakestCriterion: { subtest: 'Writing', criterion: 'Conciseness & Clarity', description: 'Your writing tends to include unnecessary clinical details. Focus on what the reader needs to know.' },
    priorComparison: { exists: false, priorMockName: '', overallTrend: 'flat', details: 'This is your first mock test. Complete another to see comparisons.' },
  },
  'mock-002': {
    id: 'mock-002', title: 'Full OET Mock Test #2', date: '2025-06-10',
    overallScore: '355',
    summary: 'Improvement visible across all sub-tests. Reading remains your strongest area. Writing conciseness has improved but Genre & Style needs attention.',
    subTests: [
      { id: 'ms-r', name: 'Reading', score: '380', rawScore: '39/42', color: '#2563eb', bg: '#dbeafe' },
      { id: 'ms-l', name: 'Listening', score: '360', rawScore: '36/42', color: '#4f46e5', bg: '#e0e7ff' },
      { id: 'ms-w', name: 'Writing', score: '340', rawScore: '27/36', color: '#e11d48', bg: '#ffe4e6' },
      { id: 'ms-s', name: 'Speaking', score: '340', rawScore: 'N/A', color: '#7c3aed', bg: '#ede9fe' },
    ],
    weakestCriterion: { subtest: 'Writing', criterion: 'Genre & Style', description: 'Letter formatting and register need more consistency. Ensure salutations, closings and tone match the intended reader.' },
    priorComparison: { exists: true, priorMockName: 'Full OET Mock Test #1', overallTrend: 'up', details: 'Overall score improved by 15 points. Biggest gain in Writing (+20). Reading and Listening also improved.' },
  },
};

// ─── READINESS ────────────────────────────────────

export const MOCK_READINESS: ReadinessData = {
  targetDate: '2025-09-15',
  weeksRemaining: 13,
  overallRisk: 'Moderate',
  recommendedStudyHours: 12,
  weakestLink: 'Writing — Conciseness & Clarity',
  subTests: [
    { id: 'rd-w', name: 'Writing', readiness: 62, target: 80, status: 'Needs attention', color: '#e11d48', bg: '#fff1f2', barColor: '#fb7185', isWeakest: true },
    { id: 'rd-s', name: 'Speaking', readiness: 68, target: 80, status: 'On track', color: '#7c3aed', bg: '#f5f3ff', barColor: '#a78bfa' },
    { id: 'rd-r', name: 'Reading', readiness: 82, target: 80, status: 'Target met', color: '#2563eb', bg: '#eff6ff', barColor: '#60a5fa' },
    { id: 'rd-l', name: 'Listening', readiness: 76, target: 80, status: 'Almost there', color: '#4f46e5', bg: '#eef2ff', barColor: '#818cf8' },
  ],
  blockers: [
    { id: 1, title: 'Writing conciseness repeatedly below threshold', description: 'Your last 3 writing submissions scored below 4/6 on Conciseness & Clarity.' },
    { id: 2, title: 'Speaking fluency flagged in 2 role plays', description: 'Filler words and hesitations noted. Consider doing 2-3 additional phrasing drills per week.' },
  ],
  evidence: {
    mocksCompleted: 2,
    practiceQuestions: 48,
    expertReviews: 1,
    recentTrend: 'Improving',
    lastUpdated: '2025-06-15T08:00:00Z',
  },
};

// ─── PROGRESS ─────────────────────────────────────

export const MOCK_TREND_DATA: TrendPoint[] = [
  { date: 'Week 1', reading: 340, listening: 320, writing: 290, speaking: 300 },
  { date: 'Week 2', reading: 350, listening: 330, writing: 300, speaking: 310 },
  { date: 'Week 3', reading: 360, listening: 340, writing: 310, speaking: 320 },
  { date: 'Week 4', reading: 370, listening: 350, writing: 320, speaking: 330 },
  { date: 'Week 5', reading: 380, listening: 360, writing: 340, speaking: 340 },
];

export const MOCK_COMPLETION_DATA = [
  { day: 'Mon', completed: 3 },
  { day: 'Tue', completed: 2 },
  { day: 'Wed', completed: 4 },
  { day: 'Thu', completed: 1 },
  { day: 'Fri', completed: 3 },
  { day: 'Sat', completed: 5 },
  { day: 'Sun', completed: 2 },
];

export const MOCK_SUBMISSION_VOLUME = [
  { week: 'W1', submissions: 4 },
  { week: 'W2', submissions: 6 },
  { week: 'W3', submissions: 5 },
  { week: 'W4', submissions: 8 },
  { week: 'W5', submissions: 7 },
];

// ─── SUBMISSIONS HISTORY ──────────────────────────

export const MOCK_SUBMISSIONS: Submission[] = [
  {
    id: 'sub-001',
    contentId: 'wt-001',
    taskName: 'Discharge Summary - Post-Surgical Patient',
    subTest: 'Writing',
    attemptDate: '2025-06-15',
    scoreEstimate: '330-360',
    reviewStatus: 'not_requested',
    comparisonGroupId: 'cmp-writing-001',
    canRequestReview: true,
    actions: {
      reopenFeedbackRoute: '/app/submissions/sub-001',
      compareRoute: '/app/submissions/compare?left=sub-001',
      requestReviewRoute: '/app/submissions/sub-001?requestReview=1',
    },
  },
  {
    id: 'sub-002',
    contentId: 'sp-001',
    taskName: 'Patient Handover - Post-Op Recovery',
    subTest: 'Speaking',
    attemptDate: '2025-06-14',
    scoreEstimate: '330-360',
    reviewStatus: 'pending',
    evaluationId: 'eval-sp-002',
    comparisonGroupId: 'cmp-speaking-001',
    canRequestReview: true,
    actions: {
      reopenFeedbackRoute: '/app/submissions/sub-002',
      compareRoute: '/app/submissions/compare?left=sub-002',
      requestReviewRoute: '/app/submissions/sub-002?requestReview=1',
    },
  },
  {
    id: 'sub-003',
    contentId: 'rt-001',
    taskName: 'HAI Prevention - Part C',
    subTest: 'Reading',
    attemptDate: '2025-06-13',
    scoreEstimate: '67%',
    reviewStatus: 'not_requested',
    comparisonGroupId: 'cmp-reading-001',
    canRequestReview: false,
    actions: {
      reopenFeedbackRoute: '/app/submissions/sub-003',
      compareRoute: '/app/submissions/compare?left=sub-003',
      requestReviewRoute: null,
    },
  },
  {
    id: 'sub-004',
    contentId: 'lt-001',
    taskName: 'Asthma Management Review',
    subTest: 'Listening',
    attemptDate: '2025-06-12',
    scoreEstimate: '66%',
    reviewStatus: 'not_requested',
    comparisonGroupId: 'cmp-listening-001',
    canRequestReview: false,
    actions: {
      reopenFeedbackRoute: '/app/submissions/sub-004',
      compareRoute: '/app/submissions/compare?left=sub-004',
      requestReviewRoute: null,
    },
  },
  {
    id: 'sub-005',
    contentId: 'wt-002',
    taskName: 'Referral Letter - Cardiology',
    subTest: 'Writing',
    attemptDate: '2025-06-10',
    scoreEstimate: '310-340',
    reviewStatus: 'reviewed',
    comparisonGroupId: 'cmp-writing-002',
    canRequestReview: false,
    actions: {
      reopenFeedbackRoute: '/app/submissions/sub-005',
      compareRoute: '/app/submissions/compare?left=sub-005',
      requestReviewRoute: null,
    },
  },
];

// ─── BILLING ──────────────────────────────────────

export const MOCK_BILLING: BillingData = {
  currentPlan: 'Premium Monthly',
  currentPlanId: 'premium-monthly',
  price: '$49.99',
  interval: 'monthly',
  status: 'Active',
  nextRenewal: '2025-07-15',
  reviewCredits: 3,
  entitlements: {
    productiveSkillReviewsEnabled: true,
    supportedReviewSubtests: ['writing', 'speaking'],
    invoiceDownloadsAvailable: true,
  },
  plans: [
    {
      id: 'starter-monthly',
      label: 'Starter Monthly',
      tier: 'starter',
      description: 'Self-study access for learners who want guided practice without review credits.',
      price: '$19.99',
      interval: 'monthly',
      reviewCredits: 0,
      canChangeTo: true,
      changeDirection: 'downgrade',
      badge: 'Base plan',
    },
    {
      id: 'premium-monthly',
      label: 'Premium Monthly',
      tier: 'premium',
      description: 'Adds productive-skill review capacity and richer learner evidence surfaces.',
      price: '$49.99',
      interval: 'monthly',
      reviewCredits: 3,
      canChangeTo: false,
      changeDirection: 'current',
      badge: 'Current plan',
    },
    {
      id: 'pro-monthly',
      label: 'Pro Monthly',
      tier: 'pro',
      description: 'Higher monthly review-credit allowance for intensive OET preparation.',
      price: '$79.99',
      interval: 'monthly',
      reviewCredits: 8,
      canChangeTo: true,
      changeDirection: 'upgrade',
      badge: 'Best for heavy review',
    },
  ],
  extras: [
    {
      id: 'review-credit-pack-2',
      quantity: 2,
      price: '$24.99',
      description: 'Two extra expert-review credits for Writing or Speaking.',
    },
    {
      id: 'review-credit-pack-5',
      quantity: 5,
      price: '$54.99',
      description: 'Five extra expert-review credits for productive-skill submissions.',
    },
  ],
  invoices: [
    { id: 'inv-003', date: '2025-06-15', amount: '$49.99', status: 'Paid', currency: 'USD', downloadUrl: '/v1/billing/invoices/inv-003/download' },
    { id: 'inv-002', date: '2025-05-15', amount: '$49.99', status: 'Paid', currency: 'USD', downloadUrl: '/v1/billing/invoices/inv-002/download' },
    { id: 'inv-001', date: '2025-04-15', amount: '$49.99', status: 'Paid', currency: 'USD', downloadUrl: '/v1/billing/invoices/inv-001/download' },
  ],
};

// ─── EXPERT REVIEW ────────────────────────────────

export const MOCK_TURNAROUND_OPTIONS: TurnaroundOption[] = [
  { id: 'standard', label: 'Standard', time: '48–72 hours', cost: 1, description: 'Detailed written feedback within 3 business days' },
  { id: 'express', label: 'Express', time: '24 hours', cost: 2, description: 'Priority review returned within 24 hours' },
];

export const MOCK_FOCUS_AREAS: FocusArea[] = [
  { id: 'purpose', label: 'Purpose', description: 'How well the letter achieves its communicative goal' },
  { id: 'content', label: 'Content', description: 'Accuracy and relevance of clinical information' },
  { id: 'conciseness', label: 'Conciseness & Clarity', description: 'Avoiding unnecessary detail; clear expression' },
  { id: 'genre', label: 'Genre & Style', description: 'Letter format, register, and tone' },
  { id: 'organisation', label: 'Organisation & Layout', description: 'Logical structure and paragraph organisation' },
  { id: 'language', label: 'Language', description: 'Grammar, vocabulary, and spelling' },
];

// ─── DIAGNOSTIC ───────────────────────────────────

export const MOCK_DIAGNOSTIC_SESSION: DiagnosticSession = {
  id: 'diag-001',
  status: 'not_started',
  subTests: [
    { subTest: 'Writing', status: 'not_started', estimatedDuration: '45 mins' },
    { subTest: 'Speaking', status: 'not_started', estimatedDuration: '20 mins' },
    { subTest: 'Reading', status: 'not_started', estimatedDuration: '30 mins' },
    { subTest: 'Listening', status: 'not_started', estimatedDuration: '25 mins' },
  ],
};

export const MOCK_DIAGNOSTIC_RESULTS: DiagnosticResult[] = [
  {
    subTest: 'Writing', scoreRange: '300–330', confidence: 'Medium',
    readiness: 55,
    strengths: ['Good clinical content inclusion', 'Appropriate medical terminology'],
    issues: ['Letter structure needs work', 'Conciseness is a key area for improvement'],
    criterionBreakdown: WRITING_CRITERIA.map((name, i) => ({
      name, score: [3, 4, 2, 3, 3, 3][i], maxScore: 6,
      grade: ['C', 'B-', 'D+', 'C', 'C', 'C'][i],
      explanation: `Initial diagnostic shows ${[3, 4, 2, 3, 3, 3][i] >= 3 ? 'developing' : 'foundational'} ability in ${name.toLowerCase()}.`,
      anchoredComments: [], omissions: [], unnecessaryDetails: [], revisionSuggestions: [],
      strengths: [], issues: [],
    })),
  },
  {
    subTest: 'Speaking', scoreRange: '310–340', confidence: 'Low',
    readiness: 60,
    strengths: ['Generally clear pronunciation', 'Good engagement with patient'],
    issues: ['Frequent filler words', 'Informal register in clinical contexts'],
    criterionBreakdown: [],
  },
  {
    subTest: 'Reading', scoreRange: '350–380', confidence: 'High',
    readiness: 78,
    strengths: ['Strong vocabulary', 'Good inference skills'],
    issues: ['Occasional detail extraction errors', 'Time management on Part C'],
    criterionBreakdown: [],
  },
  {
    subTest: 'Listening', scoreRange: '330–360', confidence: 'Medium',
    readiness: 70,
    strengths: ['Good comprehension of main ideas', 'Accurate note-taking'],
    issues: ['Distractor questions need practice', 'Specific number/frequency capture'],
    criterionBreakdown: [],
  },
];
