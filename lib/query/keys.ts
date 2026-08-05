/**
 * Query keys are kept in a dependency-free module so root providers can share
 * cache identities without importing the full API client into the first-load
 * browser graph.
 */
export const queryKeys = {
  profile: {
    _def: ['profile'] as const,
    self: (userId: string) => ['profile', userId, 'self'] as const,
    onboarding: (userId: string) => ['profile', userId, 'onboarding'] as const,
  },
  dashboard: {
    _def: ['dashboard'] as const,
    home: (userId: string) => ['dashboard', userId, 'home'] as const,
    engagement: (userId: string) => ['dashboard', userId, 'engagement'] as const,
    scoringPolicy: (userId: string) => ['dashboard', userId, 'scoring-policy'] as const,
    entitlement: (userId: string) => ['dashboard', userId, 'entitlement'] as const,
    subscription: (userId: string) => ['dashboard', userId, 'subscription'] as const,
    aiPackageCredits: (userId: string) => ['dashboard', userId, 'ai-package-credits'] as const,
  },
  readiness: {
    _def: ['readiness'] as const,
    self: (userId: string) => ['readiness', userId, 'self'] as const,
  },
  studyPlan: {
    _def: ['study-plan'] as const,
    list: (userId: string) => ['study-plan', userId, 'list'] as const,
  },
  onboardingTours: {
    _def: ['onboarding-tours'] as const,
    state: ['onboarding-tours', 'state'] as const,
  },
  settings: {
    _def: ['settings'] as const,
    home: (userId: string) => ['settings', userId, 'home'] as const,
    freeze: (userId: string) => ['settings', userId, 'freeze'] as const,
    section: (userId: string, section: string) => ['settings', userId, 'section', section] as const,
  },
  progress: {
    _def: ['progress'] as const,
    trend: (userId: string) => ['progress', userId, 'trend'] as const,
    completion: (userId: string) => ['progress', userId, 'completion'] as const,
    submissionVolume: (userId: string) => ['progress', userId, 'submission-volume'] as const,
    evidence: (userId: string) => ['progress', userId, 'evidence'] as const,
  },
  leaderboard: {
    _def: ['leaderboard'] as const,
    list: (userId: string, examType: string, period: string) =>
      ['leaderboard', userId, 'list', examType, period] as const,
    position: (userId: string, examType: string, period: string) =>
      ['leaderboard', userId, 'position', examType, period] as const,
  },
  vocabulary: {
    _def: ['vocabulary'] as const,
    categories: (userId: string, examType: string, profession?: string) =>
      ['vocabulary', userId, 'categories', examType, profession ?? null] as const,
    recallSets: (userId: string, examType: string, profession?: string) =>
      ['vocabulary', userId, 'recall-sets', examType, profession ?? null] as const,
  },
  listening: {
    _def: ['listening'] as const,
    lessons: ['listening', 'lessons'] as const,
    strategies: (category: string) => ['listening', 'strategies', category] as const,
  },
} as const;
