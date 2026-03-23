import type {
  Attempt,
  ContentItem,
  Criterion,
  EnrollmentSession,
  ExamType,
  Evaluation,
  LearnerGoal,
  LearnerSettingsWorkspaceData,
  Profession,
  ReadinessSnapshot,
  ReviewRequest,
  StudyPlan,
  Subscription,
  TargetCountry,
  UserProfile,
  WalletCredits,
} from "@/types/oet";

export const examTypes: ExamType[] = [
  {
    code: "OET",
    description: "Occupational English Test preparation and enrollment.",
    id: "oet",
    label: "OET",
    status: "active",
  },
  {
    code: "IELTS",
    description: "IELTS preparation and session enrollment.",
    id: "ielts",
    label: "IELTS",
    status: "active",
  },
];

export const professions: Profession[] = [
  {
    countryTargets: ["Australia", "New Zealand"],
    description: "Registered nurse and clinical nursing candidates.",
    examTypeIds: ["oet"],
    id: "nursing",
    label: "Nursing",
    status: "active",
  },
  {
    countryTargets: ["United Kingdom", "Australia"],
    description: "Doctors and physicians preparing for healthcare pathways.",
    examTypeIds: ["oet"],
    id: "medicine",
    label: "Medicine",
    status: "active",
  },
  {
    countryTargets: ["Ireland", "Australia"],
    description: "Pharmacists and pharmacy practice candidates.",
    examTypeIds: ["oet"],
    id: "pharmacy",
    label: "Pharmacy",
    status: "active",
  },
  {
    countryTargets: ["United Kingdom", "New Zealand"],
    description: "Dental professionals and dentistry applicants.",
    examTypeIds: ["oet"],
    id: "dentistry",
    label: "Dentistry",
    status: "active",
  },
  {
    countryTargets: ["Canada", "United Kingdom", "Australia"],
    description: "General academic and migration IELTS candidates.",
    examTypeIds: ["ielts"],
    id: "academic-english",
    label: "Academic / General English",
    status: "active",
  },
];

export const targetCountries: TargetCountry[] = [
  { id: "australia", label: "Australia", status: "active" },
  { id: "new-zealand", label: "New Zealand", status: "active" },
  { id: "united-kingdom", label: "United Kingdom", status: "active" },
  { id: "ireland", label: "Ireland", status: "active" },
  { id: "canada", label: "Canada", status: "active" },
];

export const enrollmentSessions: EnrollmentSession[] = [
  {
    capacity: 40,
    currency: "USD",
    deliveryMode: "online",
    description: "Live OET nursing cohort with writing and speaking feedback.",
    endDate: "2026-06-28",
    enrollmentOpen: true,
    examTypeId: "oet",
    id: "session-oet-nursing-apr",
    name: "OET Nursing April Cohort",
    priceLabel: "$299",
    professionIds: ["nursing"],
    seatsRemaining: 11,
    startDate: "2026-04-06",
    status: "open",
    timezone: "Asia/Karachi",
  },
  {
    capacity: 32,
    currency: "USD",
    deliveryMode: "hybrid",
    description: "OET medicine intensive focused on case notes and role-play.",
    endDate: "2026-07-05",
    enrollmentOpen: true,
    examTypeId: "oet",
    id: "session-oet-medicine-may",
    name: "OET Medicine Intensive",
    priceLabel: "$349",
    professionIds: ["medicine", "dentistry", "pharmacy"],
    seatsRemaining: 9,
    startDate: "2026-05-11",
    status: "open",
    timezone: "Asia/Karachi",
  },
  {
    capacity: 60,
    currency: "USD",
    deliveryMode: "online",
    description: "IELTS foundation session for academic and migration tracks.",
    endDate: "2026-06-01",
    enrollmentOpen: true,
    examTypeId: "ielts",
    id: "session-ielts-foundation-apr",
    name: "IELTS Foundation Sprint",
    priceLabel: "$199",
    professionIds: ["academic-english"],
    seatsRemaining: 21,
    startDate: "2026-04-20",
    status: "open",
    timezone: "Asia/Karachi",
  },
  {
    capacity: 45,
    currency: "USD",
    deliveryMode: "online",
    description: "Weekend IELTS cohort with timed mock practice and review.",
    endDate: "2026-07-19",
    enrollmentOpen: false,
    examTypeId: "ielts",
    id: "session-ielts-weekend-may",
    name: "IELTS Weekend Cohort",
    priceLabel: "$239",
    professionIds: ["academic-english"],
    seatsRemaining: 0,
    startDate: "2026-05-23",
    status: "closed",
    timezone: "Asia/Karachi",
  },
];

