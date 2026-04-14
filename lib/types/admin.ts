export interface AdminDashboardData {
  generatedAt: string;
  freshness: {
    contentUpdatedAt: string | null;
    auditUpdatedAt: string | null;
    reviewUpdatedAt: string | null;
    qualityWindow: string;
  };
  contentHealth: {
    published: number;
    drafts: number;
    archived: number;
    staleDrafts: number;
  };
  reviewOps: {
    backlog: number;
    overdue: number;
    failedReviews: number;
    failedJobs: number;
    inProgress: number;
  };
  billingRisk: {
    pendingInvoices: number;
    failedInvoices: number;
    legacyPlans: number;
    activeSubscribers: number;
  };
  flags: {
    total: number;
    enabled: number;
    liveExperiments: number;
    recentChanges: number;
  };
  quality: {
    agreementRate: number;
    avgReviewHours: number;
    riskCases: number;
    evaluationCount: number;
  };
}

export interface AdminContentRow {
  id: string;
  title: string;
  type: string;
  profession: string;
  status: 'draft' | 'published' | 'archived';
  sourceType?: string;
  qaStatus?: string;
  updatedAt: string;
  author: string;
  revisionCount: number;
}

export interface AdminContentDetail {
  id: string;
  title: string;
  contentType: string;
  subtestCode: string;
  professionId: string;
  difficulty: string;
  estimatedDurationMinutes: number;
  status: 'draft' | 'published' | 'archived';
  sourceType?: string;
  qaStatus?: string;
  description: string;
  caseNotes: string;
  modelAnswer: string;
  criteriaFocus: string[];
  revisions: Array<{
    id: string;
    revisionNumber: number;
    state: string;
    changeNote: string;
    createdBy: string;
    createdAt: string;
  }>;
}

export interface AdminContentImpact {
  contentId: string;
  title: string;
  status: string;
  usage: {
    attemptCount: number;
    evaluationCount: number;
    studyPlanReferences: number;
    activeAttempts: number;
  };
  safeToArchive: boolean;
  safeToDelete: boolean;
}

export interface AdminRevisionRow {
  id: string;
  contentId: string;
  date: string;
  author: string;
  state: string;
  note: string;
}

export interface AdminTaxonomyNode {
  id: string;
  label: string;
  slug: string;
  type: 'profession' | 'category';
  status: 'active' | 'archived';
  contentCount: number;
}

export interface AdminTaxonomyImpact {
  professionId: string;
  label: string;
  status: string;
  usage: {
    contentCount: number;
    learnerCount: number;
    goalCount: number;
  };
  safeToArchive: boolean;
}

export interface AdminCriterion {
  id: string;
  name: string;
  type: string;
  weight: number;
  status: 'active' | 'archived';
  description: string;
}

export interface AdminAIConfig {
  id: string;
  model: string;
  provider: string;
  taskType: string;
  status: 'active' | 'testing' | 'deprecated';
  accuracy: number;
  confidenceThreshold: number;
  routingRule: string;
  experimentFlag: string;
  promptLabel: string;
}

export interface AdminFlag {
  id: string;
  name: string;
  key: string;
  enabled: boolean;
  type: string;
  rolloutPercentage: number;
  description: string;
  owner: string;
}

export interface AdminAuditLogRow {
  id: string;
  timestamp: string;
  actor: string;
  action: string;
  resource: string;
  details: string;
}

export interface AdminAuditLogDetail {
  id: string;
  timestamp: string;
  actorId: string;
  actorName: string;
  action: string;
  resourceType: string;
  resourceId: string;
  details: string;
}

export interface AdminUserRow {
  id: string;
  name: string;
  email: string;
  role: 'learner' | 'expert' | 'admin' | 'sponsor';
  status: 'active' | 'suspended' | 'deleted';
  lastLogin: string | null;
}

