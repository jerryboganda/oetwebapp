import {
  adminBillingRows,
  adminContentLibrary,
  aiConfigRows,
  enrollmentSessions,
  enrollmentSessionRows,
  assignedLearners,
  attempts,
  auditLogs,
  calibrationCases,
  contentItems,
  dashboardSummary,
  examTypes,
  examTypeRows,
  evaluations,
  expertMetrics,
  expertQueueRows,
  featureFlags,
  getAttemptById,
  getContentItemById,
  getContentRevisionsById,
  getEvaluationById,
  getLearnerById,
  getMockReportById,
  getReviewRequestById,
  learnerGoal,
  mockReports,
  professions,
  progressDatasets,
  qualityAnalyticsRows,
  readinessSnapshot,
  reviewOpsCards,
  reviewRequests,
  speakingBetterPhrasing,
  speakingTranscriptSegments,
  studyPlan,
  subscription,
  taxonomyRows,
  walletCredits,
  writingCaseNotes,
  writingCriteria,
  writingModelAnswer,
  speakingCriteria,
  adminUsers,
} from "@/Data/OET/mock";
import {
  useLearnerSettingsStore,
  type LearnerSettingsSection,
} from "@/lib/oet/stores/learner-settings-store";

async function withLatency<T>(payload: T, delay = 120): Promise<T> {
  await new Promise((resolve) => setTimeout(resolve, delay));
  return payload;
}

export const oetMockApi = {
  getAdminBilling: () => withLatency(adminBillingRows),
  getAdminContent: () => withLatency(adminContentLibrary),
  getAdminContentDetail: (id: string) =>
    withLatency({
      content: getContentItemById(id),
      revisions: getContentRevisionsById(id),
    }),
  getAdminUsers: () => withLatency(adminUsers),
  getAiConfig: () => withLatency(aiConfigRows),
  getAssignedLearners: () => withLatency(assignedLearners),
  getAttempts: () => withLatency(attempts),
  getAttemptDetail: (id: string) => withLatency(getAttemptById(id)),
  getAuditLogs: () => withLatency(auditLogs),
  getCalibrationCases: () => withLatency(calibrationCases),
  getContentItems: () => withLatency(contentItems),
  getDashboardSummary: () => withLatency(dashboardSummary),
  getEvaluationDetail: (id: string) => withLatency(getEvaluationById(id)),
  getEvaluations: () => withLatency(evaluations),
  getExamTypes: () => withLatency(examTypes),
  getExamTypeRows: () => withLatency(examTypeRows),
  getExpertMetrics: () => withLatency(expertMetrics),
  getExpertQueue: () => withLatency(expertQueueRows),
  getFeatureFlags: () => withLatency(featureFlags),
  getLearner: (id: string) => withLatency(getLearnerById(id)),
  getLearnerGoal: () => withLatency(learnerGoal),
  getLearnerSettings: () =>
    withLatency({
      activity: useLearnerSettingsStore.getState().activity,
      activitySummary: useLearnerSettingsStore.getState().activitySummary,
      connections: useLearnerSettingsStore.getState().connections,
      notifications: useLearnerSettingsStore.getState().notifications,
      privacy: useLearnerSettingsStore.getState().privacy,
      profile: useLearnerSettingsStore.getState().profile,
      security: useLearnerSettingsStore.getState().security,
      subscription: useLearnerSettingsStore.getState().subscription,
    }),
  getMockReport: (id: string) => withLatency(getMockReportById(id)),
  getMockReports: () => withLatency(mockReports),
  getProfessions: () => withLatency(professions),
  getProgressDatasets: () => withLatency(progressDatasets),
  getQualityAnalytics: () => withLatency(qualityAnalyticsRows),
  getReadiness: () => withLatency(readinessSnapshot),
  getReviewOps: () => withLatency(reviewOpsCards),
  getReviewRequestDetail: (id: string) => withLatency(getReviewRequestById(id)),
  getReviewRequests: () => withLatency(reviewRequests),
  getSessions: () => withLatency(enrollmentSessions),
  getSessionRows: () => withLatency(enrollmentSessionRows),
  getSpeakingBetterPhrasing: () => withLatency(speakingBetterPhrasing),
  getSpeakingCriteria: () => withLatency(speakingCriteria),
  getSpeakingTranscript: () => withLatency(speakingTranscriptSegments),
  getStudyPlan: () => withLatency(studyPlan),
  getSubscription: () => withLatency(subscription),
  getTaxonomy: () => withLatency(taxonomyRows),
  getWalletCredits: () => withLatency(walletCredits),
  getWritingCaseNotes: () => withLatency(writingCaseNotes),
  getWritingCriteria: () => withLatency(writingCriteria),
  getWritingModelAnswer: () => withLatency(writingModelAnswer),
  removeLearnerTrustedSession: async (sessionId: string) => {
    useLearnerSettingsStore.getState().removeTrustedSession(sessionId);
    return oetMockApi.getLearnerSettings();
  },
  resetLearnerSettings: async () => {
    useLearnerSettingsStore.getState().reset();
    return oetMockApi.getLearnerSettings();
  },
  saveLearnerSettingsSection: async (
    section: LearnerSettingsSection,
    payload: Record<string, unknown>
  ) => {
    useLearnerSettingsStore.getState().saveSection(section, payload as never);
    return oetMockApi.getLearnerSettings();
  },
  signOutOtherLearnerSessions: async () => {
    useLearnerSettingsStore.getState().signOutOtherSessions();
    return oetMockApi.getLearnerSettings();
  },
  toggleLearnerTwoFactor: async (enabled: boolean) => {
    useLearnerSettingsStore.getState().toggleTwoFactor(enabled);
    return oetMockApi.getLearnerSettings();
  },
  updateLearnerPassword: async (_newPassword: string) => {
    useLearnerSettingsStore.getState().changePassword(_newPassword);
    return oetMockApi.getLearnerSettings();
  },
};