export const writingCriteria: Criterion[] = [
  {
    description: "Shows clear purpose throughout the letter.",
    id: "purpose",
    name: "Purpose",
    subtest: "writing",
  },
  {
    description: "Selects relevant case-note details accurately.",
    id: "content",
    name: "Content",
    subtest: "writing",
  },
  {
    description: "Remains concise and easy to follow under exam timing.",
    id: "clarity",
    name: "Conciseness & Clarity",
    subtest: "writing",
  },
  {
    description: "Matches professional register and task genre.",
    id: "genre",
    name: "Genre & Style",
    subtest: "writing",
  },
  {
    description: "Organizes information into a usable structure.",
    id: "organisation",
    name: "Organisation & Layout",
    subtest: "writing",
  },
  {
    description: "Maintains accurate grammar, vocabulary, and cohesion.",
    id: "language",
    name: "Language",
    subtest: "writing",
  },
];

export const speakingCriteria: Criterion[] = [
  {
    description: "Speech is easy for the patient to understand.",
    id: "intelligibility",
    name: "Intelligibility",
    subtest: "speaking",
  },
  {
    description: "Speech flows with appropriate pacing and control.",
    id: "fluency",
    name: "Fluency",
    subtest: "speaking",
  },
  {
    description: "Language choice is accurate and clinically appropriate.",
    id: "appropriateness",
    name: "Appropriateness of Language",
    subtest: "speaking",
  },
  {
    description: "Grammar and expression support precise explanations.",
    id: "resources",
    name: "Resources of Grammar and Expression",
    subtest: "speaking",
  },
  {
    description: "The candidate builds rapport and empathy with the patient.",
    id: "relationship",
    name: "Relationship Building",
    subtest: "speaking",
  },
  {
    description:
      "The candidate checks and uses the patient's perspective well.",
    id: "patient-perspective",
    name: "Understanding and Incorporating Patient Perspective",
    subtest: "speaking",
  },
];

export const learnerProfile: UserProfile = {
  avatarUrl: "/images/avatar/1.png",
  email: "learner@oet.app",
  fullName: "Hannah Musa",
  id: "learner-1001",
  professionId: "nursing",
  role: "learner",
  username: "learner@oet.app",
};

export const assignedLearners: UserProfile[] = [
  learnerProfile,
  {
    avatarUrl: "/images/avatar/2.png",
    email: "marco@oet.app",
    fullName: "Marco Alvarez",
    id: "learner-1002",
    professionId: "medicine",
    role: "learner",
    username: "marco@oet.app",
  },
  {
    avatarUrl: "/images/avatar/3.png",
    email: "amina@oet.app",
    fullName: "Amina Yusuf",
    id: "learner-1003",
    professionId: "pharmacy",
    role: "learner",
    username: "amina@oet.app",
  },
];

export const learnerGoal: LearnerGoal = {
  examDate: "2026-06-27",
  previousAttempts: ["Mock A completed", "Diagnostic completed"],
  professionId: "nursing",
  subtestTargets: {
    listening: "380-430",
    reading: "370-420",
    speaking: "360-410",
    writing: "350-400",
  },
  targetCountry: "Australia",
  weakSubtests: ["writing", "speaking"],
  weeklyStudyHours: 10,
};

export const studyPlan: StudyPlan = {
  checkpoint: {
    date: "2026-03-29",
    summary: "Complete one Writing revision and one Speaking empathy drill.",
    title: "Weekend checkpoint",
  },
  thisWeek: [
    {
      dueDate: "2026-03-23",
      durationMinutes: 45,
      id: "plan-1",
      reason:
        "Purpose and Content are the biggest blockers in recent Writing work.",
      status: "up-next",
      subtest: "writing",
      title: "Referral letter revision sprint",
    },
    {
      dueDate: "2026-03-24",
      durationMinutes: 30,
      id: "plan-2",
      reason: "Recent Speaking transcript shows missed empathy responses.",
      status: "scheduled",
      subtest: "speaking",
      title: "Empathy and reassurance drill",
    },
    {
      dueDate: "2026-03-25",
      durationMinutes: 25,
      id: "plan-3",
      reason: "Reading accuracy slips under timed skimming.",
      status: "scheduled",
      subtest: "reading",
      title: "Part A speed calibration",
    },
    {
      dueDate: "2026-03-27",
      durationMinutes: 35,
      id: "plan-4",
      reason: "Listening distractor traps still cluster in Part C.",
      status: "scheduled",
      subtest: "listening",
      title: "Part C distractor drill",
    },
  ],
  today: [
    {
      dueDate: "2026-03-23",
      durationMinutes: 45,
      id: "plan-1",
      reason:
        "Purpose and Content are the biggest blockers in recent Writing work.",
      status: "up-next",
      subtest: "writing",
      title: "Referral letter revision sprint",
    },
    {
      dueDate: "2026-03-23",
      durationMinutes: 20,
      id: "plan-5",
      reason: "Keeps the streak alive with a quick confidence win.",
      status: "scheduled",
      subtest: "reading",
      title: "Part B warm-up set",
    },
  ],
};

