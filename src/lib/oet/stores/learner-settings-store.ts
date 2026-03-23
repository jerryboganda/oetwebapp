"use client";

import { create } from "zustand";
import { persist } from "zustand/middleware";
import { learnerSettingsWorkspace } from "@/Data/OET/mock";
import type {
  LearnerConnectionPreferences,
  LearnerNotificationPreferences,
  LearnerPrivacyPreferences,
  LearnerSecurityState,
  LearnerSettingsProfile,
  LearnerSettingsWorkspaceData,
} from "@/types/oet";

type MutableLearnerSettingsSections = Pick<
  LearnerSettingsWorkspaceData,
  "connections" | "notifications" | "privacy" | "profile" | "security"
>;

export type LearnerSettingsSection = keyof MutableLearnerSettingsSections;

function cloneInitialState(): LearnerSettingsWorkspaceData {
  return {
    activity: learnerSettingsWorkspace.activity.map((item) => ({ ...item })),
    activitySummary: { ...learnerSettingsWorkspace.activitySummary },
    connections: { ...learnerSettingsWorkspace.connections },
    notifications: { ...learnerSettingsWorkspace.notifications },
    privacy: { ...learnerSettingsWorkspace.privacy },
    profile: { ...learnerSettingsWorkspace.profile },
    security: {
      ...learnerSettingsWorkspace.security,
      trustedSessions: learnerSettingsWorkspace.security.trustedSessions.map(
        (session) => ({ ...session })
      ),
    },
    subscription: { ...learnerSettingsWorkspace.subscription },
  };
}

function stampNow() {
  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit",
    hour: "2-digit",
    hour12: true,
    minute: "2-digit",
    month: "short",
    year: "numeric",
  }).format(new Date());
}

interface LearnerSettingsStoreState extends LearnerSettingsWorkspaceData {
  changePassword: (newPassword: string) => void;
  removeTrustedSession: (sessionId: string) => void;
  reset: () => void;
  saveSection: <K extends LearnerSettingsSection>(
    section: K,
    payload: Partial<MutableLearnerSettingsSections[K]>
  ) => void;
  signOutOtherSessions: () => void;
  toggleTwoFactor: (enabled: boolean) => void;
}

const initialState = cloneInitialState();

export const useLearnerSettingsStore = create<LearnerSettingsStoreState>()(
  persist(
    (set) => ({
      ...initialState,
      changePassword: () =>
        set((state) => ({
          security: {
            ...state.security,
            lastPasswordChanged: stampNow(),
          },
        })),
      removeTrustedSession: (sessionId) =>
        set((state) => ({
          security: {
            ...state.security,
            trustedSessions: state.security.trustedSessions.filter(
              (session) =>
                session.id !== sessionId || session.status === "current"
            ),
          },
        })),
      reset: () => set(cloneInitialState()),
      saveSection: (section, payload) =>
        set((state) => ({
          [section]: {
            ...state[section],
            ...payload,
          },
        })),
      signOutOtherSessions: () =>
        set((state) => ({
          security: {
            ...state.security,
            trustedSessions: state.security.trustedSessions.filter(
              (session) => session.status === "current"
            ),
          },
        })),
      toggleTwoFactor: (enabled) =>
        set((state) => ({
          security: {
            ...state.security,
            twoFactorEnabled: enabled,
          },
        })),
    }),
    {
      name: "oet-learner-settings",
      partialize: (state) => ({
        activity: state.activity,
        activitySummary: state.activitySummary,
        connections: state.connections,
        notifications: state.notifications,
        privacy: state.privacy,
        profile: state.profile,
        security: state.security,
        subscription: state.subscription,
      }),
    }
  )
);

export type LearnerSettingsProfileInput = Partial<LearnerSettingsProfile>;
export type LearnerNotificationPreferencesInput =
  Partial<LearnerNotificationPreferences>;
export type LearnerPrivacyPreferencesInput = Partial<LearnerPrivacyPreferences>;
export type LearnerConnectionPreferencesInput =
  Partial<LearnerConnectionPreferences>;
export type LearnerSecurityStateInput = Partial<LearnerSecurityState>;