export interface AdminUserDetail {
  id: string;
  name: string;
  email: string;
  role: 'learner' | 'expert' | 'admin' | 'sponsor';
  status: 'active' | 'suspended' | 'deleted';
  lastLogin: string | null;
  createdAt: string | null;
  authAccountId: string | null;
  profession?: string | null;
  specialties?: string[];
  tasksCompleted?: number;
  tasksGraded?: number;
  creditBalance?: number;
  availableActions: {
    canSuspend: boolean;
    canDelete: boolean;
    canRestore: boolean;
    canAdjustCredits: boolean;
    canTriggerPasswordReset: boolean;
  };
}

export interface AdminUsersPageData {
  total: number;
  page: number;
  pageSize: number;
  items: AdminUserRow[];
}

export interface AdminInvitationResult {
  id: string;
  email: string;
  role: string;
  invitation: {
    purpose: string;
    deliveryChannel: string;
    destinationHint: string;
    expiresAt: string;
    retryAfterSeconds: number;
  };
}

export interface AdminBillingPlan {
  id: string;
  code?: string;
  name: string;
  description?: string;
  price: number;
  currency?: string;
  interval: string;
  durationMonths?: number;
  includedCredits?: number;
  displayOrder?: number;
  isVisible?: boolean;
  isRenewable?: boolean;
  trialDays?: number;
  activeSubscribers: number;
  status: string;
  includedSubtests?: string[];
  entitlements?: Record<string, unknown>;
  archivedAt?: string | null;
  createdAt?: string;
  updatedAt?: string;
}

export interface AdminBillingInvoice {
  id: string;
  userId: string;
  userName: string;
  amount: number;
  currency: string;
  status: string;
  date: string;
  plan: string;
}

export interface AdminBillingAddOn {
  id: string;
  code: string;
  name: string;
  description: string;
  price: number;
  currency: string;
  interval: string;
  durationDays: number;
  grantCredits: number;
  displayOrder: number;
  isRecurring: boolean;
  appliesToAllPlans: boolean;
  isStackable: boolean;
  quantityStep: number;
  maxQuantity: number | null;
  status: string;
  compatiblePlanCodes: string[];
  grantEntitlements: Record<string, unknown>;
  createdAt: string;
  updatedAt: string;
}