export const readinessSnapshot: ReadinessSnapshot = {
  examDate: "2026-06-27",
  overallLabel: "Target score is realistic with steady weekly completion.",
  remainingStudyHours: 68,
  subtests: [
    {
      confidence: "moderate",
      readinessLabel: "Close to target",
      scoreRange: "350-390",
      subtest: "writing",
    },
    {
      confidence: "moderate",
      readinessLabel: "Needs speaking review support",
      scoreRange: "340-380",
      subtest: "speaking",
    },
    {
      confidence: "high",
      readinessLabel: "Stable",
      scoreRange: "370-410",
      subtest: "reading",
    },
    {
      confidence: "high",
      readinessLabel: "Stable",
      scoreRange: "380-420",
      subtest: "listening",
    },
  ],
  weakestLink: "Speaking relationship-building under time pressure",
};

export const contentItems: ContentItem[] = [
  {
    criteriaFocus: ["purpose", "content", "organisation"],
    difficulty: "target",
    durationMinutes: 45,
    id: "writing-task-1",
    professionId: "nursing",
    scenarioType: "Discharge referral",
    status: "published",
    subtest: "writing",
    title: "Referral to community wound clinic",
  },
  {
    criteriaFocus: ["clarity", "genre", "language"],
    difficulty: "stretch",
    durationMinutes: 45,
    id: "writing-task-2",
    professionId: "medicine",
    scenarioType: "Transfer letter",
    status: "published",
    subtest: "writing",
    title: "Transfer to respiratory specialist",
  },
  {
    criteriaFocus: ["relationship", "patient-perspective", "appropriateness"],
    difficulty: "target",
    durationMinutes: 20,
    id: "speaking-task-1",
    professionId: "nursing",
    scenarioType: "Explaining medication side effects",
    status: "published",
    subtest: "speaking",
    title: "Post-operative pain discussion",
  },
  {
    criteriaFocus: ["fluency", "resources", "intelligibility"],
    difficulty: "foundation",
    durationMinutes: 20,
    id: "speaking-task-2",
    professionId: "pharmacy",
    scenarioType: "Checking adherence",
    status: "published",
    subtest: "speaking",
    title: "Medication counselling follow-up",
  },
  {
    criteriaFocus: ["speed", "accuracy"],
    difficulty: "target",
    durationMinutes: 20,
    id: "reading-task-1",
    scenarioType: "Part A",
    status: "published",
    subtest: "reading",
    title: "Emergency triage quick scan",
  },
  {
    criteriaFocus: ["distractors", "detail"],
    difficulty: "target",
    durationMinutes: 30,
    id: "listening-task-1",
    scenarioType: "Part C",
    status: "published",
    subtest: "listening",
    title: "Consultant discussion: asthma management",
  },
  {
    criteriaFocus: ["endurance", "integration"],
    difficulty: "stretch",
    durationMinutes: 180,
    id: "mock-1",
    professionId: "nursing",
    scenarioType: "Full exam simulation",
    status: "published",
    subtest: "writing",
    title: "Full OET simulation pack A",
  },
];

export const attempts: Attempt[] = [
  {
    contentItemId: "writing-task-1",
    id: "attempt-writing-1",
    mode: "timed",
    startedAt: "2026-03-22T08:30:00Z",
    status: "completed",
    submittedAt: "2026-03-22T09:13:00Z",
    subtest: "writing",
  },
  {
    contentItemId: "speaking-task-1",
    id: "attempt-speaking-1",
    mode: "practice",
    startedAt: "2026-03-22T12:10:00Z",
    status: "processing",
    submittedAt: "2026-03-22T12:32:00Z",
    subtest: "speaking",
  },
  {
    contentItemId: "reading-task-1",
    id: "attempt-reading-1",
    mode: "practice",
    startedAt: "2026-03-21T06:45:00Z",
    status: "completed",
    submittedAt: "2026-03-21T07:05:00Z",
    subtest: "reading",
  },
  {
    contentItemId: "listening-task-1",
    id: "attempt-listening-1",
    mode: "practice",
    startedAt: "2026-03-20T18:15:00Z",
    status: "completed",
    submittedAt: "2026-03-20T18:45:00Z",
    subtest: "listening",
  },
];

