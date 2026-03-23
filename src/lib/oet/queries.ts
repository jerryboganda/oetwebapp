"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { oetMockApi } from "@/lib/oet/mock-api";
import type { LearnerSettingsSection } from "@/lib/oet/stores/learner-settings-store";
import type { OetSubtest } from "@/types/oet";

export function useLearnerDashboardQuery() {
  return useQuery({
    queryKey: ["learner-dashboard"],
    queryFn: async () => {
      const [summary, readiness, studyPlan, reviews, evaluations] =
        await Promise.all([
          oetMockApi.getDashboardSummary(),
          oetMockApi.getReadiness(),
          oetMockApi.getStudyPlan(),
          oetMockApi.getReviewRequests(),
          oetMockApi.getEvaluations(),
        ]);

      return {
        evaluations,
        readiness,
        reviews,
        studyPlan,
        summary,
      };
    },
  });
}

export function useLearnerGoalsQuery() {
  return useQuery({
    queryKey: ["learner-goals"],
    queryFn: async () => {
      const [goal, professions] = await Promise.all([
        oetMockApi.getLearnerGoal(),
        oetMockApi.getProfessions(),
      ]);

      return { goal, professions };
    },
  });
}

export function useLearnerSettingsQuery() {
  return useQuery({
    queryKey: ["learner-settings"],
    queryFn: () => oetMockApi.getLearnerSettings(),
  });
}

export function useSaveLearnerSettingsSectionMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      payload,
      section,
    }: {
      payload: Record<string, unknown>;
      section: LearnerSettingsSection;
    }) => oetMockApi.saveLearnerSettingsSection(section, payload),
    onSuccess: (data) => {
      queryClient.setQueryData(["learner-settings"], data);
    },
  });
}

export function useToggleLearnerTwoFactorMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (enabled: boolean) =>
      oetMockApi.toggleLearnerTwoFactor(enabled),
    onSuccess: (data) => {
      queryClient.setQueryData(["learner-settings"], data);
    },
  });
}

export function useUpdateLearnerPasswordMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (newPassword: string) =>
      oetMockApi.updateLearnerPassword(newPassword),
    onSuccess: (data) => {
      queryClient.setQueryData(["learner-settings"], data);
    },
  });
}

export function useRemoveLearnerTrustedSessionMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (sessionId: string) =>
      oetMockApi.removeLearnerTrustedSession(sessionId),
    onSuccess: (data) => {
      queryClient.setQueryData(["learner-settings"], data);
    },
  });
}

export function useSignOutOtherLearnerSessionsMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => oetMockApi.signOutOtherLearnerSessions(),
    onSuccess: (data) => {
      queryClient.setQueryData(["learner-settings"], data);
    },
  });
}

export function useResetLearnerSettingsMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => oetMockApi.resetLearnerSettings(),
    onSuccess: (data) => {
      queryClient.setQueryData(["learner-settings"], data);
    },
  });
}

export function useEnrollmentOptionsQuery() {
  return useQuery({
    queryKey: ["enrollment-options"],
    queryFn: async () => {
      const [examTypes, professions, sessions] = await Promise.all([
        oetMockApi.getExamTypes(),
        oetMockApi.getProfessions(),
        oetMockApi.getSessions(),
      ]);

      return { examTypes, professions, sessions };
    },
  });
}

export function useStudyPlanQuery() {
  return useQuery({
    queryKey: ["study-plan"],
    queryFn: () => oetMockApi.getStudyPlan(),
  });
}

export function useReadinessQuery() {
  return useQuery({
    queryKey: ["readiness"],
    queryFn: () => oetMockApi.getReadiness(),
  });
}

export function useProgressQuery() {
  return useQuery({
    queryKey: ["progress-datasets"],
    queryFn: () => oetMockApi.getProgressDatasets(),
  });
}

