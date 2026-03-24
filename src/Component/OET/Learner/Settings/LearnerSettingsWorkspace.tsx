"use client";

import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { Reports } from "iconoir-react";
import {
  Alarm,
  Calendar,
  BellSimple,
  CheckCircle,
  ClockCounterClockwise,
  CreditCard,
  DeviceMobile,
  GlobeHemisphereWest,
  Graph,
  LockOpen,
  Monitor,
  Notification,
  ShieldStar,
  ShieldCheck,
  Trash,
  UserCircleGear,
} from "phosphor-react";
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  Col,
  Form,
  FormGroup,
  Input,
  InputGroup,
  InputGroupText,
  Label,
  Row,
} from "reactstrap";
import {
  buildSupportMailto,
  buildSupportWhatsAppLink,
} from "@/lib/auth/support";
import { IconCircleXFilled } from "@/lib/icons/tabler";
import { normalizeLearnerSettingsTab } from "@/lib/oet/settings";
import {
  useLearnerGoalsQuery,
  useLearnerSettingsQuery,
  useRemoveLearnerTrustedSessionMutation,
  useResetLearnerSettingsMutation,
  useSaveLearnerSettingsSectionMutation,
  useSignOutOtherLearnerSessionsMutation,
  useSubscriptionQuery,
  useToggleLearnerTwoFactorMutation,
  useUpdateLearnerPasswordMutation,
} from "@/lib/oet/queries";
import { useEnrollmentTaxonomyStore } from "@/lib/oet/stores/enrollment-taxonomy-store";
import type {
  LearnerConnectionPreferences,
  LearnerNotificationPreferences,
  LearnerPrivacyPreferences,
  LearnerSettingsActivityItem,
  LearnerSettingsProfile,
  LearnerSettingsTabId,
  LearnerSettingsWorkspaceData,
} from "@/types/oet";
import styles from "./LearnerSettingsWorkspace.module.scss";

const LANGUAGE_OPTIONS = [
  "English",
  "Arabic",
  "Hindi",
  "Urdu",
  "Tamil",
  "French",
];

const TIMEZONE_OPTIONS = [
  "Asia/Karachi",
  "Australia/Sydney",
  "Europe/London",
  "Asia/Dubai",
  "America/Toronto",
];

const SETTINGS_TABS: Array<{
  description: string;
  icon: React.ElementType;
  id: LearnerSettingsTabId;
  label: string;
}> = [
  {
    description: "Identity, contact, profession, and exam pathway.",
    icon: UserCircleGear,
    id: "profile",
    label: "Profile",
  },
  {
    description: "Study signal feed, recent logins, and weekly activity.",
    icon: Alarm,
    id: "activity",
    label: "Activity",
  },
  {
    description: "Password, two-factor protection, and trusted sessions.",
    icon: ShieldCheck,
    id: "security",
    label: "Security",
  },
  {
    description: "Visibility, consent, retention, and learner privacy rules.",
    icon: LockOpen,
    id: "privacy",
    label: "Privacy",
  },
  {
    description: "Review alerts, reminders, WhatsApp, and digests.",
    icon: Notification,
    id: "notifications",
    label: "Notifications",
  },
  {
    description: "Plan summary, credits, renewal, and payment snapshot.",
    icon: BellSimple,
    id: "subscription",
    label: "Subscription",
  },
  {
    description: "Calendar sync, device readiness, captions, and bandwidth.",
    icon: Graph,
    id: "connections",
    label: "Connections",
  },
  {
    description: "Exports, support handoff, and account closure controls.",
    icon: Trash,
    id: "delete",
    label: "Delete",
  },
];

function formatDateTime(value: string) {
  const normalized = value.includes("T") ? value : value.replace(" ", "T");
  const parsed = new Date(normalized);

  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit",
    hour: "2-digit",
    hour12: true,
    minute: "2-digit",
    month: "short",
    year: "numeric",
  }).format(parsed);
}

function formatDateOnly(value: string) {
  const parsed = new Date(value);

  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit",
    month: "short",
    year: "numeric",
  }).format(parsed);
}

function getToneStyle(tone: LearnerSettingsActivityItem["tone"]) {
  const palette = {
    danger: { background: "rgba(255, 88, 88, 0.14)", color: "#a91d3a" },
    info: { background: "rgba(58, 123, 213, 0.12)", color: "#1258a8" },
    primary: { background: "rgba(90, 72, 255, 0.12)", color: "#4b3ef6" },
    success: { background: "rgba(31, 186, 110, 0.14)", color: "#128356" },
    warning: { background: "rgba(255, 182, 55, 0.16)", color: "#b86a00" },
  };

  return palette[tone];
}

type HeroStatCard = {
  helper: string;
  icon: React.ElementType;
  label: string;
  meta: string;
  tone: "large" | "small";
  value: string;
};

