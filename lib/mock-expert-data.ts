/**
 * Centralised expert console mock data.
 * Every expert page imports from here instead of keeping inline data.
 */

import type {
  ReviewRequest,
  WritingReviewDetail,
  SpeakingReviewDetail,
  CalibrationCase,
  CalibrationNote,
  ExpertMetrics,
  ExpertCompletionData,
  ExpertSchedule,
  LearnerProfileExpanded,
  ExpertTranscriptLine,
  AIFlag,
} from './types/expert';

// ─── FIXED TIMESTAMP (hydration-safe) ─────────────

const NOW = 1710000000000;

// ─── REVIEW QUEUE ─────────────────────────────────

export const MOCK_REVIEW_QUEUE: ReviewRequest[] = [
  {
    id: 'REV-001',
    learnerId: 'L-123',
    learnerName: 'Alice Smith',
    profession: 'medicine',
    subTest: 'writing',
    type: 'writing',
    aiConfidence: 'low',
    priority: 'high',
    slaDue: new Date(NOW + 1000 * 60 * 60 * 2).toISOString(),
    status: 'queued',
    assignedReviewerId: 'EXP-01',
    assignedReviewerName: 'Dr. Expert',
    createdAt: new Date(NOW - 1000 * 60 * 60 * 24).toISOString(),
  },
  {
    id: 'REV-002',
    learnerId: 'L-456',
    learnerName: 'Bob Jones',
    profession: 'nursing',
    subTest: 'speaking',
    type: 'speaking',
    aiConfidence: 'high',
    priority: 'normal',
    slaDue: new Date(NOW + 1000 * 60 * 60 * 24).toISOString(),
    status: 'assigned',
    assignedReviewerId: 'EXP-01',
    assignedReviewerName: 'Dr. Expert',
    createdAt: new Date(NOW - 1000 * 60 * 60 * 2).toISOString(),
  },
  {
    id: 'REV-003',
    learnerId: 'L-789',
    learnerName: 'Charlie Brown',
    profession: 'dentistry',
    subTest: 'writing',
    type: 'writing',
    aiConfidence: 'medium',
    priority: 'normal',
    slaDue: new Date(NOW - 1000 * 60 * 60 * 1).toISOString(),
    status: 'overdue',
    createdAt: new Date(NOW - 1000 * 60 * 60 * 48).toISOString(),
  },
  {
    id: 'REV-004',
    learnerId: 'L-321',
    learnerName: 'Diana Lee',
    profession: 'pharmacy',
    subTest: 'speaking',
    type: 'speaking',
    aiConfidence: 'low',
    priority: 'high',
    slaDue: new Date(NOW + 1000 * 60 * 45).toISOString(),
    status: 'queued',
    createdAt: new Date(NOW - 1000 * 60 * 60 * 6).toISOString(),
  },
  {
    id: 'REV-005',
    learnerId: 'L-654',
    learnerName: 'Edward Park',
    profession: 'physiotherapy',
    subTest: 'writing',
    type: 'writing',
    aiConfidence: 'high',
    priority: 'low',
    slaDue: new Date(NOW + 1000 * 60 * 60 * 72).toISOString(),
    status: 'assigned',
    assignedReviewerId: 'EXP-02',
    assignedReviewerName: 'Dr. Reviewer',
    createdAt: new Date(NOW - 1000 * 60 * 60 * 12).toISOString(),
  },
];

// ─── WRITING REVIEW DETAIL ────────────────────────