export function useContentLibraryQuery(subtest?: OetSubtest) {
  return useQuery({
    queryKey: ["content-library", subtest],
    queryFn: async () => {
      const items = await oetMockApi.getContentItems();
      return subtest ? items.filter((item) => item.subtest === subtest) : items;
    },
  });
}

export function useEvaluationDetailQuery(evaluationId: string) {
  return useQuery({
    queryKey: ["evaluation", evaluationId],
    queryFn: () => oetMockApi.getEvaluationDetail(evaluationId),
  });
}

export function useAttemptDetailQuery(attemptId: string) {
  return useQuery({
    queryKey: ["attempt", attemptId],
    queryFn: () => oetMockApi.getAttemptDetail(attemptId),
  });
}

export function useWritingDetailQuery(contentId: string) {
  return useQuery({
    queryKey: ["writing-detail", contentId],
    queryFn: async () => {
      const [content, caseNotes, criteria, modelAnswer] = await Promise.all([
        oetMockApi.getContentItems(),
        oetMockApi.getWritingCaseNotes(),
        oetMockApi.getWritingCriteria(),
        oetMockApi.getWritingModelAnswer(),
      ]);

      return {
        caseNotes,
        content: content.find((item) => item.id === contentId),
        criteria,
        modelAnswer,
      };
    },
  });
}

export function useSpeakingReviewQuery(attemptId: string) {
  return useQuery({
    queryKey: ["speaking-review", attemptId],
    queryFn: async () => {
      const [attempt, transcript, betterPhrasing, evaluations] =
        await Promise.all([
          oetMockApi.getAttemptDetail(attemptId),
          oetMockApi.getSpeakingTranscript(),
          oetMockApi.getSpeakingBetterPhrasing(),
          oetMockApi.getEvaluations(),
        ]);

      return {
        attempt,
        betterPhrasing,
        evaluation: evaluations.find((item) => item.attemptId === attemptId),
        transcript,
      };
    },
  });
}

export function useSubscriptionQuery() {
  return useQuery({
    queryKey: ["subscription"],
    queryFn: async () => {
      const [subscription, wallet] = await Promise.all([
        oetMockApi.getSubscription(),
        oetMockApi.getWalletCredits(),
      ]);

      return { subscription, wallet };
    },
  });
}

export function useExpertQueueQuery() {
  return useQuery({
    queryKey: ["expert-queue"],
    queryFn: () => oetMockApi.getExpertQueue(),
  });
}

export function useExpertWorkspaceQuery(reviewRequestId: string) {
  return useQuery({
    queryKey: ["expert-workspace", reviewRequestId],
    queryFn: async () => {
      const [review, transcript, evaluations, contentItems, attempts] =
        await Promise.all([
          oetMockApi.getReviewRequestDetail(reviewRequestId),
          oetMockApi.getSpeakingTranscript(),
          oetMockApi.getEvaluations(),
          oetMockApi.getContentItems(),
          oetMockApi.getAttempts(),
        ]);

      const attempt = attempts.find((item) => item.id === review?.attemptId);
      const content = contentItems.find(
        (item) => item.id === attempt?.contentItemId
      );
      const evaluation = evaluations.find(
        (item) => item.attemptId === review?.attemptId
      );

      return {
        attempt,
        content,
        evaluation,
        review,
        transcript,
      };
    },
  });
}

export function useAdminOpsQuery() {
  return useQuery({
    queryKey: ["admin-ops"],
    queryFn: async () => {
      const [
        content,
        aiConfig,
        qualityAnalytics,
        users,
        billing,
        flags,
        audit,
      ] = await Promise.all([
        oetMockApi.getAdminContent(),
        oetMockApi.getAiConfig(),
        oetMockApi.getQualityAnalytics(),
        oetMockApi.getAdminUsers(),
        oetMockApi.getAdminBilling(),
        oetMockApi.getFeatureFlags(),
        oetMockApi.getAuditLogs(),
      ]);

      return {
        aiConfig,
        audit,
        billing,
        content,
        flags,
        qualityAnalytics,
        users,
      };
    },
  });
}