function LearnerSettingsWorkspace() {
  const pathname = usePathname();
  const router = useRouter();
  const searchParams = useSearchParams();
  const activeTab = normalizeLearnerSettingsTab(searchParams.get("tab"));

  const professions = useEnrollmentTaxonomyStore((state) => state.professions);
  const targetCountries = useEnrollmentTaxonomyStore(
    (state) => state.targetCountries
  );
  const { data, isLoading } = useLearnerSettingsQuery();
  const { data: goalData } = useLearnerGoalsQuery();
  const { data: subscriptionData } = useSubscriptionQuery();

  const saveSection = useSaveLearnerSettingsSectionMutation();
  const toggleTwoFactor = useToggleLearnerTwoFactorMutation();
  const updatePassword = useUpdateLearnerPasswordMutation();
  const removeSession = useRemoveLearnerTrustedSessionMutation();
  const signOutOtherSessions = useSignOutOtherLearnerSessionsMutation();
  const resetSettings = useResetLearnerSettingsMutation();

  const [notice, setNotice] = useState<{
    message: string;
    tone: "danger" | "success";
  } | null>(null);

  useEffect(() => {
    if (!notice) {
      return;
    }

    const timer = window.setTimeout(() => setNotice(null), 3200);
    return () => window.clearTimeout(timer);
  }, [notice]);

  const activeMeta = useMemo(
    () =>
      SETTINGS_TABS.find((item) => item.id === activeTab) ?? SETTINGS_TABS[0]!,
    [activeTab]
  );

  const professionLabel = useMemo(
    () =>
      professions.find((item) => item.id === data?.profile.professionId)
        ?.label ?? "Learner pathway",
    [data?.profile.professionId, professions]
  );

  const heroStats = useMemo<HeroStatCard[]>(() => {
    if (!data) {
      return [];
    }

    const enabledNotificationChannels = [
      data.notifications.emailReviewAlerts,
      data.notifications.inAppProgressDigest,
      data.notifications.browserPracticeReminders,
      data.notifications.whatsappReminders,
    ].filter(Boolean).length;

    const common: Record<LearnerSettingsTabId, HeroStatCard[]> = {
      activity: [
        {
          helper: "Visible this week",
          icon: ClockCounterClockwise,
          label: "Study minutes",
          meta: `${data.activitySummary.reviewMinutesThisWeek} review minutes`,
          tone: "small",
          value: `${data.activitySummary.studyMinutesThisWeek}m`,
        },
        {
          helper: "Live study pulse",
          icon: Reports,
          label: "Recent events",
          meta: `${data.activity.length} activity items tracked`,
          tone: "large",
          value: formatDateTime(data.activitySummary.lastLoginAt),
        },
        {
          helper: "Across trusted sessions",
          icon: Monitor,
          label: "Active devices",
          meta:
            data.activitySummary.activeDevices === 1
              ? "single device"
              : "workspace verified",
          tone: "small",
          value: String(data.activitySummary.activeDevices),
        },
      ],
      connections: [
        {
          helper: "Calendar sync",
          icon: Calendar,
          label: "Connected schedule",
          meta: data.connections.calendarSync,
          tone: "small",
          value: data.connections.calendarSync === "none" ? "Off" : "On",
        },
        {
          helper: "Device readiness",
          icon: DeviceMobile,
          label: "Playback profile",
          meta: `${data.connections.playbackSpeed} playback`,
          tone: "large",
          value: data.connections.lowBandwidthMode
            ? "Low-bandwidth"
            : "Full media",
        },
        {
          helper: "Assistive settings",
          icon: GlobeHemisphereWest,
          label: "Captions",
          meta: data.connections.browserNotifications
            ? "browser prompts enabled"
            : "browser prompts muted",
          tone: "small",
          value: data.connections.captionsEnabled ? "Enabled" : "Off",
        },
      ],
      delete: [
        {
          helper: "Recovery first",
          icon: CheckCircle,
          label: "Export status",
          meta: "Profile and study settings are ready to export",
          tone: "small",
          value: "Ready",
        },
        {
          helper: "Support handoff",
          icon: ShieldStar,
          label: "Closure guardrails",
          meta: "Destructive actions stay behind confirmation",
          tone: "large",
          value: "Protected",
        },
        {
          helper: "Need human help",
          icon: BellSimple,
          label: "Support route",
          meta: "Support and WhatsApp stay one click away",
          tone: "small",
          value: "Live",
        },
      ],
      notifications: [
        {
          helper: "Core channels",
          icon: BellSimple,
          label: "Channels enabled",
          meta: data.notifications.whatsappReminders
            ? "WhatsApp included"
            : "WhatsApp muted",
          tone: "small",
          value: `${enabledNotificationChannels}/4`,
        },
        {
          helper: "Reminder control",
          icon: Notification,
          label: "Cadence",
          meta: data.notifications.reviewNudges
            ? "review alerts active"
            : "review alerts paused",
          tone: "large",
          value: data.notifications.reminderCadence,
        },
        {
          helper: "Quiet hours",
          icon: Alarm,
          label: "Browser alerts",
          meta: data.notifications.emailReviewAlerts
            ? "email reminders on"
            : "email reminders off",
          tone: "small",
          value: data.notifications.browserPracticeReminders ? "On" : "Off",
        },
      ],
      privacy: [
        {
          helper: "Profile visibility",
          icon: UserCircleGear,
          label: "Audience",
          meta: data.privacy.expertSharingConsent
            ? "expert sharing enabled"
            : "expert sharing limited",
          tone: "small",
          value: data.privacy.profileVisibility,
        },
        {
          helper: "Retention policy",
          icon: LockOpen,
          label: "Transcript storage",
          meta: data.privacy.analyticsOptIn
            ? "analytics consent granted"
            : "analytics consent disabled",
          tone: "large",
          value: data.privacy.transcriptRetention,
        },
        {
          helper: "Consent status",
          icon: ShieldCheck,
          label: "Marketing",
          meta: "Learner-controlled preference",
          tone: "small",
          value: data.privacy.marketingEmailsEnabled ? "Allowed" : "Blocked",
        },
      ],
      profile: [
        {
          helper: "Identity quality",
          icon: UserCircleGear,
          label: "Profile completion",
          meta: "Profession, region, and timezone are active",
          tone: "small",
          value: "92%",
        },
        {
          helper: "Learner path",
          icon: GlobeHemisphereWest,
          label: "Target country",
          meta: professionLabel,
          tone: "large",
          value: data.profile.targetCountry,
        },
        {
          helper: "Timezone",
          icon: ClockCounterClockwise,
          label: "Study timing",
          meta: data.profile.preferredLanguage,
          tone: "small",
          value: data.profile.timezone,
        },
      ],
      security: [
        {
          helper: "Trusted sessions",
          icon: Monitor,
          label: "Active devices",
          meta: data.security.recoveryEmail,
          tone: "small",
          value: String(data.security.trustedSessions.length),
        },
        {
          helper: data.security.twoFactorEnabled
            ? "2FA enabled"
            : "2FA disabled",
          icon: ShieldStar,
          label: "Protection status",
          meta: `Updated ${formatDateTime(data.security.lastPasswordChanged)}`,
          tone: "large",
          value: data.security.twoFactorEnabled ? "Protected" : "Basic",
        },
        {
          helper: "Password timeline",
          icon: LockOpen,
          label: "Last rotation",
          meta: "Recovery remains predictable",
          tone: "small",
          value: formatDateOnly(data.security.lastPasswordChanged),
        },
      ],
      subscription: [
        {
          helper: "Billing snapshot",
          icon: CreditCard,
          label: "Current plan",
          meta: formatDateOnly(data.subscription.nextRenewal),
          tone: "small",
          value: data.subscription.currentPlan,
        },
        {
          helper: "Review inventory",
          icon: Reports,
          label: "Credits available",
          meta: "Use billing center for invoices and upgrades",
          tone: "large",
          value: String(data.subscription.reviewCredits),
        },
        {
          helper: "Renewal",
          icon: Calendar,
          label: "Next cycle",
          meta: data.subscription.paymentMethodLabel,
          tone: "small",
          value: formatDateOnly(data.subscription.nextRenewal),
        },
      ],
    };

    return common[activeTab];
  }, [activeTab, data, professionLabel]);

  const compactSummary = useMemo(
    () => [
      {
        label: "Target exam",
        value: goalData?.goal.examDate
          ? formatDateOnly(goalData.goal.examDate)
          : "Not set",
      },
      {
        label: "Weekly plan",
        value: goalData?.goal.weeklyStudyHours
          ? `${goalData.goal.weeklyStudyHours}h / week`
          : "Pending",
      },
      {
        label: "Renewal",
        value: data?.subscription.nextRenewal
          ? formatDateOnly(data.subscription.nextRenewal)
          : (subscriptionData?.subscription.renewalDate ?? "Pending"),
      },
      {
        label: "Credits",
        value: data
          ? `${data.subscription.reviewCredits} available`
          : subscriptionData
            ? `${subscriptionData.wallet.available} available`
            : "Pending",
      },
    ],
    [data, goalData, subscriptionData]
  );

  const changeTab = (tabId: LearnerSettingsTabId) => {
    const params = new URLSearchParams(searchParams.toString());
    params.set("tab", tabId);
    router.replace(`${pathname}?${params.toString()}`, { scroll: false });
  };

  const handleSectionSave = async (
    section:
      | "connections"
      | "notifications"
      | "privacy"
      | "profile"
      | "security",
    payload: Record<string, unknown>,
    message: string
  ) => {
    try {
      await saveSection.mutateAsync({ payload, section });
      setNotice({ message, tone: "success" });
    } catch {
      setNotice({
        message: "Something went wrong while saving this settings section.",
        tone: "danger",
      });
    }
  };

  const handlePasswordChange = async (newPassword: string) => {
    try {
      await updatePassword.mutateAsync(newPassword);
      setNotice({
        message:
          "Password details updated. Your security timeline has been refreshed.",
        tone: "success",
      });
    } catch {
      setNotice({
        message: "Password update failed. Please try again.",
        tone: "danger",
      });
    }
  };

  const handleToggleTwoFactor = async (enabled: boolean) => {
    try {
      await toggleTwoFactor.mutateAsync(enabled);
      setNotice({
        message: enabled
          ? "Two-step verification is now enabled."
          : "Two-step verification is now disabled.",
        tone: "success",
      });
    } catch {
      setNotice({
        message: "Unable to update two-step verification right now.",
        tone: "danger",
      });
    }
  };

  const handleRemoveSession = async (sessionId: string) => {
    try {
      await removeSession.mutateAsync(sessionId);
      setNotice({
        message: "Trusted session removed from this learner workspace.",
        tone: "success",
      });
    } catch {
      setNotice({
        message: "Unable to remove this session right now.",
        tone: "danger",
      });
    }
  };

  const handleSignOutOtherSessions = async () => {
    try {
      await signOutOtherSessions.mutateAsync();
      setNotice({
        message: "All other sessions were signed out successfully.",
        tone: "success",
      });
    } catch {
      setNotice({
        message: "Unable to sign out the other sessions right now.",
        tone: "danger",
      });
    }
  };

  const handleResetWorkspace = async () => {
    try {
      await resetSettings.mutateAsync();
      setNotice({
        message: "Learner settings reset to the seeded OET defaults.",
        tone: "success",
      });
    } catch {
      setNotice({
        message: "Unable to reset the settings workspace right now.",
        tone: "danger",
      });
    }
  };

  if (isLoading || !data) {
    return <Alert color="light">Loading learner settings workspace...</Alert>;
  }

  return (
    <div className={styles.workspace}>
      <aside className={styles.rail}>
        <Card className={styles.railCard}>
          <CardBody className={styles.profileCard}>
            <div className={styles.profileTop}>
              <span className={styles.avatarWrap}>
                <img
                  alt={data.profile.fullName}
                  className={styles.avatar}
                  src={data.profile.avatarUrl}
                />
              </span>
              <div>
                <p className={styles.eyebrow}>Learner settings hub</p>
                <h3 className={styles.profileName}>{data.profile.fullName}</h3>
                <p className={styles.profileMeta}>
                  {professionLabel} · {data.profile.targetCountry}
                </p>
                <p className={styles.profileMeta}>{data.profile.email}</p>
              </div>
            </div>
            <div className={styles.chipRow}>
              <span className={styles.chip}>
                {data.subscription.currentPlan}
              </span>
              {goalData?.goal.examDate ? (
                <span className={styles.chip}>
                  Exam {formatDateOnly(goalData.goal.examDate)}
                </span>
              ) : null}
            </div>
          </CardBody>
        </Card>

        <Card className={styles.railCard}>
          <CardBody className={styles.summaryCard}>
            <h4 className={styles.summaryTitle}>Workspace snapshot</h4>
            <p className={styles.summaryText}>
              Quick signals that keep the settings page useful without turning
              it into another dashboard.
            </p>
            <div className={styles.summaryGrid}>
              {compactSummary.map((item) => (
                <div className={styles.summaryTile} key={item.label}>
                  <span className={styles.summaryLabel}>{item.label}</span>
                  <span className={styles.summaryValue}>{item.value}</span>
                </div>
              ))}
            </div>
          </CardBody>
        </Card>

        <Card className={styles.railCard}>
          <CardBody className={styles.navCard}>
            <div className={styles.navList}>
              {SETTINGS_TABS.map((tab) => {
                const Icon = tab.icon;
                return (
                  <button
                    className={[
                      styles.navButton,
                      activeTab === tab.id ? styles.navButtonActive : "",
                    ]
                      .filter(Boolean)
                      .join(" ")}
                    key={tab.id}
                    onClick={() => changeTab(tab.id)}
                    type="button"
                  >
                    <span className={styles.navIcon}>
                      <Icon size={20} weight="bold" />
                    </span>
                    <span>
                      <span className={styles.navTitle}>{tab.label}</span>
                      <span className={styles.navDescription}>
                        {tab.description}
                      </span>
                    </span>
                  </button>
                );
              })}
            </div>
          </CardBody>
        </Card>
      </aside>

      <section className={styles.contentCard}>
        {notice ? (
          <Alert color={notice.tone === "success" ? "success" : "danger"}>
            {notice.message}
          </Alert>
        ) : null}
        <div className={styles.contentHero}>
          <div>
            <span className={styles.badgePill}>Learner settings</span>
            <h2 className={styles.heroTitle}>{activeMeta.label}</h2>
            <p className={styles.heroText}>{activeMeta.description}</p>
            <div className={styles.chipRow}>
              <span className={styles.chip}>
                {data.subscription.currentPlan}
              </span>
              <span className={styles.chip}>
                {data.notifications.reminderCadence} reminders
              </span>
              <span className={styles.chip}>
                {data.connections.lowBandwidthMode
                  ? "Low-bandwidth ready"
                  : "Full media mode"}
              </span>
            </div>
          </div>

          <div className={styles.heroStatsGrid}>
            {heroStats.map((item, index) => {
              const Icon = item.icon;

              return (
                <Card
                  className={[
                    item.tone === "large"
                      ? "bg-primary-300 product-sold-card"
                      : index === heroStats.length - 1
                        ? "product-store-card"
                        : "orders-provided-card",
                    styles.heroStatCard,
                    item.tone === "large"
                      ? styles.heroStatLarge
                      : styles.heroStatSmall,
                  ].join(" ")}
                  key={`${activeTab}-${item.label}`}
                >
                  <CardBody className={styles.heroStatBody}>
                    <div className={styles.heroStatHeader}>
                      <span className={styles.heroStatIcon}>
                        <Icon size={20} weight="fill" />
                      </span>
                      <div>
                        <p className={styles.heroStatLabel}>{item.label}</p>
                        <p className={styles.heroStatHelper}>{item.helper}</p>
                      </div>
                    </div>
                    <div className={styles.heroStatFooter}>
                      <h3 className={styles.heroStatValue}>{item.value}</h3>
                      <p className={styles.heroStatMeta}>{item.meta}</p>
                    </div>
                  </CardBody>
                </Card>
              );
            })}
          </div>
        </div>

        {activeTab === "profile" ? (
          <ProfilePanel
            countries={targetCountries.map((item) => item.label)}
            data={data.profile}
            onReset={handleResetWorkspace}
            onSave={(payload) =>
              handleSectionSave(
                "profile",
                payload,
                "Profile settings saved to the learner workspace."
              )
            }
            professions={professions.map((item) => ({
              id: item.id,
              label: item.label,
            }))}
            saving={saveSection.isPending || resetSettings.isPending}
          />
        ) : null}
        {activeTab === "activity" ? <ActivityPanel data={data} /> : null}
        {activeTab === "security" ? (
          <SecurityPanel
            data={data}
            onChangePassword={handlePasswordChange}
            onRemoveSession={handleRemoveSession}
            onSaveRecoveryEmail={(payload) =>
              handleSectionSave(
                "security",
                payload,
                "Security preferences updated."
              )
            }
            onSignOutOtherSessions={handleSignOutOtherSessions}
            onToggleTwoFactor={handleToggleTwoFactor}
            saving={
              saveSection.isPending ||
              toggleTwoFactor.isPending ||
              updatePassword.isPending ||
              removeSession.isPending ||
              signOutOtherSessions.isPending
            }
          />
        ) : null}
        {activeTab === "privacy" ? (
          <PrivacyPanel
            data={data.privacy}
            onSave={(payload) =>
              handleSectionSave("privacy", payload, "Privacy settings saved.")
            }
            saving={saveSection.isPending}
          />
        ) : null}
        {activeTab === "notifications" ? (
          <NotificationsPanel
            data={data.notifications}
            onSave={(payload) =>
              handleSectionSave(
                "notifications",
                payload,
                "Notification preferences saved."
              )
            }
            saving={saveSection.isPending}
          />
        ) : null}
        {activeTab === "subscription" ? (
          <SubscriptionPanel data={data} />
        ) : null}
        {activeTab === "connections" ? (
          <ConnectionsPanel
            data={data.connections}
            onSave={(payload) =>
              handleSectionSave(
                "connections",
                payload,
                "Connection preferences saved."
              )
            }
            saving={saveSection.isPending}
          />
        ) : null}
        {activeTab === "delete" ? (
          <DeletePanel data={data} onReset={handleResetWorkspace} />
        ) : null}
      </section>
    </div>
  );
}