export const evaluations: Evaluation[] = [
  {
    attemptId: "attempt-writing-1",
    confidence: "moderate",
    criterionScores: [
      {
        criterionId: "purpose",
        improvements: ["State the referral purpose in the opening sentence."],
        scoreBand: "B",
        strengths: ["Purpose is generally present and relevant."],
        summary: "Purpose becomes clearer after the first paragraph.",
      },
      {
        criterionId: "content",
        improvements: ["Remove duplicate wound-care chronology."],
        scoreBand: "C+",
        strengths: ["Includes current dressing and infection status."],
        summary: "Relevant detail is present but slightly over-supplied.",
      },
      {
        criterionId: "clarity",
        improvements: ["Use shorter sentences for handover instructions."],
        scoreBand: "B",
        strengths: ["Main recommendation is understandable."],
        summary: "Clarity drops where multiple actions are joined together.",
      },
      {
        criterionId: "genre",
        improvements: ["Reduce informal phrasing in the closing paragraph."],
        scoreBand: "B",
        strengths: ["Tone is mostly appropriate for a referral."],
        summary: "Register is appropriate with a few conversational phrases.",
      },
      {
        criterionId: "organisation",
        improvements: ["Group social and discharge information together."],
        scoreBand: "C+",
        strengths: ["Paragraphs follow a logical order overall."],
        summary: "Layout is usable but some information appears twice.",
      },
      {
        criterionId: "language",
        improvements: ["Tighten article use and singular/plural control."],
        scoreBand: "B",
        strengths: ["Clinical vocabulary is accurate."],
        summary:
          "Language is accurate enough but still inconsistent under time pressure.",
      },
    ],
    feedback: [
      {
        anchorLabel: "Opening paragraph",
        criterionId: "purpose",
        detail:
          "Make the request explicit sooner so the reader understands the referral immediately.",
        id: "fb-writing-1",
        severity: "warning",
        title: "Purpose is delayed",
      },
      {
        anchorLabel: "Case notes selection",
        criterionId: "content",
        detail:
          "You included several wound-dressing changes that the receiving clinician does not need.",
        id: "fb-writing-2",
        severity: "warning",
        title: "Some unnecessary detail",
      },
      {
        anchorLabel: "Action summary",
        criterionId: "organisation",
        detail:
          "A short concluding action summary would make the letter easier to act on.",
        id: "fb-writing-3",
        severity: "info",
        title: "Stronger close needed",
      },
    ],
    id: "eval-writing-1",
    scoreRange: "350-390",
    status: "completed",
    subtest: "writing",
    summary:
      "The letter is clinically relevant and promising, but purpose, trimming, and clearer organisation will unlock the next band.",
  },
  {
    attemptId: "attempt-speaking-1",
    confidence: "low",
    criterionScores: [
      {
        criterionId: "relationship",
        improvements: ["Acknowledge the patient's worry before giving advice."],
        scoreBand: "C+",
        strengths: ["Tone remains professional throughout."],
        summary:
          "Clinical content is sound, but empathy cues are sometimes missed.",
      },
      {
        criterionId: "patient-perspective",
        improvements: [
          "Check what the patient already knows before continuing.",
        ],
        scoreBand: "C+",
        strengths: ["You ask one useful checking question."],
        summary: "Perspective-taking is present but not sustained.",
      },
      {
        criterionId: "intelligibility",
        improvements: ["Slow slightly during longer explanations."],
        scoreBand: "B",
        strengths: ["Key phrases remain understandable."],
        summary: "Speech is generally clear with occasional rushed delivery.",
      },
    ],
    feedback: [
      {
        anchorLabel: "00:42",
        criterionId: "relationship",
        detail:
          "You moved into instructions before acknowledging the patient's concern about pain.",
        id: "fb-speaking-1",
        severity: "warning",
        title: "Empathy opportunity missed",
      },
      {
        anchorLabel: "01:17",
        criterionId: "patient-perspective",
        detail:
          "Ask a short checking question before explaining the side-effect plan.",
        id: "fb-speaking-2",
        severity: "info",
        title: "Could check understanding sooner",
      },
    ],
    id: "eval-speaking-1",
    scoreRange: "340-380",
    status: "processing",
    subtest: "speaking",
    summary:
      "Initial signals point to strong clinical control with weaker rapport-building under pressure.",
  },
  {
    attemptId: "attempt-reading-1",
    confidence: "high",
    criterionScores: [],
    feedback: [
      {
        detail:
          "Most errors came from scanning the wrong subsection before reading the keyword in full.",
        id: "fb-reading-1",
        severity: "info",
        title: "Part A scanning drift",
      },
    ],
    id: "eval-reading-1",
    scoreRange: "370-410",
    status: "completed",
    subtest: "reading",
    summary:
      "Accuracy is stable; improvement now depends on faster elimination in Part A.",
  },
  {
    attemptId: "attempt-listening-1",
    confidence: "high",
    criterionScores: [],
    feedback: [
      {
        detail:
          "Part C distractors still catch you when the speaker restates the same point with different emphasis.",
        id: "fb-listening-1",
        severity: "info",
        title: "Distractor clustering in Part C",
      },
    ],
    id: "eval-listening-1",
    scoreRange: "380-420",
    status: "completed",
    subtest: "listening",
    summary:
      "Listening is a strength; focus on controlling late-section distractors.",
  },
];