export const MOCK_WRITING_REVIEW_DETAIL: WritingReviewDetail = {
  id: 'REV-001',
  learnerId: 'L-123',
  learnerName: 'Alice Smith',
  profession: 'medicine',
  subTest: 'writing',
  type: 'writing',
  aiConfidence: 'low',
  priority: 'high',
  slaDue: new Date(NOW + 1000 * 60 * 60 * 2).toISOString(),
  status: 'in_progress',
  createdAt: new Date(NOW - 1000 * 60 * 60 * 24).toISOString(),
  learnerResponse: `Dear Dr. Thornton,

Re: Mrs. Margaret Wilson, DOB 15/04/1958

Thank you for referring Mrs Wilson for assessment of her persistent lower back pain. I reviewed her in my clinic on the 14th of March 2024.

Mrs Wilson reports a six-month history of progressive lower back pain radiating to the left leg. The pain is exacerbated by prolonged sitting and bending. She rates the pain as 7/10 on average. She has tried over-the-counter analgesics with limited relief.

On examination, there was tenderness over the L4-L5 region with reduced range of motion in lumbar flexion. Straight leg raise was positive on the left at 45 degrees. Neurological examination of the lower limbs was otherwise unremarkable.

An MRI of the lumbar spine revealed a moderate disc protrusion at L4-L5 with mild neural foraminal narrowing on the left.

I have commenced Mrs Wilson on a course of physiotherapy and prescribed a short course of NSAIDs. I have also referred her for a surgical opinion given the disc protrusion.

I would be grateful if you could continue to manage her hypertension and monitor her renal function while she is on NSAIDs.

Please do not hesitate to contact me if you require further information.

Yours sincerely,
Dr. Sarah Chen
Orthopaedic Specialist`,
  caseNotes: `TASK: You are a hospital doctor. Write a letter of referral to a specialist regarding a patient who has been experiencing persistent lower back pain. Use the information from the case notes below to write your letter.

PATIENT: Mrs Margaret Wilson, DOB 15/04/1958
DIAGNOSIS: Persistent lower back pain, L4-L5 disc protrusion
HISTORY: 6-month progressive LBP radiating to left leg. Pain 7/10. OTC analgesics ineffective.
EXAMINATION: Tenderness L4-L5, reduced ROM lumbar flexion, +ve SLR left at 45°, neuro otherwise NAD.
INVESTIGATIONS: MRI lumbar spine — moderate disc protrusion L4-L5, mild neural foraminal narrowing L.
MANAGEMENT: Commenced physiotherapy, short course NSAIDs, surgical referral.
SOCIAL: Retired teacher, lives alone, independent ADLs.
PMH: Hypertension (controlled), no diabetes.`,
  aiDraftFeedback: `AI Assessment Summary (Advisory Only — Score: ~300/500)

• Purpose: Letter addresses referral requirement adequately. Opening and closing formulae present.
• Content: Most case note information included. Some details about social history omitted.
• Conciseness: Generally concise. Some minor redundancy in examination findings.
• Genre & Style: Appropriate formal medical letter format. Professional tone maintained.
• Organisation: Logical flow from history → examination → investigations → management → request.
• Language: Generally accurate. Minor issues with article usage and complex sentence structure.

Key areas for expert review:
- Verify completeness of case note coverage
- Assess appropriateness of clinical terminology
- Check if closing request is sufficiently specific`,
  aiSuggestedScores: { purpose: 5, content: 4, conciseness: 5, genre: 5, organization: 5, language: 4 },
};

// ─── SPEAKING REVIEW DETAIL ───────────────────────

const MOCK_TRANSCRIPT_LINES: ExpertTranscriptLine[] = [
  { id: 'tl-1', speaker: 'interlocutor', startTime: 0, endTime: 5.2, text: 'Good morning. My name is Nurse Parker. I understand you have been experiencing some difficulties recently. Could you tell me what has been happening?' },
  { id: 'tl-2', speaker: 'candidate', startTime: 5.5, endTime: 12.8, text: 'Yes, good morning Nurse Parker. I have been feeling very tired and short of breath for the past two weeks. I also noticed my ankles have been quite swollen, especially in the evenings.' },
  { id: 'tl-3', speaker: 'interlocutor', startTime: 13.0, endTime: 17.5, text: 'I see. That must be concerning for you. Have you noticed any other symptoms, such as chest pain or difficulty sleeping?' },
  { id: 'tl-4', speaker: 'candidate', startTime: 18.0, endTime: 28.5, text: 'Well, I have been needing to use more pillows at night to sleep comfortably. Sometimes I wake up feeling like I cannot catch my breath. I have not had chest pain exactly, but there is a heaviness in my chest when I try to climb stairs.' },
  { id: 'tl-5', speaker: 'interlocutor', startTime: 29.0, endTime: 33.2, text: 'Thank you for explaining that. Can you tell me about your current medications and whether you have been taking them as prescribed?' },
  { id: 'tl-6', speaker: 'candidate', startTime: 33.5, endTime: 45.0, text: 'I am on several medications. I take lisinopril for my blood pressure, metformin for diabetes, and I was recently started on furosemide. To be honest, I have not been very good at remembering to take the furosemide because it makes me need the bathroom so frequently.' },
];