function ProfilePanel({
  countries,
  data,
  onReset,
  onSave,
  professions,
  saving,
}: {
  countries: string[];
  data: LearnerSettingsProfile;
  onReset: () => Promise<void>;
  onSave: (payload: Partial<LearnerSettingsProfile>) => Promise<void>;
  professions: Array<{ id: string; label: string }>;
  saving: boolean;
}) {
  const [formState, setFormState] = useState(data);

  useEffect(() => {
    setFormState(data);
  }, [data]);

  return (
    <div className={styles.panelGrid}>
      <div className={styles.splitGrid}>
        <Card className={`${styles.panelCard} ${styles.panelCardSoft}`}>
          <CardBody>
            <p className={styles.eyebrow}>Identity</p>
            <h3 className={styles.sectionTitle}>Profile details</h3>
            <p className={styles.sectionText}>
              These values power your reminders, profession-aware defaults, and
              account contact details.
            </p>
            <div className={styles.chipRow}>
              <span className={styles.chip}>{formState.username}</span>
              <span className={styles.chip}>{formState.timezone}</span>
            </div>
          </CardBody>
        </Card>
        <Card className={styles.panelCard}>
          <CardBody>
            <p className={styles.eyebrow}>Workspace status</p>
            <h3 className={styles.sectionTitle}>Account context</h3>
            <p className={styles.sectionText}>
              Keep profession, country target, and timezone aligned so every
              learner flow stays context-aware.
            </p>
            <div className={styles.summaryGrid}>
              <div className={styles.summaryTile}>
                <span className={styles.summaryLabel}>Profession</span>
                <span className={styles.summaryValue}>
                  {professions.find(
                    (item) => item.id === formState.professionId
                  )?.label ?? "Not set"}
                </span>
              </div>
              <div className={styles.summaryTile}>
                <span className={styles.summaryLabel}>Target country</span>
                <span className={styles.summaryValue}>
                  {formState.targetCountry}
                </span>
              </div>
            </div>
          </CardBody>
        </Card>
      </div>

      <Card className={styles.panelCard}>
        <CardBody>
          <Form
            onSubmit={(event) => {
              event.preventDefault();
              void onSave(formState);
            }}
          >
            <Row className="g-3">
              <Col md={6}>
                <FormGroup>
                  <Label for="settings-full-name">Full name</Label>
                  <Input
                    id="settings-full-name"
                    value={formState.fullName}
                    onChange={(event) =>
                      setFormState((current) => ({
                        ...current,
                        fullName: event.target.value,
                      }))
                    }
                  />
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label for="settings-username">Username</Label>
                  <Input
                    id="settings-username"
                    value={formState.username}
                    onChange={(event) =>
                      setFormState((current) => ({
                        ...current,
                        username: event.target.value,
                      }))
                    }
                  />
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label for="settings-email">Email</Label>
                  <Input
                    id="settings-email"
                    type="email"
                    value={formState.email}
                    onChange={(event) =>
                      setFormState((current) => ({
                        ...current,
                        email: event.target.value,
                      }))
                    }
                  />
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label for="settings-phone">Phone number</Label>
                  <Input
                    id="settings-phone"
                    value={formState.phoneNumber}
                    onChange={(event) =>
                      setFormState((current) => ({
                        ...current,
                        phoneNumber: event.target.value,
                      }))
                    }
                  />
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label for="settings-profession">Profession</Label>
                  <Input
                    id="settings-profession"
                    type="select"
                    value={formState.professionId}
                    onChange={(event) =>
                      setFormState((current) => ({
                        ...current,
                        professionId: event.target.value,
                      }))
                    }
                  >
                    {professions.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.label}
                      </option>
                    ))}
                  </Input>
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label for="settings-country">Target country</Label>
                  <Input
                    id="settings-country"
                    type="select"
                    value={formState.targetCountry}
                    onChange={(event) =>
                      setFormState((current) => ({
                        ...current,
                        targetCountry: event.target.value,
                      }))
                    }
                  >
                    {countries.map((country) => (
                      <option key={country} value={country}>
                        {country}
                      </option>
                    ))}
                  </Input>
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label for="settings-language">Preferred language</Label>
                  <Input
                    id="settings-language"
                    type="select"
                    value={formState.preferredLanguage}
                    onChange={(event) =>
                      setFormState((current) => ({
                        ...current,
                        preferredLanguage: event.target.value,
                      }))
                    }
                  >
                    {LANGUAGE_OPTIONS.map((item) => (
                      <option key={item} value={item}>
                        {item}
                      </option>
                    ))}
                  </Input>
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label for="settings-timezone">Timezone</Label>
                  <Input
                    id="settings-timezone"
                    type="select"
                    value={formState.timezone}
                    onChange={(event) =>
                      setFormState((current) => ({
                        ...current,
                        timezone: event.target.value,
                      }))
                    }
                  >
                    {TIMEZONE_OPTIONS.map((item) => (
                      <option key={item} value={item}>
                        {item}
                      </option>
                    ))}
                  </Input>
                </FormGroup>
              </Col>
            </Row>
            <div className={styles.formActions}>
              <Button
                color="light-secondary"
                onClick={() => {
                  void onReset();
                }}
                type="button"
              >
                Reset to defaults
              </Button>
              <Button color="primary" disabled={saving} type="submit">
                {saving ? "Saving..." : "Save profile"}
              </Button>
            </div>
          </Form>
        </CardBody>
      </Card>
    </div>
  );
}

