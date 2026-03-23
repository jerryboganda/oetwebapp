import type { LearnerSettingsTabId } from "@/types/oet";

export const learnerSettingsTabs: LearnerSettingsTabId[] = [
  "profile",
  "activity",
  "security",
  "privacy",
  "notifications",
  "subscription",
  "connections",
  "delete",
];

export function isLearnerSettingsTabId(
  value: string | null | undefined
): value is LearnerSettingsTabId {
  return learnerSettingsTabs.includes(value as LearnerSettingsTabId);
}

export function normalizeLearnerSettingsTab(
  value: string | null | undefined
): LearnerSettingsTabId {
  return isLearnerSettingsTabId(value) ? value : "profile";
}