export const reviewRequests: ReviewRequest[] = [
  {
    assignedReviewer: "Dr. Samuel Okafor",
    attemptId: "attempt-writing-1",
    dueAt: "2026-03-24T12:00:00Z",
    focusAreas: ["Purpose", "Organisation & Layout"],
    id: "review-writing-1",
    learnerId: "learner-1001",
    priority: "standard",
    reviewerNotes:
      "Please focus on whether the opening is direct enough for a busy community clinician.",
    status: "in-review",
    subtest: "writing",
  },
  {
    attemptId: "attempt-speaking-1",
    dueAt: "2026-03-24T18:00:00Z",
    focusAreas: ["Empathy", "Checking understanding"],
    id: "review-speaking-1",
    learnerId: "learner-1001",
    priority: "priority",
    reviewerNotes:
      "Please review the balance between explanation and relationship-building.",
    status: "queued",
    subtest: "speaking",
  },
];

export const subscription: Subscription = {
  creditsRemaining: 3,
  currentPlan: "Plus Review Pack",
  invoices: [
    {
      amountLabel: "$49.00",
      id: "inv-1001",
      issuedAt: "2026-03-01",
      status: "paid",
    },
    {
      amountLabel: "$19.00",
      id: "inv-1002",
      issuedAt: "2026-03-15",
      status: "paid",
    },
  ],
  renewalDate: "2026-04-01",
};

export const walletCredits: WalletCredits = {
  available: 3,
  reserved: 1,
  total: 4,
};

export const dashboardSummary = {
  latestEvaluatedSubmission: "Referral to community wound clinic",
  nextExamDate: "27 Jun 2026",
  nextMockRecommendation: "Full simulation pack A in 9 days",
  pendingExpertReviews: reviewRequests.length,
  streakMomentum: "6-day completion streak",
  todayCompletionTarget: "2 focused tasks",
};

export const progressDatasets = {
  completionTrend: [
    { label: "Week 1", value: 4 },
    { label: "Week 2", value: 5 },
    { label: "Week 3", value: 6 },
    { label: "Week 4", value: 7 },
  ],
  criterionTrend: [
    { label: "Purpose", value: 68 },
    { label: "Content", value: 64 },
    { label: "Clarity", value: 72 },
    { label: "Empathy", value: 61 },
  ],
  submissionVolume: [
    { label: "Writing", value: 9 },
    { label: "Speaking", value: 6 },
    { label: "Reading", value: 11 },
    { label: "Listening", value: 8 },
  ],
  subtestTrend: [
    { label: "Writing", value: 370 },
    { label: "Speaking", value: 360 },
    { label: "Reading", value: 395 },
    { label: "Listening", value: 401 },
  ],
};

export const writingCaseNotes = [
  "82-year-old patient discharged after lower-leg wound treatment.",
  "Lives alone; community nursing follow-up arranged.",
  "Wound improving but still requires dressing changes three times weekly.",
  "Type 2 diabetes managed with metformin.",
  "Needs review for pain control and infection warning signs.",
];