function ActivityPanel({ data }: { data: LearnerSettingsWorkspaceData }) {
  return (
    <div className={styles.panelGrid}>
      <div className={styles.metricGrid}>
        <div className={styles.metricTile}>
          <span className={styles.summaryLabel}>Study minutes</span>
          <p className={styles.metricValue}>
            {data.activitySummary.studyMinutesThisWeek}
          </p>
          <p className={styles.metricHelper}>Captured this week</p>
        </div>
        <div className={styles.metricTile}>
          <span className={styles.summaryLabel}>Review minutes</span>
          <p className={styles.metricValue}>
            {data.activitySummary.reviewMinutesThisWeek}
          </p>
          <p className={styles.metricHelper}>Feedback time this week</p>
        </div>
        <div className={styles.metricTile}>
          <span className={styles.summaryLabel}>Active devices</span>
          <p className={styles.metricValue}>
            {data.activitySummary.activeDevices}
          </p>
          <p className={styles.metricHelper}>Trusted right now</p>
        </div>
        <div className={styles.metricTile}>
          <span className={styles.summaryLabel}>Last login</span>
          <p className={styles.metricValue}>
            {formatDateOnly(data.activitySummary.lastLoginAt)}
          </p>
          <p className={styles.metricHelper}>Most recent session</p>
        </div>
      </div>

      <Card className={styles.panelCard}>
        <CardBody>
          <h3 className={styles.sectionTitle}>Recent learner activity</h3>
          <p className={styles.sectionText}>
            A compact history of the study, review, and session signals that
            matter most.
          </p>
          <div className={styles.timelineList}>
            {data.activity.map((item) => {
              const tone = getToneStyle(item.tone);

              return (
                <div className={styles.timelineItem} key={item.id}>
                  <span
                    className={styles.timelineTone}
                    style={{ background: tone.background, color: tone.color }}
                  >
                    <Reports height={20} width={20} />
                  </span>
                  <div>
                    <span className={styles.timelineTimestamp}>
                      {formatDateTime(item.timestamp)}
                    </span>
                    <h4 className={styles.timelineTitle}>{item.title}</h4>
                    <p className={styles.timelineDescription}>
                      {item.description}
                    </p>
                  </div>
                </div>
              );
            })}
          </div>
        </CardBody>
      </Card>
    </div>
  );
}