const MOCK_AI_FLAGS: AIFlag[] = [
  { id: 'af-1', type: 'pause', message: 'Extended pause detected (3.2s) — may indicate hesitation or word-finding difficulty.', timestampStart: 17.5, timestampEnd: 18.0, severity: 'info' },
  { id: 'af-2', type: 'grammar', message: 'Potential grammar issue: "I have not had chest pain exactly" — unusual word order.', timestampStart: 22.0, severity: 'warning' },
  { id: 'af-3', type: 'clinical', message: 'Candidate disclosed medication non-compliance — assess appropriateness of clinical communication response.', timestampStart: 38.0, timestampEnd: 45.0, severity: 'info' },
];

export const MOCK_SPEAKING_REVIEW_DETAIL: SpeakingReviewDetail = {
  id: 'REV-002',
  learnerId: 'L-456',
  learnerName: 'Bob Jones',
  profession: 'nursing',
  subTest: 'speaking',
  type: 'speaking',
  aiConfidence: 'high',
  priority: 'normal',
  slaDue: new Date(NOW + 1000 * 60 * 60 * 24).toISOString(),
  status: 'in_progress',
  createdAt: new Date(NOW - 1000 * 60 * 60 * 2).toISOString(),
  audioUrl: 'https://actions.google.com/sounds/v1/ambiences/coffee_shop.ogg',
  transcriptLines: MOCK_TRANSCRIPT_LINES,
  roleCard: {
    role: 'Nurse',
    setting: 'Community Health Centre',
    patient: 'Mr. David Thompson, 68 years old',
    task: 'Take a history from Mr. Thompson who has presented with worsening symptoms. Explore his current medications and compliance.',
    background: 'Mr. Thompson has a history of congestive heart failure, type 2 diabetes, and hypertension. He was discharged from hospital two weeks ago after an acute exacerbation.',
  },
  aiFlags: MOCK_AI_FLAGS,
  aiSuggestedScores: { intelligibility: 5, fluency: 4, appropriateness: 5, grammar: 4, clinicalCommunication: 5 },
};

// ─── CALIBRATION CASES ────────────────────────────

export const MOCK_CALIBRATION_CASES: CalibrationCase[] = [
  {
    id: 'CAL-001',
    title: 'Referral Letter – Nursing',
    profession: 'nursing',
    subTest: 'writing',
    type: 'writing',
    benchmarkScore: 340,
    reviewerScore: 350,
    status: 'completed',
    createdAt: new Date(NOW - 1000 * 60 * 60 * 72).toISOString(),
  },
  {
    id: 'CAL-002',
    title: 'Patient Consultation – Medicine',
    profession: 'medicine',
    subTest: 'speaking',
    type: 'speaking',
    benchmarkScore: 400,
    reviewerScore: 320,
    status: 'completed',
    createdAt: new Date(NOW - 1000 * 60 * 60 * 48).toISOString(),
  },
  {
    id: 'CAL-003',
    title: 'Discharge Summary – Dentistry',
    profession: 'dentistry',
    subTest: 'writing',
    type: 'writing',
    benchmarkScore: 380,
    status: 'pending',
    createdAt: new Date(NOW - 1000 * 60 * 60 * 24).toISOString(),
  },
];

// ─── CALIBRATION NOTES / HISTORY ──────────────────

export const MOCK_CALIBRATION_NOTES: CalibrationNote[] = [
  { id: 'CN-001', type: 'completed', message: 'Completed CAL-001 (Referral Letter – Nursing). Alignment: Aligned.', caseId: 'CAL-001', createdAt: new Date(NOW - 1000 * 60 * 60 * 70).toISOString() },
  { id: 'CN-002', type: 'comment', message: 'Content criterion scoring felt borderline — reviewed benchmark rubric notes for clarification.', caseId: 'CAL-001', createdAt: new Date(NOW - 1000 * 60 * 60 * 69).toISOString() },
  { id: 'CN-003', type: 'completed', message: 'Completed CAL-002 (Patient Consultation – Medicine). Alignment: Review Needed.', caseId: 'CAL-002', createdAt: new Date(NOW - 1000 * 60 * 60 * 46).toISOString() },
  { id: 'CN-004', type: 'system', message: 'New calibration case assigned: Discharge Summary – Dentistry (CAL-003).', caseId: 'CAL-003', createdAt: new Date(NOW - 1000 * 60 * 60 * 24).toISOString() },
];

// ─── EXPERT METRICS ───────────────────────────────

export const MOCK_EXPERT_METRICS: ExpertMetrics = {
  totalReviewsCompleted: 452,
  averageSlaCompliance: 98.5,
  averageCalibrationAlignment: 94.0,
  reworkRate: 1.2,
};