export const speakingTranscriptSegments = [
  {
    end: "00:21",
    issue: "Good rapport opening",
    speaker: "candidate",
    start: "00:00",
    text: "Good morning, Mrs Peters. I can see you're worried about the pain after surgery.",
  },
  {
    end: "00:54",
    issue: "Empathy miss",
    speaker: "candidate",
    start: "00:22",
    text: "You need to keep taking the tablets and report any nausea or dizziness to us immediately.",
  },
  {
    end: "01:21",
    issue: "Checking understanding opportunity",
    speaker: "candidate",
    start: "00:55",
    text: "These side effects can happen, but they are usually manageable if we adjust timing and food intake.",
  },
];

export const speakingBetterPhrasing = [
  {
    better:
      "I understand that this feels unsettling. Let me explain what is common and what to do if it happens.",
    explanation:
      "This version acknowledges emotion before moving into instructions.",
    issue: "Empathy miss",
    original:
      "You need to keep taking the tablets and report any nausea or dizziness to us immediately.",
    prompt: "Repeat with a calm, reassuring tone before the instruction.",
  },
  {
    better:
      "Before I go on, what have you already been told about these side effects?",
    explanation:
      "A short checking question keeps the interaction collaborative.",
    issue: "Weak understanding check",
    original:
      "These side effects can happen, but they are usually manageable if we adjust timing and food intake.",
    prompt: "Practice pausing and inviting the patient perspective.",
  },
];

export const writingModelAnswer = {
  annotations: [
    "Opening sentence states the referral purpose immediately.",
    "Second paragraph groups current clinical status before social context.",
    "Final paragraph gives a short requested-action summary.",
  ],
  contentId: "writing-task-1",
  rationale: [
    "The model answer excludes earlier dressing changes because they do not affect ongoing management.",
    "Clinical language remains direct without becoming overly technical for the receiving clinician.",
    "Each paragraph maps cleanly to one reader need: reason, current status, requested follow-up.",
  ],
};

export const mockReports = [
  {
    comparisonSummary:
      "Writing rose by 15 points since the previous mock; Speaking is unchanged.",
    id: "mock-1",
    strongestArea: "Listening stability",
    subtestBreakdown: [
      { label: "Writing", value: "360-390" },
      { label: "Speaking", value: "340-380" },
      { label: "Reading", value: "380-420" },
      { label: "Listening", value: "390-430" },
    ],
    weakestCriterion: "Speaking relationship building",
  },
];

export const expertQueueRows = [
  {
    aiConfidence: "Moderate",
    assignedReviewer: "Dr. Samuel Okafor",
    learner: "Hannah Musa",
    priority: "Standard",
    profession: "Nursing",
    reviewId: "review-writing-1",
    slaDue: "24 Mar, 12:00",
    status: "In review",
    subtest: "Writing",
  },
  {
    aiConfidence: "Low",
    assignedReviewer: "Unassigned",
    learner: "Hannah Musa",
    priority: "Priority",
    profession: "Nursing",
    reviewId: "review-speaking-1",
    slaDue: "24 Mar, 18:00",
    status: "Queued",
    subtest: "Speaking",
  },
  {
    aiConfidence: "High",
    assignedReviewer: "Dr. Samuel Okafor",
    learner: "Marco Alvarez",
    priority: "Standard",
    profession: "Medicine",
    reviewId: "review-writing-2",
    slaDue: "25 Mar, 10:30",
    status: "Queued",
    subtest: "Writing",
  },
];

export const expertMetrics = {
  averageTurnaround: "9h 20m",
  casesThisWeek: 24,
  calibrationAlignment: "91%",
  overdueCases: 2,
};

export const calibrationCases = [
  {
    benchmark: "Target band B",
    caseId: "cal-1",
    learner: "Amina Yusuf",
    note: "Disagreement centered on whether content trimming was sufficient.",
    reviewerAlignment: "88%",
  },
  {
    benchmark: "Target band C+",
    caseId: "cal-2",
    learner: "Marco Alvarez",
    note: "Speaking empathy markers were scored inconsistently.",
    reviewerAlignment: "84%",
  },
];

export const adminContentLibrary = contentItems.map((item, index) => ({
  difficulty: item.difficulty,
  id: item.id,
  profession: item.professionId ?? "multi",
  revisions: 2 + index,
  status: item.status,
  subtest: item.subtest,
  title: item.title,
}));