function SecurityPanel({
  data,
  onChangePassword,
  onRemoveSession,
  onSaveRecoveryEmail,
  onSignOutOtherSessions,
  onToggleTwoFactor,
  saving,
}: {
  data: LearnerSettingsWorkspaceData;
  onChangePassword: (newPassword: string) => Promise<void>;
  onRemoveSession: (sessionId: string) => Promise<void>;
  onSaveRecoveryEmail: (payload: {
    recoveryEmail: string;
    twoFactorMethod: "authenticator" | "email";
  }) => Promise<void>;
  onSignOutOtherSessions: () => Promise<void>;
  onToggleTwoFactor: (enabled: boolean) => Promise<void>;
  saving: boolean;
}) {
  const [recoveryEmail, setRecoveryEmail] = useState(
    data.security.recoveryEmail
  );
  const [twoFactorMethod, setTwoFactorMethod] = useState<
    "authenticator" | "email"
  >(data.security.twoFactorMethod);
  const [passwordForm, setPasswordForm] = useState({
    confirmPassword: "",
    currentPassword: "",
    newPassword: "",
  });
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [visibility, setVisibility] = useState({
    confirmPassword: false,
    currentPassword: false,
    newPassword: false,
  });

  useEffect(() => {
    setRecoveryEmail(data.security.recoveryEmail);
    setTwoFactorMethod(data.security.twoFactorMethod);
  }, [data.security.recoveryEmail, data.security.twoFactorMethod]);

  const leftSessions = data.security.trustedSessions.filter(
    (_, index) => index % 2 === 0
  );
  const rightSessions = data.security.trustedSessions.filter(
    (_, index) => index % 2 === 1
  );

  const getSessionIconClass = (platform: string) => {
    const normalized = platform.toLowerCase();

    if (
      normalized.includes("iphone") ||
      normalized.includes("ios") ||
      normalized.includes("android") ||
      normalized.includes("mobile")
    ) {
      return "ph-device-mobile";
    }

    if (normalized.includes("windows")) {
      return "ph-monitor";
    }

    return "ph-laptop";
  };

  const toggleVisibility = (
    field: "confirmPassword" | "currentPassword" | "newPassword"
  ) => {
    setVisibility((current) => ({
      ...current,
      [field]: !current[field],
    }));
  };

  const renderSessionColumn = (
    sessions: LearnerSettingsWorkspaceData["security"]["trustedSessions"],
    highlightFirst: boolean
  ) => (
    <ul className="active-device-session active-device-list">
      {sessions.map((session, index) => (
        <li key={session.id}>
          <Card
            className={
              highlightFirst && index === 0 && session.status === "current"
                ? "share-menu-active"
                : ""
            }
          >
            <CardBody>
              <div className="device-menu-item" draggable={false}>
                <span className="device-menu-img">
                  <i
                    className={`ph-duotone ${getSessionIconClass(session.platform)} f-s-40 text-primary`}
                  ></i>
                </span>
                <div className="device-menu-content">
                  <h6 className="mb-0 txt-ellipsis-1">{session.deviceName}</h6>
                  <p className="mb-0 txt-ellipsis-1 text-secondary">
                    {session.location} {session.ipAddress}
                  </p>
                  <p className="mb-0 mt-1 text-secondary f-s-12">
                    Last active {formatDateTime(session.lastActiveAt)}
                  </p>
                </div>
                <div className="device-menu-icons text-end">
                  <Badge
                    color="light-secondary"
                    className="p-2 f-s-16 text-secondary"
                  >
                    <IconCircleXFilled
                      size={16}
                      className={`me-1 ${
                        session.status === "current" ||
                        session.status === "active"
                          ? "text-success"
                          : "text-primary"
                      }`}
                    />
                    {session.status === "current"
                      ? "Current"
                      : session.status === "active"
                        ? "Online"
                        : "Offline"}
                  </Badge>
                  {session.status !== "current" ? (
                    <div className="mt-2">
                      <Button
                        color="light-danger"
                        disabled={saving}
                        onClick={() => {
                          void onRemoveSession(session.id);
                        }}
                        size="sm"
                        type="button"
                      >
                        Remove
                      </Button>
                    </div>
                  ) : null}
                </div>
              </div>
            </CardBody>
          </Card>
        </li>
      ))}
    </ul>
  );

  return (
    <div className={styles.panelGrid}>
      <Card className="security-card-content mb-4">
        <CardBody>
          <Row className="align-items-center">
            <Col sm="8">
              <h5 className="text-primary fw-semibold">Account Security</h5>
              <p className="text-secondary fs-6 mt-2 mb-0">
                Keep the learner workspace protected with clear two-step
                verification and a recovery channel you can trust.
              </p>
            </Col>
            <Col sm="4" className="text-end">
              <img
                alt="Account"
                className="w-100"
                src="/images/setting/account.png"
              />
            </Col>
          </Row>
        </CardBody>
      </Card>

      <Card className="mb-4">
        <CardBody>
          <Row className="security-box-card align-items-center">
            <Col md="3" className="position-relative">
              <span className="anti-code">
                <i className="ph-duotone ph-shield-check f-s-24 text-primary"></i>
              </span>
              <p className="security-box-title text-dark f-w-500 f-s-16 ms-5 security-code">
                Two-step verification
              </p>
            </Col>
            <Col md="6" className="security-discription">
              <p className="text-secondary fs-6 mb-2">
                {data.security.twoFactorEnabled
                  ? "Two-step verification is active and protecting learner access."
                  : "Enable two-step verification to add another layer of protection."}
              </p>
              <Badge color="light-secondary" className="text-secondary p-2">
                {data.security.twoFactorMethod === "authenticator"
                  ? "Authenticator app"
                  : "Email backup"}{" "}
                · Updated {formatDateTime(data.security.lastPasswordChanged)}
              </Badge>
            </Col>
            <Col md="3" className="text-end">
              <Button
                color={
                  data.security.twoFactorEnabled ? "light-secondary" : "primary"
                }
                disabled={saving}
                onClick={() => {
                  void onToggleTwoFactor(!data.security.twoFactorEnabled);
                }}
                type="button"
              >
                {data.security.twoFactorEnabled ? "Turn Off" : "Turn On"}
              </Button>
            </Col>
          </Row>
        </CardBody>
      </Card>

      <Card className="mb-4">
        <CardBody>
          <Form
            onSubmit={(event) => {
              event.preventDefault();
              void onSaveRecoveryEmail({
                recoveryEmail,
                twoFactorMethod,
              });
            }}
          >
            <Row className="security-box-card align-items-center">
              <Col md="3" className="position-relative">
                <span className="anti-code">
                  <i className="ph-duotone ph-envelope-simple-open f-s-24 text-primary"></i>
                </span>
                <p className="security-box-title text-dark f-w-500 f-s-16 ms-5 security-code">
                  Recovery channel
                </p>
              </Col>
              <Col md="9" className="security-discription">
                <Row className="g-3">
                  <Col md="7">
                    <Input
                      id="settings-recovery-email"
                      type="email"
                      value={recoveryEmail}
                      onChange={(event) => setRecoveryEmail(event.target.value)}
                    />
                  </Col>
                  <Col md="3">
                    <Input
                      id="settings-2fa-method"
                      type="select"
                      value={twoFactorMethod}
                      onChange={(event) =>
                        setTwoFactorMethod(
                          event.target.value as "authenticator" | "email"
                        )
                      }
                    >
                      <option value="email">Email backup</option>
                      <option value="authenticator">Authenticator app</option>
                    </Input>
                  </Col>
                  <Col md="2" className="text-end">
                    <Button color="primary" disabled={saving} type="submit">
                      Save
                    </Button>
                  </Col>
                </Row>
              </Col>
            </Row>
          </Form>
        </CardBody>
      </Card>

      <Card className="security-card-content mb-4">
        <CardBody>
          <Row className="align-items-center">
            <Col sm="9">
              <h5 className="text-primary fw-semibold">
                Devices and active sessions
              </h5>
              <p className="text-secondary fs-6 mt-3">
                Review where this learner workspace is active and remove any
                device you no longer recognise.
              </p>
            </Col>
            <Col sm="3" className="text-end">
              <img
                alt="Device"
                className="w-100"
                src="/images/setting/device.png"
              />
            </Col>
          </Row>
        </CardBody>
      </Card>

      <div className="d-flex justify-content-end mb-3">
        <Button
          color="light-secondary"
          disabled={saving}
          onClick={() => {
            void onSignOutOtherSessions();
          }}
          type="button"
        >
          Sign out others
        </Button>
      </div>

      <Row>
        <Col lg="12" xxl="6">
          {renderSessionColumn(leftSessions, true)}
        </Col>
        <Col lg="12" xxl="6">
          {renderSessionColumn(rightSessions, false)}
        </Col>
      </Row>

      <Card className="security-card-content">
        <CardBody>
          <div className="account-security mb-2">
            <Row className="align-items-center">
              <Col sm="9">
                <h5 className="text-primary fw-semibold">Change Password</h5>
                <p className="account-discription text-secondary fs-6 mt-3">
                  To change your password, please fill in the fields below. Your
                  password must contain at least 8 characters and include at
                  least one uppercase letter, one lowercase letter, one number,
                  and one special character.
                </p>
              </Col>
              <Col sm="3" className="account-security-img">
                <img
                  alt="Password Illustration"
                  className="w-100"
                  src="/images/setting/password.png"
                />
              </Col>
            </Row>
          </div>

          <Form
            className="app-form"
            onSubmit={(event) => {
              event.preventDefault();

              if (!passwordForm.currentPassword.trim()) {
                setPasswordError(
                  "Enter your current password before updating it."
                );
                return;
              }

              if (
                !passwordForm.newPassword.trim() ||
                passwordForm.newPassword !== passwordForm.confirmPassword
              ) {
                setPasswordError("New password and confirmation must match.");
                return;
              }

              setPasswordError(null);
              void onChangePassword(passwordForm.newPassword).then(() => {
                setPasswordForm({
                  confirmPassword: "",
                  currentPassword: "",
                  newPassword: "",
                });
              });
            }}
          >
            {passwordError ? (
              <Alert color="danger">{passwordError}</Alert>
            ) : null}
            <Row>
              <Col sm="12" className="mb-3">
                <Label for="settings-current-password" className="form-label">
                  Current Password
                </Label>
                <InputGroup className="input-group-password">
                  <InputGroupText className="b-r-left">
                    <i className="ph-bold ph-lock f-s-20" />
                  </InputGroupText>
                  <Input
                    id="settings-current-password"
                    placeholder="********"
                    type={visibility.currentPassword ? "text" : "password"}
                    value={passwordForm.currentPassword}
                    onChange={(event) =>
                      setPasswordForm((current) => ({
                        ...current,
                        currentPassword: event.target.value,
                      }))
                    }
                  />
                  <InputGroupText
                    className="b-r-right cursor-pointer"
                    onClick={() => toggleVisibility("currentPassword")}
                  >
                    <i
                      className={`ph f-s-20 ${
                        visibility.currentPassword ? "ph-eye" : "ph-eye-slash"
                      }`}
                    />
                  </InputGroupText>
                </InputGroup>
              </Col>

              <Col sm="12" className="mb-3">
                <Label for="settings-new-password" className="form-label">
                  New Password
                </Label>
                <InputGroup className="input-group-password">
                  <InputGroupText className="b-r-left">
                    <i className="ph-bold ph-lock f-s-20" />
                  </InputGroupText>
                  <Input
                    id="settings-new-password"
                    placeholder="********"
                    type={visibility.newPassword ? "text" : "password"}
                    value={passwordForm.newPassword}
                    onChange={(event) =>
                      setPasswordForm((current) => ({
                        ...current,
                        newPassword: event.target.value,
                      }))
                    }
                  />
                  <InputGroupText
                    className="b-r-right cursor-pointer"
                    onClick={() => toggleVisibility("newPassword")}
                  >
                    <i
                      className={`ph f-s-20 ${
                        visibility.newPassword ? "ph-eye" : "ph-eye-slash"
                      }`}
                    />
                  </InputGroupText>
                </InputGroup>
              </Col>

              <Col sm="12" className="mb-3">
                <Label for="settings-confirm-password" className="form-label">
                  Confirm Password
                </Label>
                <InputGroup className="input-group-password">
                  <InputGroupText className="b-r-left">
                    <i className="ph-bold ph-lock f-s-20" />
                  </InputGroupText>
                  <Input
                    id="settings-confirm-password"
                    placeholder="********"
                    type={visibility.confirmPassword ? "text" : "password"}
                    value={passwordForm.confirmPassword}
                    onChange={(event) =>
                      setPasswordForm((current) => ({
                        ...current,
                        confirmPassword: event.target.value,
                      }))
                    }
                  />
                  <InputGroupText
                    className="b-r-right cursor-pointer"
                    onClick={() => toggleVisibility("confirmPassword")}
                  >
                    <i
                      className={`ph f-s-20 ${
                        visibility.confirmPassword ? "ph-eye" : "ph-eye-slash"
                      }`}
                    />
                  </InputGroupText>
                </InputGroup>
              </Col>
            </Row>
            <div className="text-end">
              <Button color="primary" disabled={saving} type="submit">
                {saving ? "Updating..." : "Update Password"}
              </Button>
            </div>
          </Form>
        </CardBody>
      </Card>
    </div>
  );
}

