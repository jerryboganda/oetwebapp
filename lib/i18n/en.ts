// ── English Locale ────────────────────────────────────────────
// Default locale — all UI strings extracted for i18n support

const en = {
  // ── Common ──
  common: {
    loading: 'Loading…',
    error: 'Something went wrong',
    retry: 'Try Again',
    save: 'Save',
    cancel: 'Cancel',
    close: 'Close',
    back: 'Back',
    next: 'Next',
    submit: 'Submit',
    confirm: 'Confirm',
    delete: 'Delete',
    edit: 'Edit',
    search: 'Search',
    filter: 'Filter',
    noResults: 'No results found',
    viewAll: 'View All',
    learnMore: 'Learn More',
    signIn: 'Sign In',
    signOut: 'Sign Out',
    signUp: 'Create Account',
  },

  // ── Navigation ──
  nav: {
    dashboard: 'Dashboard',
    studyPlan: 'Study Plan',
    writing: 'Writing',
    speaking: 'Speaking',
    reading: 'Reading',
    listening: 'Listening',
    mocks: 'Mocks',
    readiness: 'Readiness',
    progress: 'Progress',
    subscriptions: 'Subscriptions',
    submissions: 'History',
    grammar: 'Grammar',
    lessons: 'Video Lessons',
    strategies: 'Strategies',
    pronunciation: 'Pronunciation',
    vocabulary: 'Vocabulary',
    review: 'Review',
    conversation: 'AI Conversation',
    community: 'Community',
    leaderboard: 'Leaderboard',
    achievements: 'Achievements',
    tutoring: 'Tutoring',
    examDates: 'Exam Dates',
    settings: 'Settings',
    help: 'Help & Support',
    marketplace: 'Marketplace',
  },

  // ── Auth ──
  auth: {
    email: 'Email address',
    password: 'Password',
    forgotPassword: 'Forgot password?',
    resetPassword: 'Reset Password',
    noAccount: "Don't have an account?",
    hasAccount: 'Already have an account?',
    welcomeBack: 'Welcome back',
    createAccount: 'Create your account',
  },

  // ── Dashboard ──
  dashboard: {
    welcomeTitle: 'Welcome back',
    todaysPlan: "Today's Plan",
    recentActivity: 'Recent Activity',
    readinessScore: 'Readiness Score',
    daysToExam: 'Days to exam',
    tasksCompleted: 'Tasks completed',
    streak: 'Day streak',
    level: 'Level',
  },

  // ── Subtests ──
  subtests: {
    writing: 'Writing',
    speaking: 'Speaking',
    reading: 'Reading',
    listening: 'Listening',
  },

  // ── Exam Types ──
  examTypes: {
    oet: 'OET',
    ielts: 'IELTS',
    pte: 'PTE',
  },

  // ── Difficulty ──
  difficulty: {
    easy: 'Easy',
    medium: 'Medium',
    hard: 'Hard',
  },

  // ── Review ──
  review: {
    spacedRepetition: 'Spaced Repetition Review',
    dueToday: 'Due Today',
    totalDue: 'Total Due',
    totalItems: 'Total Items',
    mastered: 'Mastered',
    startReview: 'Start Review',
    sessionComplete: 'Session Complete!',
    tapToReveal: 'Tap to reveal answer',
    howWellRecall: 'How well did you recall this?',
    again: 'Again',
    hard: 'Hard',
    okay: 'Okay',
    good: 'Good',
    easy: 'Easy',
    perfect: 'Perfect',
    reviewAgain: 'Review Again',
  },

  // ── Conversation ──
  conversation: {
    title: 'AI Conversation Practice',
    description: 'Practise speaking with an AI partner. Get real-time transcription and detailed evaluation.',
    startNew: 'Start a New Conversation',
    recentHistory: 'Recent Conversations',
    noConversations: 'No conversations yet',
    startFirst: 'Start your first AI conversation above',
    prepPhase: 'Preparation Phase',
    yourRole: 'Your Role',
    patient: 'Patient',
    objectives: 'Objectives',
    startNow: 'Start Now',
    endConversation: 'End Conversation',
    evaluating: 'Evaluating Your Conversation',
    evaluatingDesc: 'Our AI is analysing your performance. This usually takes a few seconds.',
    performance: 'Conversation Performance',
    criterionBreakdown: 'Criterion Breakdown',
    strengths: 'Strengths',
    improvements: 'Areas to Improve',
    turnFeedback: 'Turn-by-Turn Feedback',
    suggestions: 'Practice Suggestions',
    practiceAgain: 'Practice Again',
  },

  // ── Pronunciation ──
  pronunciation: {
    title: 'Pronunciation Drills',
    description: 'Master English phonemes and sounds for OET',
    noDrills: 'No pronunciation drills available.',
    practices: 'practices',
    mastered: 'Mastered',
  },

  // ── Vocabulary ──
  vocabulary: {
    title: 'Vocabulary Builder',
    dailyWords: 'Daily Words',
    flashcards: 'Flashcards',
    quiz: 'Quiz',
    browse: 'Browse',
  },

  // ── Writing Coach ──
  writingCoach: {
    title: 'Writing Coach',
    enableCoach: 'Enable Coach',
    disableCoach: 'Disable Coach',
    noSuggestions: 'No suggestions yet. Keep writing!',
    accept: 'Accept',
    dismiss: 'Dismiss',
    grammar: 'Grammar',
    vocabulary: 'Vocabulary',
    structure: 'Structure',
    conciseness: 'Conciseness',
    tone: 'Tone',
    format: 'Format',
  },

  // ── Gamification ──
  gamification: {
    achievementUnlocked: 'Achievement Unlocked',
    xpEarned: 'XP earned',
    levelUp: 'Level Up!',
    dayStreak: 'Day streak',
  },

  // ── Settings ──
  settings: {
    title: 'Settings',
    profile: 'Profile',
    goals: 'Goals',
    notifications: 'Notifications',
    privacy: 'Privacy',
    accessibility: 'Accessibility',
    audio: 'Audio',
    study: 'Study Preferences',
    language: 'Language',
    theme: 'Theme',
    darkMode: 'Dark Mode',
    lightMode: 'Light Mode',
    systemTheme: 'System',
  },

  // ── Accessibility ──
  a11y: {
    skipToMain: 'Skip to main content',
    closeDialog: 'Close dialog',
    openMenu: 'Open menu',
    closeMenu: 'Close menu',
    previousSlide: 'Previous',
    nextSlide: 'Next',
    expandSection: 'Expand section',
    collapseSection: 'Collapse section',
    loading: 'Loading content',
    required: 'Required',
    optional: 'Optional',
  },

  // ── Marketplace ──
  marketplace: {
    title: 'Content Marketplace',
    description: 'Browse community-contributed OET practice content or submit your own.',
    browseContent: 'Browse Content',
    submitContent: 'Submit Content',
    mySubmissions: 'My Submissions',
    approved: 'Approved',
    pending: 'Pending Review',
    rejected: 'Rejected',
    noContent: 'No marketplace content available yet.',
  },

  // ── Offline ──
  offline: {
    title: 'Offline Mode',
    availableOffline: 'Available Offline',
    downloadForOffline: 'Download for Offline',
    syncRequired: 'Sync Required',
    lastSynced: 'Last synced',
    syncNow: 'Sync Now',
    pendingUploads: 'Pending uploads',
    offlineBanner: 'You are currently offline. Some features may be limited.',
  },
};

// Structural type: each section maps string keys to string values
export type LocaleSection = Record<string, string>;
export type LocaleStrings = {
  [K in keyof typeof en]: { [P in keyof typeof en[K]]: string };
};
export type LocaleKey = keyof LocaleStrings;
export default en;