export const adminRevisions = [
  {
    author: "Priya Raman",
    contentId: "writing-task-1",
    createdAt: "2026-03-18",
    revisionId: "rev-1001",
    summary: "Clarified follow-up instructions and criteria tags.",
  },
  {
    author: "Priya Raman",
    contentId: "writing-task-1",
    createdAt: "2026-03-11",
    revisionId: "rev-1000",
    summary: "Updated case notes to better match current wound-care pathways.",
  },
];

export const taxonomyRows = professions.map((profession) => ({
  examTypes: profession.examTypeIds.join(", ").toUpperCase(),
  countryTargets: profession.countryTargets.join(", "),
  id: profession.id,
  label: profession.label,
  status: profession.status,
}));

export const examTypeRows = examTypes.map((examType) => ({
  code: examType.code,
  description: examType.description,
  id: examType.id,
  label: examType.label,
  status: examType.status,
}));

export const enrollmentSessionRows = enrollmentSessions.map((session) => ({
  dates: `${session.startDate} -> ${session.endDate}`,
  examTypeId: session.examTypeId,
  id: session.id,
  name: session.name,
  priceLabel: session.priceLabel,
  seats: `${session.seatsRemaining}/${session.capacity}`,
  status: session.status,
}));

export const aiConfigRows = [
  {
    label: "Active model",
    value: "oet-eval-v3.2",
  },
  {
    label: "Writing confidence route",
    value: "Escalate to expert below moderate confidence",
  },
  {
    label: "Speaking transcription mode",
    value: "Hybrid polling + live-ready adapter",
  },
  {
    label: "Experiment flags",
    value: "Speaking empathy prompt A/B enabled",
  },
];

export const reviewOpsCards = [
  { label: "Queued reviews", value: 18 },
  { label: "Priority reviews", value: 5 },
  { label: "Overdue", value: 2 },
  { label: "Median SLA", value: "11h" },
];

export const qualityAnalyticsRows = [
  {
    label: "AI-human disagreement",
    value: "8.4%",
  },
  {
    label: "Content flagged for confusion",
    value: "3 items",
  },
  {
    label: "Feature adoption",
    value: "72% revision-mode usage",
  },
  {
    label: "Risk cases",
    value: "4 high-variance speaking evaluations",
  },
];

export const adminUsers = [
  {
    credits: 3,
    id: "learner-1001",
    name: "Hannah Musa",
    plan: "Plus Review Pack",
    role: "Learner",
    status: "Active",
  },
  {
    credits: 2,
    id: "expert-2001",
    name: "Dr. Samuel Okafor",
    plan: "Reviewer",
    role: "Expert",
    status: "Active",
  },
  {
    credits: 0,
    id: "admin-3001",
    name: "Priya Raman",
    plan: "Admin",
    role: "Admin",
    status: "Active",
  },
];

export const adminBillingRows = [
  {
    account: "Hannah Musa",
    amount: "$49.00",
    invoiceId: "inv-1001",
    status: "Paid",
  },
  {
    account: "Hannah Musa",
    amount: "$19.00",
    invoiceId: "inv-1002",
    status: "Paid",
  },
  {
    account: "Marco Alvarez",
    amount: "$49.00",
    invoiceId: "inv-1003",
    status: "Pending",
  },
];

export const featureFlags = [
  {
    description: "Shows better-phrasing repeat drill prompts",
    flag: "speaking_better_phrase_v1",
    status: "Enabled",
  },
  {
    description:
      "Routes low-confidence writing evaluations to expert review nudges",
    flag: "writing_confidence_routing",
    status: "Enabled",
  },
  {
    description: "Displays mobile low-bandwidth mode controls",
    flag: "mobile_low_bandwidth_mode",
    status: "Enabled",
  },
];