function PrivacyPanel({
  data,
  onSave,
  saving,
}: {
  data: LearnerPrivacyPreferences;
  onSave: (payload: Partial<LearnerPrivacyPreferences>) => Promise<void>;
  saving: boolean;
}) {
  const [formState, setFormState] = useState(data);

  useEffect(() => {
    setFormState(data);
  }, [data]);

  return (
    <div className={styles.panelGrid}>
      <Card className={styles.panelCard}>
        <CardBody>
          <Form
            onSubmit={(event) => {
              event.preventDefault();
              void onSave(formState);
            }}
          >
            <div className={styles.splitGrid}>
              <div>
                <h3 className={styles.sectionTitle}>
                  Visibility and retention
                </h3>
                <p className={styles.sectionText}>
                  Keep learner privacy grounded in who can see coaching context
                  and how long transcripts stay available.
                </p>
              </div>
              <div className={styles.summaryGrid}>
                <div className={styles.summaryTile}>
                  <span className={styles.summaryLabel}>
                    Profile visibility
                  </span>
                  <span className={styles.summaryValue}>
                    {formState.profileVisibility}
                  </span>
                </div>
                <div className={styles.summaryTile}>
                  <span className={styles.summaryLabel}>Retention</span>
                  <span className={styles.summaryValue}>
                    {formState.transcriptRetention}
                  </span>
                </div>
              </div>
            </div>

            <Row className="g-3 mt-1">
              <Col md={6}>
                <FormGroup>
                  <Label for="settings-privacy-visibility">
                    Profile visibility
                  </Label>
                  <Input
                    id="settings-privacy-visibility"
                    type="select"
                    value={formState.profileVisibility}
                    onChange={(event) =>
                      setFormState((current) => ({
                        ...current,
                        profileVisibility: event.target
                          .value as LearnerPrivacyPreferences["profileVisibility"],
                      }))
                    }
                  >
                    <option value="private">Private</option>
                    <option value="coached">Visible to coaches</option>
                    <option value="public">Public profile</option>
                  </Input>
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label for="settings-privacy-retention">
                    Transcript retention
                  </Label>
                  <Input
                    id="settings-privacy-retention"
                    type="select"
                    value={formState.transcriptRetention}
                    onChange={(event) =>
                      setFormState((current) => ({
                        ...current,
                        transcriptRetention: event.target
                          .value as LearnerPrivacyPreferences["transcriptRetention"],
                      }))
                    }
                  >
                    <option value="30-days">30 days</option>
                    <option value="90-days">90 days</option>
                    <option value="1-year">1 year</option>
                  </Input>
                </FormGroup>
              </Col>
            </Row>

            <div className={styles.preferenceList}>
              {[
                {
                  description:
                    "Allow expert reviewers to see your transcript and session context when feedback is requested.",
                  field: "expertSharingConsent",
                  label: "Expert sharing consent",
                },
                {
                  description:
                    "Allow audio recordings to remain available for replays and revision loops.",
                  field: "audioStorageConsent",
                  label: "Audio storage consent",
                },
                {
                  description:
                    "Allow anonymized analytics to improve study-plan and review routing quality.",
                  field: "analyticsOptIn",
                  label: "Analytics opt-in",
                },
                {
                  description:
                    "Receive campaign and feature updates outside operational reminders.",
                  field: "marketingEmailsEnabled",
                  label: "Marketing emails",
                },
              ].map((item) => (
                <div className={styles.preferenceItem} key={item.field}>
                  <span
                    className={styles.preferenceTone}
                    style={getToneStyle("primary")}
                  >
                    <LockOpen size={20} weight="bold" />
                  </span>
                  <div className={styles.inlineSwitch}>
                    <div className={styles.inlineSwitchCopy}>
                      <h4 className={styles.timelineTitle}>{item.label}</h4>
                      <p className={styles.timelineDescription}>
                        {item.description}
                      </p>
                    </div>
                    <Input
                      checked={Boolean(
                        formState[item.field as keyof LearnerPrivacyPreferences]
                      )}
                      onChange={(event) =>
                        setFormState((current) => ({
                          ...current,
                          [item.field]: event.target.checked,
                        }))
                      }
                      type="switch"
                    />
                  </div>
                </div>
              ))}
            </div>

            <div className={styles.formActions}>
              <Button color="primary" disabled={saving} type="submit">
                {saving ? "Saving..." : "Save privacy settings"}
              </Button>
            </div>
          </Form>
        </CardBody>
      </Card>
    </div>
  );
}

