export type OetRole = "learner" | "expert" | "admin";

export type OetSubtest = "writing" | "speaking" | "reading" | "listening";

export type OetBoardRecipe = "command" | "workflow" | "reporting";

export type OetVisualAccent =
  | "primary"
  | "success"
  | "info"
  | "warning"
  | "danger";

export type AsyncWorkflowStatus =
  | "queued"
  | "processing"
  | "completed"
  | "failed";

export type EnrollmentEntityStatus = "active" | "inactive";

export interface TargetCountry {
  id: string;
  label: string;
  status: EnrollmentEntityStatus;
}

export interface Criterion {
  id: string;
  name: string;
  description: string;
  subtest: OetSubtest;
}

export interface CriterionScore {
  criterionId: string;
  scoreBand: string;
  summary: string;
  strengths: string[];
  improvements: string[];
}

export interface FeedbackItem {
  id: string;
  criterionId?: string;
  title: string;
  detail: string;
  anchorLabel?: string;
  severity: "info" | "warning" | "critical";
}

export interface Profession {
  id: string;
  label: string;
  countryTargets: string[];
  examTypeIds: string[];
  status: EnrollmentEntityStatus;
  description?: string;
}

export interface ExamType {
  id: string;
  label: string;
  code: string;
  description: string;
  status: EnrollmentEntityStatus;
}

export interface EnrollmentSession {
  id: string;
  name: string;
  examTypeId: string;
  professionIds: string[];
  priceLabel: string;
  currency: string;
  startDate: string;
  endDate: string;
  enrollmentOpen: boolean;
  deliveryMode: "online" | "hybrid" | "in-person";
  timezone: string;
  capacity: number;
  seatsRemaining: number;
  status: "upcoming" | "open" | "closed" | "completed";
  description: string;
}

export interface LearnerGoal {
  professionId: string;
  examDate?: string;
  weeklyStudyHours: number;
  targetCountry?: string;
  subtestTargets: Partial<Record<OetSubtest, string>>;
  weakSubtests: OetSubtest[];
  previousAttempts: string[];
}

export interface UserProfile {
  id: string;
  username: string;
  fullName: string;
  role: OetRole;
  professionId?: string;
  email: string;
  avatarUrl: string;
}

export interface ContentItem {
  id: string;
  title: string;
  professionId?: string;
  subtest: OetSubtest;
  difficulty: "foundation" | "target" | "stretch";
  durationMinutes: number;
  criteriaFocus: string[];
  scenarioType: string;
  status: "draft" | "published" | "archived";
}

export interface Attempt {
  id: string;
  contentItemId: string;
  subtest: OetSubtest;
  startedAt: string;
  submittedAt?: string;
  mode: "practice" | "timed" | "mock";
  status: AsyncWorkflowStatus;
}

export interface Evaluation {
  id: string;
  attemptId: string;
  subtest: OetSubtest;
  status: AsyncWorkflowStatus;
  scoreRange: string;
  confidence: "low" | "moderate" | "high";
  summary: string;
  criterionScores: CriterionScore[];
  feedback: FeedbackItem[];
}

export interface StudyPlanItem {
  id: string;
  title: string;
  subtest: OetSubtest;
  durationMinutes: number;
  reason: string;
  dueDate: string;
  status: "up-next" | "scheduled" | "completed" | "skipped";
}

export interface StudyPlan {
  today: StudyPlanItem[];
  thisWeek: StudyPlanItem[];
  checkpoint: {
    title: string;
    date: string;
    summary: string;
  };
}

export interface ReadinessSnapshot {
  overallLabel: string;
  examDate: string;
  weakestLink: string;
  remainingStudyHours: number;
  subtests: Array<{
    subtest: OetSubtest;
    readinessLabel: string;
    scoreRange: string;
    confidence: "low" | "moderate" | "high";
  }>;
}

export interface ReviewRequest {
  id: string;
  learnerId: string;
  attemptId: string;
  subtest: OetSubtest;
  priority: "standard" | "priority";
  focusAreas: string[];
  reviewerNotes: string;
  status: AsyncWorkflowStatus | "in-review";
  assignedReviewer?: string;
  dueAt: string;
}

export interface Subscription {
  currentPlan: string;
  renewalDate: string;
  creditsRemaining: number;
  invoices: Array<{
    id: string;
    issuedAt: string;
    amountLabel: string;
    status: "paid" | "pending";
  }>;
}

export interface WalletCredits {
  total: number;
  reserved: number;
  available: number;
}