export interface AdminBillingCoupon {
  id: string;
  code: string;
  name: string;
  description: string;
  discountType: 'percentage' | 'fixed';
  discountValue: number;
  currency: string;
  startsAt: string | null;
  endsAt: string | null;
  usageLimitTotal: number | null;
  usageLimitPerUser: number | null;
  minimumSubtotal: number | null;
  isStackable: boolean;
  status: string;
  applicablePlanCodes: string[];
  applicableAddOnCodes: string[];
  redemptionCount: number;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface AdminBillingSubscription {
  id: string;
  userId: string;
  userName: string;
  planId: string;
  planName: string;
  status: 'pending' | 'trial' | 'active' | 'past_due' | 'suspended' | 'cancelled' | 'expired';
  nextRenewalAt: string | null;
  startedAt: string | null;
  changedAt: string | null;
  price: number;
  currency: string;
  interval: string;
  addOnCount: number;
}

export interface AdminBillingCouponRedemption {
  id: string;
  couponCode: string;
  userId: string;
  quoteId: string | null;
  checkoutSessionId: string | null;
  subscriptionId: string | null;
  discountAmount: number;
  currency: string;
  status: 'pending' | 'applied' | 'rejected' | 'reversed';
  redeemedAt: string;
}

export interface AdminReviewOpsSummary {
  backlog: number;
  overdue: number;
  slaRisk: number;
  statusDistribution: {
    pending: number;
    inProgress: number;
    completed: number;
  };
}

export interface AdminReviewQueueItem {
  id: string;
  taskId: string;
  learnerId: string;
  learnerName: string;
  assignedExpertId: string | null;
  status: 'pending' | 'in_progress' | 'completed';
  assignedAt: string;
  subtestCode: string;
  priority: 'high' | 'normal' | 'low';
}

export interface AdminReviewFailures {
  summary: {
    failedReviewCount: number;
    stuckReviewCount: number;
    failedJobCount: number;
  };
  failedReviews: Array<{
    id: string;
    attemptId: string;
    subtestCode: string;
    state: string;
    createdAt: string;
    completedAt: string | null;
  }>;
  stuckReviews: Array<{
    id: string;
    attemptId: string;
    subtestCode: string;
    state: string;
    createdAt: string;
    completedAt: string | null;
  }>;
  failedJobs: Array<{
    id: string;
    type: string;
    attemptId: string | null;
    state: string;
    reason: string;
    message: string;
    retryCount: number;
    createdAt: string;
  }>;
}

export interface AdminQualityPoint {
  label: string;
  value: number;
}

export interface AdminQualityAnalytics {
  aiHumanAgreement: { value: number; trend: number };
  appealsRate: { value: number; trend: number };
  avgReviewTime: { value: number; unit: string };
  contentPerformance: { publishedCount: number; activeContent: number };
  reviewSLA: { metPercent: number; avgTurnaround: string };
  featureAdoption: { activeUsers: number; adoptionRate: number };
  riskCases: { count: number; severity: string };
  filters: { timeRange: string; subtest: string; profession: string };
  freshness: { generatedAt: string; evaluationSampleCount: number; reviewSampleCount: number; windowDays: number };
  trendSeries: {
    agreement: AdminQualityPoint[];
    appeals: AdminQualityPoint[];
    reviewTime: AdminQualityPoint[];
    riskCases: AdminQualityPoint[];
  };
}

// ── Admin Permissions (RBAC) ────────────────────────────

export interface AdminPermissionGrant {
  permission: string;
  grantedBy: string;
  grantedAt: string;
}

export interface AdminPermissionsResponse {
  userId: string;
  permissions: AdminPermissionGrant[];
  allPermissions: string[];
}

export interface PermissionTemplate {
  id: string;
  name: string;
  description: string | null;
  permissions: string[];
  createdBy: string;
  createdAt: string;
}

// ── Content Publishing Workflow ─────────────────────────

export interface AdminPublishRequest {
  id: string;
  contentItemId: string;
  requestedBy: string;
  requestedByName: string;
  reviewedBy: string | null;
  reviewedByName: string | null;
  status: 'pending' | 'editor_review' | 'publisher_approval' | 'approved' | 'rejected';
  stage: 'editor_review' | 'publisher_approval';
  requestNote: string | null;
  reviewNote: string | null;
  requestedAt: string;
  reviewedAt: string | null;
  editorReviewedBy: string | null;
  editorReviewedByName: string | null;
  editorReviewedAt: string | null;
  editorNotes: string | null;
  publisherApprovedBy: string | null;
  publisherApprovedByName: string | null;
  publisherApprovedAt: string | null;
  publisherNotes: string | null;
  rejectedBy: string | null;
  rejectedByName: string | null;
  rejectedAt: string | null;
  rejectionReason: string | null;
  rejectionStage: string | null;
}

export interface AdminPublishRequestsResponse {
  items: AdminPublishRequest[];
  total: number;
  page: number;
  pageSize: number;
}

// ── Webhook Monitoring ──────────────────────────────────

export interface AdminWebhookEvent {
  id: string;
  gateway: string;
  eventType: string;
  gatewayEventId: string;
  processingStatus: 'received' | 'processing' | 'completed' | 'failed' | 'ignored';
  errorMessage: string | null;
  receivedAt: string;
  processedAt: string | null;
}

export interface AdminWebhookEventsResponse {
  items: AdminWebhookEvent[];
  total: number;
  page: number;
  pageSize: number;
}

export interface AdminWebhookSummary {
  total: number;
  recent24h: number;
  failed: number;
  failed24h: number;
  byStatus: { status: string; count: number }[];
  byGateway: { gateway: string; count: number }[];
  recentFailures: { id: string; eventType: string; errorMessage: string | null; receivedAt: string }[];
}

// ── Review Escalation ───────────────────────────────────

export interface AdminReviewEscalation {
  id: string;
  reviewRequestId: string;
  originalReviewerId: string;
  secondReviewerId: string | null;
  subtestCode: string;
  triggerCriterion: string;
  aiScore: number;
  humanScore: number;
  divergence: number;
  status: 'pending' | 'assigned' | 'resolved';
  resolutionNote: string | null;
  finalScore: number | null;
  createdAt: string;
  resolvedAt: string | null;
}

export interface AdminEscalationsResponse {
  items: AdminReviewEscalation[];
  total: number;
  page: number;
  pageSize: number;
}

// ── Score Guarantee Claims (Admin) ───────────────────

export interface AdminScoreGuaranteeClaim {
  id: string;
  userId: string;
  subscriptionId: string;
  baselineScore: number;
  guaranteedImprovement: number;
  actualScore: number | null;
  status: 'active' | 'claim_submitted' | 'claim_approved' | 'claim_rejected' | 'expired';
  proofDocumentUrl: string | null;
  claimNote: string | null;
  reviewNote: string | null;
  reviewedBy: string | null;
  activatedAt: string;
  expiresAt: string;
}

export interface AdminScoreGuaranteeClaimsResponse {
  items: AdminScoreGuaranteeClaim[];
  total: number;
  page: number;
  pageSize: number;
}

// ── Business Intelligence (Admin) ────────────────────

export interface AdminBIMetric {
  label: string;
  value: number;
  change: number;
  changeLabel: string;
}

export interface AdminBIDashboardData {
  revenue: AdminBIMetric;
  activeUsers: AdminBIMetric;
  retention: AdminBIMetric;
  churnRate: AdminBIMetric;
  avgScoreImprovement: AdminBIMetric;
  expertUtilization: AdminBIMetric;
  topSubtests: Array<{ subtest: string; completions: number; avgScore: number }>;
  revenueByMonth: Array<{ month: string; revenue: number; subscriptions: number }>;
  userGrowth: Array<{ month: string; newUsers: number; totalUsers: number }>;
}

export interface AdminSubscriptionHealthRevenuePlan {
  planId: string;
  planName: string;
  subscribers: number;
  monthlyRevenue: number;
}

export interface AdminSubscriptionHealthMonthlyTrend {
  month: string;
  newSubscriptions: number;
  cancellations: number;
}

export interface AdminSubscriptionHealthData {
  mrr: number;
  activeSubscriptions: number;
  churnRate: number;
  newSubscriptionsThisMonth: number;
  trialConversionRate: number;
  arpu: number;
  revenueByPlan: AdminSubscriptionHealthRevenuePlan[];
  monthlyTrend: AdminSubscriptionHealthMonthlyTrend[];
  generatedAt: string;
}

export interface AdminCohortAnalysisItem {
  cohortKey: string;
  cohortName: string;
  learnerCount: number;
  averageScore: number | null;
  evaluationCount: number;
  activeLastMonth: number;
}

export interface AdminCohortAnalysisData {
  groupBy: string;
  cohorts: AdminCohortAnalysisItem[];
  totalLearners: number;
  generatedAt: string;
}

export interface AdminContentEffectivenessItem {
  contentId: string;
  title: string;
  subtestCode: string;
  difficulty: string;
  totalAttempts: number;
  completionRate: number;
  averageScore: number | null;
  avgTimeSeconds: number | null;
  effectivenessScore: number | null;
}

export interface AdminContentEffectivenessData {
  subtestFilter: string | null;
  items: AdminContentEffectivenessItem[];
  generatedAt: string;
}

export interface AdminExpertEfficiencyItem {
  expertId: string;
  expertName: string;
  period: number;
  assignmentsReceived: number;
  reviewsCompleted: number;
  averageReviewTimeMinutes: number | null;
  reviewsPerDay: number;
  aiAlignmentScore: number | null;
  efficiency: 'high' | 'medium' | 'low' | 'no-data';
}

export interface AdminExpertEfficiencyData {
  period: number;
  experts: AdminExpertEfficiencyItem[];
  summary: {
    totalExperts: number;
    activeExperts: number;
    totalReviewsCompleted: number;
    averageReviewsPerExpertPerDay: number;
  };
  generatedAt: string;
}

export interface AdminBusinessIntelligenceData {
  generatedAt: string;
  subscriptionHealth: AdminSubscriptionHealthData;
  cohortAnalysis: AdminCohortAnalysisData;
  contentEffectiveness: AdminContentEffectivenessData;
  expertEfficiency: AdminExpertEfficiencyData;
}