function NotificationsPanel({
  data,
  onSave,
  saving,
}: {
  data: LearnerNotificationPreferences;
  onSave: (payload: Partial<LearnerNotificationPreferences>) => Promise<void>;
  saving: boolean;
}) {
  const [formState, setFormState] = useState(data);

  useEffect(() => {
    setFormState(data);
  }, [data]);

  return (
    <div className={styles.panelGrid}>
      <Card className={styles.panelCard}>
        <CardBody>
          <Form
            onSubmit={(event) => {
              event.preventDefault();
              void onSave(formState);
            }}
          >
            <div className={styles.splitGrid}>
              <div>
                <h3 className={styles.sectionTitle}>Delivery channels</h3>
                <p className={styles.sectionText}>
                  Focus on high-value review and session reminders rather than a
                  noisy generic notification center.
                </p>
              </div>
              <FormGroup>
                <Label for="settings-reminder-cadence">Reminder cadence</Label>
                <Input
                  id="settings-reminder-cadence"
                  type="select"
                  value={formState.reminderCadence}
                  onChange={(event) =>
                    setFormState((current) => ({
                      ...current,
                      reminderCadence: event.target
                        .value as LearnerNotificationPreferences["reminderCadence"],
                    }))
                  }
                >
                  <option value="quiet">Quiet</option>
                  <option value="balanced">Balanced</option>
                  <option value="high-touch">High-touch</option>
                </Input>
              </FormGroup>
            </div>

            <div className={styles.preferenceList}>
              {[
                {
                  description:
                    "Send review completions and feedback alerts to email.",
                  field: "emailReviewAlerts",
                  label: "Email review alerts",
                  tone: "info",
                },
                {
                  description:
                    "Send browser nudges before practice blocks and checkpoints.",
                  field: "browserPracticeReminders",
                  label: "Browser practice reminders",
                  tone: "primary",
                },
                {
                  description:
                    "Keep the home surfaces updated with light progress digests.",
                  field: "inAppProgressDigest",
                  label: "In-app progress digest",
                  tone: "success",
                },
                {
                  description:
                    "Send timetable and booking reminders for live sessions.",
                  field: "sessionReminders",
                  label: "Session reminders",
                  tone: "warning",
                },
                {
                  description:
                    "Escalate overdue review and weak-confidence prompts.",
                  field: "reviewNudges",
                  label: "Review nudges",
                  tone: "danger",
                },
                {
                  description:
                    "Send weekly planning prompts before the next study reset.",
                  field: "weeklyPlanningDigest",
                  label: "Weekly planning digest",
                  tone: "primary",
                },
                {
                  description:
                    "Enable WhatsApp reminders for urgent study and session prompts.",
                  field: "whatsappReminders",
                  label: "WhatsApp reminders",
                  tone: "success",
                },
              ].map((item) => (
                <div className={styles.preferenceItem} key={item.field}>
                  <span
                    className={styles.preferenceTone}
                    style={getToneStyle(
                      item.tone as LearnerSettingsActivityItem["tone"]
                    )}
                  >
                    <BellSimple size={20} weight="bold" />
                  </span>
                  <div className={styles.inlineSwitch}>
                    <div className={styles.inlineSwitchCopy}>
                      <h4 className={styles.timelineTitle}>{item.label}</h4>
                      <p className={styles.timelineDescription}>
                        {item.description}
                      </p>
                    </div>
                    <Input
                      checked={Boolean(
                        formState[
                          item.field as keyof LearnerNotificationPreferences
                        ]
                      )}
                      onChange={(event) =>
                        setFormState((current) => ({
                          ...current,
                          [item.field]: event.target.checked,
                        }))
                      }
                      type="switch"
                    />
                  </div>
                </div>
              ))}
            </div>

            <div className={styles.formActions}>
              <Button color="primary" disabled={saving} type="submit">
                {saving ? "Saving..." : "Save notifications"}
              </Button>
            </div>
          </Form>
        </CardBody>
      </Card>
    </div>
  );
}

function SubscriptionPanel({ data }: { data: LearnerSettingsWorkspaceData }) {
  return (
    <div className={styles.panelGrid}>
      <div className={styles.metricGrid}>
        <div className={styles.metricTile}>
          <span className={styles.summaryLabel}>Current plan</span>
          <p className={styles.metricValue}>{data.subscription.currentPlan}</p>
          <p className={styles.metricHelper}>Active learner tier</p>
        </div>
        <div className={styles.metricTile}>
          <span className={styles.summaryLabel}>Next renewal</span>
          <p className={styles.metricValue}>
            {formatDateOnly(data.subscription.nextRenewal)}
          </p>
          <p className={styles.metricHelper}>Billing date</p>
        </div>
        <div className={styles.metricTile}>
          <span className={styles.summaryLabel}>Review credits</span>
          <p className={styles.metricValue}>
            {data.subscription.reviewCredits}
          </p>
          <p className={styles.metricHelper}>Available now</p>
        </div>
        <div className={styles.metricTile}>
          <span className={styles.summaryLabel}>Reserved credits</span>
          <p className={styles.metricValue}>
            {data.subscription.reservedCredits}
          </p>
          <p className={styles.metricHelper}>Already committed</p>
        </div>
      </div>

      <div className={styles.splitGrid}>
        <Card className={styles.panelCard}>
          <CardBody>
            <h3 className={styles.sectionTitle}>Billing snapshot</h3>
            <p className={styles.sectionText}>
              This stays intentionally compact. Full invoices, plan changes, and
              purchase flows live in the dedicated billing route.
            </p>
            <div className={styles.summaryGrid}>
              <div className={styles.summaryTile}>
                <span className={styles.summaryLabel}>Payment method</span>
                <span className={styles.summaryValue}>
                  {data.subscription.paymentMethodLabel}
                </span>
              </div>
              <div className={styles.summaryTile}>
                <span className={styles.summaryLabel}>Reminder channel</span>
                <span className={styles.summaryValue}>
                  {data.subscription.reminderChannel}
                </span>
              </div>
            </div>
          </CardBody>
        </Card>

        <Card className={`${styles.panelCard} ${styles.panelCardSoft}`}>
          <CardBody>
            <span className={styles.badgePill}>Billing stays separate</span>
            <h3 className={styles.sectionTitle}>Go to billing center</h3>
            <p className={styles.sectionText}>
              Use the dedicated billing route for invoices, upgrades, and extra
              review purchases.
            </p>
            <div className={styles.ctaRow}>
              <Button color="primary" tag={Link} href="/learner/billing">
                Open billing
              </Button>
              <Button
                color="light-secondary"
                tag={Link}
                href="/learner/billing"
              >
                Manage credits
              </Button>
            </div>
          </CardBody>
        </Card>
      </div>
    </div>
  );
}