export const MOCK_EXPERT_COMPLETION_DATA: ExpertCompletionData[] = [
  { day: 'Mon', count: 12 },
  { day: 'Tue', count: 19 },
  { day: 'Wed', count: 15 },
  { day: 'Thu', count: 22 },
  { day: 'Fri', count: 28 },
  { day: 'Sat', count: 8 },
  { day: 'Sun', count: 5 },
];

// ─── EXPERT SCHEDULE ──────────────────────────────

export const MOCK_EXPERT_SCHEDULE: ExpertSchedule = {
  timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
  days: {
    monday:    { active: true, start: '09:00', end: '17:00' },
    tuesday:   { active: true, start: '09:00', end: '17:00' },
    wednesday: { active: true, start: '09:00', end: '17:00' },
    thursday:  { active: true, start: '09:00', end: '17:00' },
    friday:    { active: true, start: '09:00', end: '17:00' },
    saturday:  { active: false, start: '09:00', end: '17:00' },
    sunday:    { active: false, start: '09:00', end: '17:00' },
  },
};

// ─── LEARNER PROFILES ─────────────────────────────

export const MOCK_LEARNER_PROFILES: Record<string, LearnerProfileExpanded> = {
  'L-123': {
    id: 'L-123',
    name: 'Alice Smith',
    profession: 'medicine',
    goalScore: 'B (350+)',
    examDate: new Date(NOW + 1000 * 60 * 60 * 24 * 30).toISOString(),
    attemptsCount: 3,
    joinedAt: new Date(NOW - 1000 * 60 * 60 * 24 * 90).toISOString(),
    totalReviews: 2,
    subTestScores: [
      { subTest: 'writing', latestScore: 310, latestGrade: 'C+', attempts: 3 },
      { subTest: 'speaking', latestScore: 280, latestGrade: 'C', attempts: 1 },
      { subTest: 'reading', latestScore: 370, latestGrade: 'B', attempts: 2 },
      { subTest: 'listening', attempts: 0 },
    ],
    priorReviews: [
      { id: 'PR-001', type: 'writing', reviewerName: 'Dr. Expert', date: new Date(NOW - 1000 * 60 * 60 * 24 * 14).toISOString(), overallComment: 'Good structure and clinical terminology. Needs improvement in conciseness and closing request specificity.' },
      { id: 'PR-002', type: 'speaking', reviewerName: 'Dr. Reviewer', date: new Date(NOW - 1000 * 60 * 60 * 24 * 7).toISOString(), overallComment: 'Strong clinical communication. Minor fluency issues under pressure.' },
    ],
  },
  'L-456': {
    id: 'L-456',
    name: 'Bob Jones',
    profession: 'nursing',
    goalScore: 'B (350+)',
    examDate: new Date(NOW + 1000 * 60 * 60 * 24 * 45).toISOString(),
    attemptsCount: 5,
    joinedAt: new Date(NOW - 1000 * 60 * 60 * 24 * 120).toISOString(),
    totalReviews: 3,
    subTestScores: [
      { subTest: 'writing', latestScore: 340, latestGrade: 'C+', attempts: 4 },
      { subTest: 'speaking', latestScore: 360, latestGrade: 'B', attempts: 3 },
      { subTest: 'reading', latestScore: 390, latestGrade: 'B', attempts: 2 },
      { subTest: 'listening', latestScore: 350, latestGrade: 'B', attempts: 1 },
    ],
    priorReviews: [
      { id: 'PR-003', type: 'writing', reviewerName: 'Dr. Expert', date: new Date(NOW - 1000 * 60 * 60 * 24 * 21).toISOString(), overallComment: 'Well-structured letter with appropriate register. Content coverage could be more comprehensive.' },
    ],
  },
  'L-789': {
    id: 'L-789',
    name: 'Charlie Brown',
    profession: 'dentistry',
    goalScore: 'A (450+)',
    attemptsCount: 1,
    joinedAt: new Date(NOW - 1000 * 60 * 60 * 24 * 30).toISOString(),
    totalReviews: 0,
    subTestScores: [
      { subTest: 'writing', latestScore: 290, latestGrade: 'C', attempts: 1 },
      { subTest: 'speaking', attempts: 0 },
      { subTest: 'reading', attempts: 0 },
      { subTest: 'listening', attempts: 0 },
    ],
    priorReviews: [],
  },
};