export const learnerSettingsWorkspace: LearnerSettingsWorkspaceData = {
  activity: [
    {
      category: "study",
      description:
        "The adaptive plan promoted your Writing sprint after two low-confidence evaluations.",
      id: "settings-activity-1",
      timestamp: "2026-03-24 08:15",
      title: "Writing plan updated",
      tone: "primary",
    },
    {
      category: "review",
      description:
        "An expert review reminder was delivered because your Speaking queue is still awaiting feedback.",
      id: "settings-activity-2",
      timestamp: "2026-03-23 19:30",
      title: "Review alert sent",
      tone: "info",
    },
    {
      category: "session",
      description:
        "A second laptop session was observed in Lahore and kept active for 18 minutes.",
      id: "settings-activity-3",
      timestamp: "2026-03-23 14:05",
      title: "New device confirmed",
      tone: "warning",
    },
    {
      category: "security",
      description:
        "Two-step verification is currently enabled through email backup delivery.",
      id: "settings-activity-4",
      timestamp: "2026-03-22 09:40",
      title: "Security posture stable",
      tone: "success",
    },
  ],
  activitySummary: {
    activeDevices: 3,
    lastLoginAt: "2026-03-24 08:15",
    reviewMinutesThisWeek: 85,
    studyMinutesThisWeek: 410,
  },
  connections: {
    browserNotifications: true,
    calendarSync: "google",
    captionsEnabled: true,
    headsetReady: true,
    lowBandwidthMode: featureFlags.some(
      (item) =>
        item.flag === "mobile_low_bandwidth_mode" && item.status === "Enabled"
    ),
    microphoneReady: true,
    playbackSpeed: "1.25x",
  },
  notifications: {
    browserPracticeReminders: true,
    emailReviewAlerts: true,
    inAppProgressDigest: true,
    reminderCadence: "balanced",
    reviewNudges: true,
    sessionReminders: true,
    weeklyPlanningDigest: true,
    whatsappReminders: false,
  },
  privacy: {
    analyticsOptIn: true,
    audioStorageConsent: true,
    expertSharingConsent: true,
    marketingEmailsEnabled: false,
    profileVisibility: "coached",
    transcriptRetention: "90-days",
  },
  profile: {
    avatarUrl: learnerProfile.avatarUrl,
    email: learnerProfile.email,
    fullName: learnerProfile.fullName,
    phoneNumber: "+92 300 1234567",
    preferredLanguage: "English",
    professionId: learnerGoal.professionId,
    targetCountry: learnerGoal.targetCountry ?? "Australia",
    timezone: "Asia/Karachi",
    username: learnerProfile.username,
  },
  security: {
    lastPasswordChanged: "2026-03-12 21:10",
    recoveryEmail: learnerProfile.email,
    trustedSessions: [
      {
        deviceName: "Current Windows Laptop",
        id: "session-current",
        ipAddress: "39.52.10.118",
        lastActiveAt: "2026-03-24 08:15",
        location: "Lahore, PK",
        platform: "Chrome on Windows 11",
        status: "current",
      },
      {
        deviceName: "MacBook Study Desk",
        id: "session-macbook",
        ipAddress: "182.191.44.81",
        lastActiveAt: "2026-03-23 19:30",
        location: "Lahore, PK",
        platform: "Safari on macOS",
        status: "active",
      },
      {
        deviceName: "iPhone Practice Mode",
        id: "session-iphone",
        ipAddress: "39.52.11.004",
        lastActiveAt: "2026-03-20 07:45",
        location: "Karachi, PK",
        platform: "iOS app session",
        status: "idle",
      },
    ],
    twoFactorEnabled: true,
    twoFactorMethod: "email",
  },
  subscription: {
    currentPlan: subscription.currentPlan,
    nextRenewal: subscription.renewalDate,
    paymentMethodLabel: "Visa ending in 4426",
    reminderChannel: "Email + in-app reminders",
    reservedCredits: walletCredits.reserved,
    reviewCredits: walletCredits.available,
  },
};

export const auditLogs = [
  {
    action: "Published updated writing task metadata",
    actor: "Priya Raman",
    id: "audit-1001",
    timestamp: "2026-03-22 09:14",
  },
  {
    action: "Changed speaking escalation threshold to moderate confidence",
    actor: "Priya Raman",
    id: "audit-1002",
    timestamp: "2026-03-21 16:40",
  },
];

export function getContentItemById(id: string): ContentItem | undefined {
  return contentItems.find((item) => item.id === id);
}

export function getAttemptById(id: string): Attempt | undefined {
  return attempts.find((attempt) => attempt.id === id);
}

export function getEvaluationById(id: string): Evaluation | undefined {
  return evaluations.find((evaluation) => evaluation.id === id);
}

export function getReviewRequestById(id: string): ReviewRequest | undefined {
  return reviewRequests.find((request) => request.id === id);
}

export function getLearnerById(id: string): UserProfile | undefined {
  return assignedLearners.find((learner) => learner.id === id);
}

export function getMockReportById(id: string) {
  return mockReports.find((report) => report.id === id);
}

export function getContentRevisionsById(contentId: string) {
  return adminRevisions.filter((revision) => revision.contentId === contentId);
}