function ConnectionsPanel({
  data,
  onSave,
  saving,
}: {
  data: LearnerConnectionPreferences;
  onSave: (payload: Partial<LearnerConnectionPreferences>) => Promise<void>;
  saving: boolean;
}) {
  const [formState, setFormState] = useState(data);
  const [checkMessage, setCheckMessage] = useState<string | null>(null);

  useEffect(() => {
    setFormState(data);
  }, [data]);

  return (
    <div className={styles.panelGrid}>
      <Card className={styles.panelCard}>
        <CardBody>
          <Form
            onSubmit={(event) => {
              event.preventDefault();
              void onSave(formState);
            }}
          >
            <div className={styles.splitGrid}>
              <div>
                <h3 className={styles.sectionTitle}>Practice readiness</h3>
                <p className={styles.sectionText}>
                  These controls keep reminders, audio handling, and timed-task
                  reliability aligned with the learner device setup.
                </p>
              </div>
              <div className={styles.summaryGrid}>
                <div className={styles.summaryTile}>
                  <span className={styles.summaryLabel}>Calendar sync</span>
                  <span className={styles.summaryValue}>
                    {formState.calendarSync}
                  </span>
                </div>
                <div className={styles.summaryTile}>
                  <span className={styles.summaryLabel}>Playback speed</span>
                  <span className={styles.summaryValue}>
                    {formState.playbackSpeed}
                  </span>
                </div>
              </div>
            </div>

            {checkMessage ? (
              <Alert color="success">{checkMessage}</Alert>
            ) : null}

            <Row className="g-3 mt-1">
              <Col md={6}>
                <FormGroup>
                  <Label for="settings-calendar-sync">Calendar sync</Label>
                  <Input
                    id="settings-calendar-sync"
                    type="select"
                    value={formState.calendarSync}
                    onChange={(event) =>
                      setFormState((current) => ({
                        ...current,
                        calendarSync: event.target
                          .value as LearnerConnectionPreferences["calendarSync"],
                      }))
                    }
                  >
                    <option value="google">Google Calendar</option>
                    <option value="outlook">Outlook Calendar</option>
                    <option value="none">No sync</option>
                  </Input>
                </FormGroup>
              </Col>
              <Col md={6}>
                <FormGroup>
                  <Label for="settings-playback-speed">Playback speed</Label>
                  <Input
                    id="settings-playback-speed"
                    type="select"
                    value={formState.playbackSpeed}
                    onChange={(event) =>
                      setFormState((current) => ({
                        ...current,
                        playbackSpeed: event.target
                          .value as LearnerConnectionPreferences["playbackSpeed"],
                      }))
                    }
                  >
                    <option value="1.0x">1.0x</option>
                    <option value="1.25x">1.25x</option>
                    <option value="1.5x">1.5x</option>
                  </Input>
                </FormGroup>
              </Col>
            </Row>

            <div className={styles.preferenceList}>
              {[
                {
                  description:
                    "Allow browser-level practice nudges when the study block is due.",
                  field: "browserNotifications",
                  label: "Browser notifications",
                },
                {
                  description:
                    "Keep caption access visible during listening and speaking playback.",
                  field: "captionsEnabled",
                  label: "Captions enabled",
                },
                {
                  description:
                    "Prefer lighter media delivery for unreliable mobile or hostel bandwidth.",
                  field: "lowBandwidthMode",
                  label: "Low-bandwidth mode",
                },
                {
                  description:
                    "Current microphone state is learner-approved for mock and speaking routes.",
                  field: "microphoneReady",
                  label: "Microphone ready",
                },
                {
                  description:
                    "Headset is recognized and available for quieter practice sessions.",
                  field: "headsetReady",
                  label: "Headset ready",
                },
              ].map((item) => (
                <div className={styles.preferenceItem} key={item.field}>
                  <span
                    className={styles.preferenceTone}
                    style={getToneStyle("success")}
                  >
                    <Graph size={20} weight="bold" />
                  </span>
                  <div className={styles.inlineSwitch}>
                    <div className={styles.inlineSwitchCopy}>
                      <h4 className={styles.timelineTitle}>{item.label}</h4>
                      <p className={styles.timelineDescription}>
                        {item.description}
                      </p>
                    </div>
                    <Input
                      checked={Boolean(
                        formState[
                          item.field as keyof LearnerConnectionPreferences
                        ]
                      )}
                      onChange={(event) =>
                        setFormState((current) => ({
                          ...current,
                          [item.field]: event.target.checked,
                        }))
                      }
                      type="switch"
                    />
                  </div>
                </div>
              ))}
            </div>

            <div className={styles.formActions}>
              <Button
                color="light-secondary"
                onClick={() => {
                  setCheckMessage(
                    "Readiness check complete. Browser notifications, microphone, and headset look stable."
                  );
                }}
                type="button"
              >
                Run readiness check
              </Button>
              <Button color="primary" disabled={saving} type="submit">
                {saving ? "Saving..." : "Save connections"}
              </Button>
            </div>
          </Form>
        </CardBody>
      </Card>
    </div>
  );
}

function DeletePanel({
  data,
  onReset,
}: {
  data: LearnerSettingsWorkspaceData;
  onReset: () => Promise<void>;
}) {
  const [deleteNotice, setDeleteNotice] = useState<string | null>(null);

  const exportWorkspace = () => {
    const blob = new Blob([JSON.stringify(data, null, 2)], {
      type: "application/json",
    });
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = "oet-learner-settings-export.json";
    link.click();
    window.URL.revokeObjectURL(url);
  };

  const requestDelete = async () => {
    const { default: Swal } = await import("sweetalert2");
    const result = await Swal.fire({
      cancelButtonText: "Keep account",
      confirmButtonText: "Request deletion",
      confirmButtonColor: "#d63384",
      icon: "warning",
      showCancelButton: true,
      text: "This remains a mock confirmation flow, but the intent should feel explicit and serious.",
      title: "Request account deletion?",
    });

    if (!result.isConfirmed) {
      return;
    }

    setDeleteNotice(
      "Deletion request captured. Support has been notified to review the learner account closure."
    );
  };

  return (
    <div className={styles.panelGrid}>
      {deleteNotice ? <Alert color="success">{deleteNotice}</Alert> : null}

      <Card className={`${styles.panelCard} ${styles.dangerCard}`}>
        <CardBody>
          <p className={styles.eyebrow}>Danger zone</p>
          <h3 className={styles.dangerTitle}>Delete or recover with care</h3>
          <p className={styles.supportCopy}>
            This area prioritizes export and support handoff before any
            destructive action, so the learner never hits a dead end.
          </p>
          <div className={styles.ctaRow}>
            <Button
              color="light-secondary"
              onClick={exportWorkspace}
              type="button"
            >
              Export data
            </Button>
            <Button
              color="light-secondary"
              onClick={() => {
                void onReset();
              }}
              type="button"
            >
              Reset settings
            </Button>
            <Button
              color="danger"
              onClick={() => void requestDelete()}
              type="button"
            >
              Request account deletion
            </Button>
          </div>
        </CardBody>
      </Card>

      <div className={styles.supportGrid}>
        <Card className={styles.supportTile}>
          <CardBody>
            <span className={styles.badgePill}>Support</span>
            <h3 className={styles.sectionTitle}>Email support</h3>
            <p className={styles.supportCopy}>
              Use this when billing, profile, or study-pathway corrections need
              a human handoff.
            </p>
            <div className={styles.ctaRow}>
              <Button
                color="primary"
                href={buildSupportMailto(data.profile.email)}
                tag="a"
              >
                Contact support
              </Button>
            </div>
          </CardBody>
        </Card>

        <Card className={styles.supportTile}>
          <CardBody>
            <span className={styles.badgePill}>WhatsApp</span>
            <h3 className={styles.sectionTitle}>Quick guided help</h3>
            <p className={styles.supportCopy}>
              Share the learner email and get quick setup guidance through a
              familiar support channel.
            </p>
            <div className={styles.ctaRow}>
              <Button
                color="light-success"
                href={buildSupportWhatsAppLink(data.profile.email)}
                rel="noreferrer"
                target="_blank"
                tag="a"
              >
                Contact on WhatsApp
              </Button>
            </div>
          </CardBody>
        </Card>
      </div>
    </div>
  );
}

export default LearnerSettingsWorkspace;