export type LearnerSettingsTabId =
  | "profile"
  | "activity"
  | "security"
  | "privacy"
  | "notifications"
  | "subscription"
  | "connections"
  | "delete";

export interface LearnerSettingsProfile {
  avatarUrl: string;
  email: string;
  fullName: string;
  phoneNumber: string;
  preferredLanguage: string;
  professionId: string;
  targetCountry: string;
  timezone: string;
  username: string;
}

export interface LearnerSettingsActivityItem {
  id: string;
  category: "study" | "review" | "security" | "session";
  description: string;
  timestamp: string;
  title: string;
  tone: OetVisualAccent;
}

export interface LearnerSettingsActivitySummary {
  activeDevices: number;
  lastLoginAt: string;
  reviewMinutesThisWeek: number;
  studyMinutesThisWeek: number;
}

export interface LearnerNotificationPreferences {
  browserPracticeReminders: boolean;
  emailReviewAlerts: boolean;
  inAppProgressDigest: boolean;
  reminderCadence: "quiet" | "balanced" | "high-touch";
  reviewNudges: boolean;
  sessionReminders: boolean;
  weeklyPlanningDigest: boolean;
  whatsappReminders: boolean;
}

export interface LearnerPrivacyPreferences {
  analyticsOptIn: boolean;
  audioStorageConsent: boolean;
  expertSharingConsent: boolean;
  marketingEmailsEnabled: boolean;
  profileVisibility: "private" | "coached" | "public";
  transcriptRetention: "30-days" | "90-days" | "1-year";
}

export interface LearnerTrustedSession {
  id: string;
  deviceName: string;
  ipAddress: string;
  lastActiveAt: string;
  location: string;
  platform: string;
  status: "current" | "active" | "idle";
}

export interface LearnerSecurityState {
  lastPasswordChanged: string;
  recoveryEmail: string;
  trustedSessions: LearnerTrustedSession[];
  twoFactorEnabled: boolean;
  twoFactorMethod: "email" | "authenticator";
}

export interface LearnerConnectionPreferences {
  browserNotifications: boolean;
  calendarSync: "google" | "outlook" | "none";
  captionsEnabled: boolean;
  headsetReady: boolean;
  lowBandwidthMode: boolean;
  microphoneReady: boolean;
  playbackSpeed: "1.0x" | "1.25x" | "1.5x";
}

export interface LearnerSettingsSubscriptionSummary {
  currentPlan: string;
  nextRenewal: string;
  paymentMethodLabel: string;
  reminderChannel: string;
  reservedCredits: number;
  reviewCredits: number;
}

export interface LearnerSettingsWorkspaceData {
  activity: LearnerSettingsActivityItem[];
  activitySummary: LearnerSettingsActivitySummary;
  connections: LearnerConnectionPreferences;
  notifications: LearnerNotificationPreferences;
  privacy: LearnerPrivacyPreferences;
  profile: LearnerSettingsProfile;
  security: LearnerSecurityState;
  subscription: LearnerSettingsSubscriptionSummary;
}

export interface SignupDraft {
  firstName: string;
  lastName: string;
  email: string;
  mobileNumber: string;
  examTypeId: string;
  professionId: string;
  sessionId: string;
  countryTarget: string;
  password: string;
  confirmPassword: string;
  agreeToTerms: boolean;
  agreeToPrivacy: boolean;
  marketingOptIn: boolean;
}

export interface SignupPayload extends SignupDraft {}

export interface AsyncWorkflowMeta {
  label: string;
  description: string;
  badgeClass: string;
  retryable: boolean;
}

export interface OetVisualHeroStat {
  label: string;
  value: string | number;
  helper: string;
  delta: string;
  icon: string;
  tone: OetVisualAccent;
}

export interface OetVisualActivityItem {
  badge: string;
  description: string;
  meta: string;
  title: string;
  tone: OetVisualAccent;
}

export interface OetVisualAvatar {
  avatarUrl: string;
  name: string;
  tone: OetVisualAccent;
}

export interface OetVisualChartSeries {
  data: number[];
  name: string;
}

export interface OetVisualChart {
  categories: string[];
  labels?: string[];
  series: OetVisualChartSeries[];
  type: "area" | "bar" | "donut" | "line";
}

export interface OetVisualConfig {
  accent: OetVisualAccent;
  activityItems: OetVisualActivityItem[];
  avatars: OetVisualAvatar[];
  chart: OetVisualChart;
  chips: string[];
  heroStats: OetVisualHeroStat[];
  recipe: OetBoardRecipe;
  summary: string;
}
