import {
  fetchAdminAIConfig,
  fetchAdminAuditLogDetail,
  fetchAdminAuditLogs,
  fetchAdminBillingAddOns,
  fetchAdminBillingCouponRedemptions,
  fetchAdminBillingCoupons,
  fetchAdminBillingInvoices,
  fetchAdminBillingPlans,
  fetchAdminBillingSubscriptions,
  fetchAdminContent,
  fetchAdminContentDetail,
  fetchAdminContentImpact,
  fetchAdminContentRevisions,
  fetchAdminCriteria,
  fetchAdminDashboard,
  fetchAdminFlags,
  fetchAdminQualityAnalytics,
  fetchAdminReviewFailures,
  fetchAdminReviewOpsQueue,
  fetchAdminReviewOpsSummary,
  fetchAdminTaxonomy,
  fetchAdminTaxonomyImpact,
  fetchAdminUserDetail,
  fetchAdminUsers,
} from './api';
import type {
  AdminAIConfig,
  AdminAuditLogDetail,
  AdminAuditLogRow,
  AdminBillingAddOn,
  AdminBillingInvoice,
  AdminBillingCoupon,
  AdminBillingCouponRedemption,
  AdminBillingPlan,
  AdminBillingSubscription,
  AdminContentDetail,
  AdminContentImpact,
  AdminContentRow,
  AdminCriterion,
  AdminDashboardData,
  AdminFlag,
  AdminQualityAnalytics,
  AdminRevisionRow,
  AdminReviewFailures,
  AdminReviewOpsSummary,
  AdminReviewQueueItem,
  AdminTaxonomyImpact,
  AdminTaxonomyNode,
  AdminUserDetail,
  AdminUserRow,
  AdminUsersPageData,
} from './types/admin';

type ApiRecord = Record<string, any>;

function asRecord(value: unknown): ApiRecord {
  return value && typeof value === 'object' ? (value as ApiRecord) : {};
}

function asArray(value: unknown): ApiRecord[] {
  return Array.isArray(value) ? value.map(asRecord) : [];
}

function toStringValue(value: unknown, fallback = ''): string {
  return typeof value === 'string' ? value : fallback;
}

function toNullableString(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function toNumberValue(value: unknown, fallback = 0): number {
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : fallback;
}

function toBooleanValue(value: unknown): boolean {
  return value === true;
}

function parseJsonObject(value: unknown): ApiRecord {
  if (typeof value !== 'string' || value.trim().length === 0) return {};
  try {
    return asRecord(JSON.parse(value));
  } catch {
    return {};
  }
}

function parseJsonArray(value: unknown): string[] {
  if (Array.isArray(value)) {
    return value.map((item) => toStringValue(item)).filter(Boolean);
  }

  if (typeof value !== 'string' || value.trim().length === 0) {
    return [];
  }

  try {
    const parsed = JSON.parse(value);
    return Array.isArray(parsed) ? parsed.map((item) => toStringValue(item)).filter(Boolean) : [];
  } catch {
    return [];
  }
}

function normalizeContentStatus(value: unknown): 'draft' | 'published' | 'archived' {
  const normalized = toStringValue(value, 'draft').toLowerCase();
  if (normalized === 'published') return 'published';
  if (normalized === 'archived') return 'archived';
  return 'draft';
}

function normalizeUserRole(value: unknown): 'learner' | 'expert' | 'admin' {
  const normalized = toStringValue(value, 'learner').toLowerCase();
  if (normalized === 'expert') return 'expert';
  if (normalized === 'admin') return 'admin';
  return 'learner';
}

function normalizeUserStatus(value: unknown): 'active' | 'suspended' | 'deleted' {
  const normalized = toStringValue(value, 'active').toLowerCase();
  if (normalized === 'suspended' || normalized === 'deleted') return normalized;
  return 'active';
}

function normalizeCriterionStatus(value: unknown): 'active' | 'archived' {
  return toStringValue(value, 'active').toLowerCase() === 'archived' ? 'archived' : 'active';
}

function normalizePriority(value: unknown): 'high' | 'normal' | 'low' {
  const normalized = toStringValue(value, 'normal').toLowerCase();
  if (normalized === 'high') return 'high';
  if (normalized === 'low') return 'low';
  return 'normal';
}

function normalizeReviewStatus(value: unknown): 'pending' | 'in_progress' | 'completed' {
  const normalized = toStringValue(value, 'pending').toLowerCase();
  if (normalized === 'completed') return 'completed';
  if (normalized === 'in_progress') return 'in_progress';
  return 'pending';
}

function normalizeSubscriptionStatus(value: unknown): 'pending' | 'trial' | 'active' | 'past_due' | 'suspended' | 'cancelled' | 'expired' {
  const normalized = toStringValue(value, 'pending').toLowerCase();
  if (normalized === 'trial') return 'trial';
  if (normalized === 'active') return 'active';
  if (normalized === 'past_due') return 'past_due';
  if (normalized === 'suspended') return 'suspended';
  if (normalized === 'cancelled') return 'cancelled';
  if (normalized === 'expired') return 'expired';
  return 'pending';
}

function normalizeRedemptionStatus(value: unknown): 'pending' | 'applied' | 'rejected' | 'reversed' {
  const normalized = toStringValue(value, 'pending').toLowerCase();
  if (normalized === 'applied') return 'applied';
  if (normalized === 'rejected') return 'rejected';
  if (normalized === 'reversed') return 'reversed';
  return 'pending';
}

export async function getAdminDashboardData(): Promise<AdminDashboardData> {
  const raw = asRecord(await fetchAdminDashboard());
  return {
    generatedAt: toStringValue(raw.generatedAt, new Date().toISOString()),
    freshness: {
      contentUpdatedAt: toNullableString(asRecord(raw.freshness).contentUpdatedAt),
      auditUpdatedAt: toNullableString(asRecord(raw.freshness).auditUpdatedAt),
      reviewUpdatedAt: toNullableString(asRecord(raw.freshness).reviewUpdatedAt),
      qualityWindow: toStringValue(asRecord(raw.freshness).qualityWindow, '30d'),
    },
    contentHealth: {
      published: toNumberValue(asRecord(raw.contentHealth).published),
      drafts: toNumberValue(asRecord(raw.contentHealth).drafts),
      archived: toNumberValue(asRecord(raw.contentHealth).archived),
      staleDrafts: toNumberValue(asRecord(raw.contentHealth).staleDrafts),
    },
    reviewOps: {
      backlog: toNumberValue(asRecord(raw.reviewOps).backlog),
      overdue: toNumberValue(asRecord(raw.reviewOps).overdue),
      failedReviews: toNumberValue(asRecord(raw.reviewOps).failedReviews),
      failedJobs: toNumberValue(asRecord(raw.reviewOps).failedJobs),
      inProgress: toNumberValue(asRecord(raw.reviewOps).inProgress),
    },
    billingRisk: {
      pendingInvoices: toNumberValue(asRecord(raw.billingRisk).pendingInvoices),
      failedInvoices: toNumberValue(asRecord(raw.billingRisk).failedInvoices),
      legacyPlans: toNumberValue(asRecord(raw.billingRisk).legacyPlans),
      activeSubscribers: toNumberValue(asRecord(raw.billingRisk).activeSubscribers),
    },
    flags: {
      total: toNumberValue(asRecord(raw.flags).total),
      enabled: toNumberValue(asRecord(raw.flags).enabled),
      liveExperiments: toNumberValue(asRecord(raw.flags).liveExperiments),
      recentChanges: toNumberValue(asRecord(raw.flags).recentChanges),
    },
    quality: {
      agreementRate: toNumberValue(asRecord(raw.quality).agreementRate),
      avgReviewHours: toNumberValue(asRecord(raw.quality).avgReviewHours),
      riskCases: toNumberValue(asRecord(raw.quality).riskCases),
      evaluationCount: toNumberValue(asRecord(raw.quality).evaluationCount),
    },
  };
}

export async function getAdminContentLibraryData(params?: Parameters<typeof fetchAdminContent>[0]) {
  const raw = asRecord(await fetchAdminContent(params));
  return {
    total: toNumberValue(raw.total),
    page: toNumberValue(raw.page, 1),
    pageSize: toNumberValue(raw.pageSize, 20),
    items: asArray(raw.items).map<AdminContentRow>((item) => ({
      id: toStringValue(item.id),
      title: toStringValue(item.title),
      type: toStringValue(item.type),
      profession: toStringValue(item.profession, 'All'),
      status: normalizeContentStatus(item.status),
      sourceType: toNullableString(item.sourceType) ?? undefined,
      qaStatus: toNullableString(item.qaStatus) ?? undefined,
      updatedAt: toStringValue(item.updatedAt, new Date().toISOString()),
      author: toStringValue(item.author, 'System'),
      revisionCount: toNumberValue(item.revisionCount),
    })),
  };
}

export async function getAdminContentDetailData(contentId: string): Promise<AdminContentDetail> {
  const raw = asRecord(await fetchAdminContentDetail(contentId));
  const detail = parseJsonObject(raw.detail);
  return {
    id: toStringValue(raw.id),
    title: toStringValue(raw.title),
    contentType: toStringValue(raw.type),
    subtestCode: toStringValue(raw.subtestCode),
    professionId: toStringValue(raw.professionId, 'all'),
    difficulty: toStringValue(raw.difficulty, 'medium'),
    estimatedDurationMinutes: toNumberValue(raw.estimatedDurationMinutes, 45),
    status: normalizeContentStatus(raw.status),
    sourceType: toNullableString(raw.sourceType) ?? undefined,
    qaStatus: toNullableString(raw.qaStatus) ?? undefined,
    description: toStringValue(detail.description ?? detail.prompt),
    caseNotes: toStringValue(detail.caseNotes ?? raw.caseNotes),
    modelAnswer: toStringValue(raw.modelAnswer),
    criteriaFocus: parseJsonArray(raw.criteriaFocus),
    revisions: asArray(raw.revisions).map((revision) => ({
      id: toStringValue(revision.id),
      revisionNumber: toNumberValue(revision.revisionNumber),
      state: toStringValue(revision.state),
      changeNote: toStringValue(revision.changeNote),
      createdBy: toStringValue(revision.createdBy),
      createdAt: toStringValue(revision.createdAt),
    })),
  };
}

export async function getAdminContentImpactData(contentId: string): Promise<AdminContentImpact> {
  const raw = asRecord(await fetchAdminContentImpact(contentId));
  return {
    contentId: toStringValue(raw.contentId),
    title: toStringValue(raw.title),
    status: toStringValue(raw.status),
    usage: {
      attemptCount: toNumberValue(asRecord(raw.usage).attemptCount),
      evaluationCount: toNumberValue(asRecord(raw.usage).evaluationCount),
      studyPlanReferences: toNumberValue(asRecord(raw.usage).studyPlanReferences),
      activeAttempts: toNumberValue(asRecord(raw.usage).activeAttempts),
    },
    safeToArchive: toBooleanValue(raw.safeToArchive),
    safeToDelete: toBooleanValue(raw.safeToDelete),
  };
}

export async function getAdminContentRevisionData(contentId: string): Promise<AdminRevisionRow[]> {
  const raw = await fetchAdminContentRevisions(contentId);
  return asArray(raw).map((revision) => ({
    id: toStringValue(revision.id),
    contentId: toStringValue(revision.contentId),
    date: toStringValue(revision.date),
    author: toStringValue(revision.author),
    state: toStringValue(revision.state),
    note: toStringValue(revision.note),
  }));
}

export async function getAdminTaxonomyData(params?: Parameters<typeof fetchAdminTaxonomy>[0]): Promise<AdminTaxonomyNode[]> {
  const raw = await fetchAdminTaxonomy(params);
  return asArray(raw).map((item) => ({
    id: toStringValue(item.id),
    label: toStringValue(item.label),
    slug: toStringValue(item.slug),
    type: toStringValue(item.type, 'profession') === 'category' ? 'category' : 'profession',
    status: normalizeCriterionStatus(item.status),
    contentCount: toNumberValue(item.contentCount),
  }));
}

export async function getAdminTaxonomyImpactData(professionId: string): Promise<AdminTaxonomyImpact> {
  const raw = asRecord(await fetchAdminTaxonomyImpact(professionId));
  return {
    professionId: toStringValue(raw.professionId),
    label: toStringValue(raw.label),
    status: toStringValue(raw.status),
    usage: {
      contentCount: toNumberValue(asRecord(raw.usage).contentCount),
      learnerCount: toNumberValue(asRecord(raw.usage).learnerCount),
      goalCount: toNumberValue(asRecord(raw.usage).goalCount),
    },
    safeToArchive: toBooleanValue(raw.safeToArchive),
  };
}

export async function getAdminCriteriaData(params?: Parameters<typeof fetchAdminCriteria>[0]): Promise<AdminCriterion[]> {
  const raw = await fetchAdminCriteria(params);
  return asArray(raw).map((item) => ({
    id: toStringValue(item.id),
    name: toStringValue(item.name),
    type: toStringValue(item.type),
    weight: toNumberValue(item.weight),
    status: normalizeCriterionStatus(item.status),
    description: toStringValue(item.description),
  }));
}

export async function getAdminAIConfigData(params?: Parameters<typeof fetchAdminAIConfig>[0]): Promise<AdminAIConfig[]> {
  const raw = await fetchAdminAIConfig(params);
  return asArray(raw).map((item) => ({
    id: toStringValue(item.id),
    model: toStringValue(item.model),
    provider: toStringValue(item.provider),
    taskType: toStringValue(item.taskType),
    status: (toStringValue(item.status, 'testing') as AdminAIConfig['status']),
    accuracy: toNumberValue(item.accuracy),
    confidenceThreshold: toNumberValue(item.confidenceThreshold),
    routingRule: toStringValue(item.routingRule),
    experimentFlag: toStringValue(item.experimentFlag),
    promptLabel: toStringValue(item.promptLabel),
  }));
}

export async function getAdminFlagData(params?: Parameters<typeof fetchAdminFlags>[0]): Promise<AdminFlag[]> {
  const raw = await fetchAdminFlags(params);
  return asArray(raw).map((item) => ({
    id: toStringValue(item.id),
    name: toStringValue(item.name),
    key: toStringValue(item.key),
    enabled: toBooleanValue(item.enabled),
    type: toStringValue(item.type),
    rolloutPercentage: toNumberValue(item.rolloutPercentage),
    description: toStringValue(item.description),
    owner: toStringValue(item.owner),
  }));
}

export async function getAdminAuditLogPageData(params?: Parameters<typeof fetchAdminAuditLogs>[0]) {
  const raw = asRecord(await fetchAdminAuditLogs(params));
  return {
    total: toNumberValue(raw.total),
    page: toNumberValue(raw.page, 1),
    pageSize: toNumberValue(raw.pageSize, 20),
    items: asArray(raw.items).map<AdminAuditLogRow>((item) => ({
      id: toStringValue(item.id),
      timestamp: toStringValue(item.timestamp),
      actor: toStringValue(item.actor),
      action: toStringValue(item.action),
      resource: toStringValue(item.resource),
      details: toStringValue(item.details),
    })),
  };
}

export async function getAdminAuditLogDetailData(eventId: string): Promise<AdminAuditLogDetail> {
  const raw = asRecord(await fetchAdminAuditLogDetail(eventId));
  return {
    id: toStringValue(raw.id),
    timestamp: toStringValue(raw.timestamp),
    actorId: toStringValue(raw.actorId),
    actorName: toStringValue(raw.actorName),
    action: toStringValue(raw.action),
    resourceType: toStringValue(raw.resourceType),
    resourceId: toStringValue(raw.resourceId),
    details: toStringValue(raw.details),
  };
}

export async function getAdminUsersPageData(params?: Parameters<typeof fetchAdminUsers>[0]): Promise<AdminUsersPageData> {
  const raw = asRecord(await fetchAdminUsers(params));
  return {
    total: toNumberValue(raw.total),
    page: toNumberValue(raw.page, 1),
    pageSize: toNumberValue(raw.pageSize, 20),
    items: asArray(raw.items).map<AdminUserRow>((item) => ({
      id: toStringValue(item.id),
      name: toStringValue(item.name),
      email: toStringValue(item.email),
      role: normalizeUserRole(item.role),
      status: normalizeUserStatus(item.status),
      lastLogin: toNullableString(item.lastLogin),
    })),
  };
}

export async function getAdminUserDetailData(userId: string): Promise<AdminUserDetail> {
  const raw = asRecord(await fetchAdminUserDetail(userId));
  return {
    id: toStringValue(raw.id),
    name: toStringValue(raw.name),
    email: toStringValue(raw.email),
    role: normalizeUserRole(raw.role),
    status: normalizeUserStatus(raw.status),
    lastLogin: toNullableString(raw.lastLogin),
    createdAt: toNullableString(raw.createdAt),
    authAccountId: toNullableString(raw.authAccountId),
    profession: toNullableString(raw.profession),
    specialties: parseJsonArray(raw.specialties ?? asRecord(raw).specialties),
    tasksCompleted: raw.tasksCompleted != null ? toNumberValue(raw.tasksCompleted) : undefined,
    tasksGraded: raw.tasksGraded != null ? toNumberValue(raw.tasksGraded) : undefined,
    creditBalance: raw.creditBalance != null ? toNumberValue(raw.creditBalance) : undefined,
    availableActions: {
      canSuspend: toBooleanValue(asRecord(raw.availableActions).canSuspend),
      canDelete: toBooleanValue(asRecord(raw.availableActions).canDelete),
      canRestore: toBooleanValue(asRecord(raw.availableActions).canRestore),
      canAdjustCredits: toBooleanValue(asRecord(raw.availableActions).canAdjustCredits),
      canTriggerPasswordReset: toBooleanValue(asRecord(raw.availableActions).canTriggerPasswordReset),
    },
  };
}

export async function getAdminBillingPlanData(params?: Parameters<typeof fetchAdminBillingPlans>[0]): Promise<AdminBillingPlan[]> {
  const raw = await fetchAdminBillingPlans(params);
  return asArray(raw).map((item) => ({
    id: toStringValue(item.id),
    code: toNullableString(item.code) ?? toStringValue(item.id),
    name: toStringValue(item.name),
    description: toNullableString(item.description) ?? '',
    price: toNumberValue(item.price),
    currency: toStringValue(item.currency, 'AUD'),
    interval: toStringValue(item.interval, 'month'),
    durationMonths: toNumberValue(item.durationMonths, 1),
    includedCredits: toNumberValue(item.includedCredits),
    displayOrder: toNumberValue(item.displayOrder),
    isVisible: item.isVisible !== false,
    isRenewable: item.isRenewable !== false,
    trialDays: toNumberValue(item.trialDays),
    activeSubscribers: toNumberValue(item.activeSubscribers),
    status: toStringValue(item.status),
    includedSubtests: parseJsonArray(item.includedSubtests),
    entitlements: asRecord(item.entitlements),
    archivedAt: toNullableString(item.archivedAt),
    createdAt: toNullableString(item.createdAt) ?? undefined,
    updatedAt: toNullableString(item.updatedAt) ?? undefined,
  }));
}

export async function getAdminBillingAddOnData(params?: Parameters<typeof fetchAdminBillingAddOns>[0]): Promise<AdminBillingAddOn[]> {
  const raw = await fetchAdminBillingAddOns(params);
  return asArray(raw).map((item) => ({
    id: toStringValue(item.id),
    code: toStringValue(item.code),
    name: toStringValue(item.name),
    description: toStringValue(item.description),
    price: toNumberValue(item.price),
    currency: toStringValue(item.currency, 'AUD'),
    interval: toStringValue(item.interval, 'one_time'),
    durationDays: toNumberValue(item.durationDays),
    grantCredits: toNumberValue(item.grantCredits),
    displayOrder: toNumberValue(item.displayOrder),
    isRecurring: toBooleanValue(item.isRecurring),
    appliesToAllPlans: toBooleanValue(item.appliesToAllPlans),
    isStackable: toBooleanValue(item.isStackable),
    quantityStep: toNumberValue(item.quantityStep, 1),
    maxQuantity: item.maxQuantity == null ? null : toNumberValue(item.maxQuantity),
    status: toStringValue(item.status),
    compatiblePlanCodes: parseJsonArray(item.compatiblePlanCodes),
    grantEntitlements: asRecord(item.grantEntitlements),
    createdAt: toStringValue(item.createdAt),
    updatedAt: toStringValue(item.updatedAt),
  }));
}

export async function getAdminBillingCouponData(params?: Parameters<typeof fetchAdminBillingCoupons>[0]): Promise<AdminBillingCoupon[]> {
  const raw = await fetchAdminBillingCoupons(params);
  return asArray(raw).map((item) => ({
    id: toStringValue(item.id),
    code: toStringValue(item.code),
    name: toStringValue(item.name),
    description: toStringValue(item.description),
    discountType: toStringValue(item.discountType, 'percentage') as 'percentage' | 'fixed',
    discountValue: toNumberValue(item.discountValue),
    currency: toStringValue(item.currency, 'AUD'),
    startsAt: toNullableString(item.startsAt),
    endsAt: toNullableString(item.endsAt),
    usageLimitTotal: item.usageLimitTotal == null ? null : toNumberValue(item.usageLimitTotal),
    usageLimitPerUser: item.usageLimitPerUser == null ? null : toNumberValue(item.usageLimitPerUser),
    minimumSubtotal: item.minimumSubtotal == null ? null : toNumberValue(item.minimumSubtotal),
    isStackable: toBooleanValue(item.isStackable),
    status: toStringValue(item.status),
    applicablePlanCodes: parseJsonArray(item.applicablePlanCodes),
    applicableAddOnCodes: parseJsonArray(item.applicableAddOnCodes),
    redemptionCount: toNumberValue(item.redemptionCount),
    notes: toNullableString(item.notes),
    createdAt: toStringValue(item.createdAt),
    updatedAt: toStringValue(item.updatedAt),
  }));
}

export async function getAdminBillingInvoiceData(params?: Parameters<typeof fetchAdminBillingInvoices>[0]) {
  const raw = asRecord(await fetchAdminBillingInvoices(params));
  return {
    total: toNumberValue(raw.total),
    page: toNumberValue(raw.page, 1),
    pageSize: toNumberValue(raw.pageSize, 20),
    items: asArray(raw.items).map<AdminBillingInvoice>((item) => ({
      id: toStringValue(item.id),
      userId: toStringValue(item.userId),
      userName: toStringValue(item.userName, toStringValue(item.userId)),
      amount: toNumberValue(item.amount),
      currency: toStringValue(item.currency, 'AUD'),
      status: toStringValue(item.status),
      date: toStringValue(item.date),
      plan: toStringValue(item.plan),
    })),
  };
}

export async function getAdminBillingSubscriptionData(params?: Parameters<typeof fetchAdminBillingSubscriptions>[0]) {
  const raw = asRecord(await fetchAdminBillingSubscriptions(params));
  return {
    total: toNumberValue(raw.total),
    page: toNumberValue(raw.page, 1),
    pageSize: toNumberValue(raw.pageSize, 20),
    items: asArray(raw.items).map<AdminBillingSubscription>((item) => ({
      id: toStringValue(item.id),
      userId: toStringValue(item.userId),
      userName: toStringValue(item.userName, toStringValue(item.userId)),
      planId: toStringValue(item.planId),
      planName: toStringValue(item.planName, toStringValue(item.planId)),
      status: normalizeSubscriptionStatus(item.status),
      nextRenewalAt: toNullableString(item.nextRenewalAt),
      startedAt: toNullableString(item.startedAt),
      changedAt: toNullableString(item.changedAt),
      price: toNumberValue(item.price),
      currency: toStringValue(item.currency, 'AUD'),
      interval: toStringValue(item.interval, 'month'),
      addOnCount: toNumberValue(item.addOnCount),
    })),
  };
}

export async function getAdminBillingCouponRedemptionData(params?: Parameters<typeof fetchAdminBillingCouponRedemptions>[0]) {
  const raw = asRecord(await fetchAdminBillingCouponRedemptions(params));
  return {
    total: toNumberValue(raw.total),
    page: toNumberValue(raw.page, 1),
    pageSize: toNumberValue(raw.pageSize, 20),
    items: asArray(raw.items).map<AdminBillingCouponRedemption>((item) => ({
      id: toStringValue(item.id),
      couponCode: toStringValue(item.couponCode),
      userId: toStringValue(item.userId),
      quoteId: toNullableString(item.quoteId),
      checkoutSessionId: toNullableString(item.checkoutSessionId),
      subscriptionId: toNullableString(item.subscriptionId),
      discountAmount: toNumberValue(item.discountAmount),
      currency: toStringValue(item.currency, 'AUD'),
      status: normalizeRedemptionStatus(item.status),
      redeemedAt: toStringValue(item.redeemedAt),
    })),
  };
}

export async function getAdminReviewOpsSummaryData(): Promise<AdminReviewOpsSummary> {
  const raw = asRecord(await fetchAdminReviewOpsSummary());
  return {
    backlog: toNumberValue(raw.backlog),
    overdue: toNumberValue(raw.overdue),
    slaRisk: toNumberValue(raw.slaRisk),
    statusDistribution: {
      pending: toNumberValue(asRecord(raw.statusDistribution).pending),
      inProgress: toNumberValue(asRecord(raw.statusDistribution).inProgress),
      completed: toNumberValue(asRecord(raw.statusDistribution).completed),
    },
  };
}

export async function getAdminReviewQueueData(params?: Parameters<typeof fetchAdminReviewOpsQueue>[0]): Promise<AdminReviewQueueItem[]> {
  const raw = await fetchAdminReviewOpsQueue(params);
  return asArray(raw).map((item) => ({
    id: toStringValue(item.id),
    taskId: toStringValue(item.taskId),
    learnerId: toStringValue(item.learnerId),
    learnerName: toStringValue(item.learnerName, toStringValue(item.learnerId)),
    assignedExpertId: toNullableString(item.assignedExpertId),
    status: normalizeReviewStatus(item.status),
    assignedAt: toStringValue(item.assignedAt),
    subtestCode: toStringValue(item.subtestCode),
    priority: normalizePriority(item.priority),
  }));
}

export async function getAdminReviewFailureData(): Promise<AdminReviewFailures> {
  const raw = asRecord(await fetchAdminReviewFailures());
  return {
    summary: {
      failedReviewCount: toNumberValue(asRecord(raw.summary).failedReviewCount),
      stuckReviewCount: toNumberValue(asRecord(raw.summary).stuckReviewCount),
      failedJobCount: toNumberValue(asRecord(raw.summary).failedJobCount),
    },
    failedReviews: asArray(raw.failedReviews).map((item) => ({
      id: toStringValue(item.id),
      attemptId: toStringValue(item.attemptId),
      subtestCode: toStringValue(item.subtestCode),
      state: toStringValue(item.state),
      createdAt: toStringValue(item.createdAt),
      completedAt: toNullableString(item.completedAt),
    })),
    stuckReviews: asArray(raw.stuckReviews).map((item) => ({
      id: toStringValue(item.id),
      attemptId: toStringValue(item.attemptId),
      subtestCode: toStringValue(item.subtestCode),
      state: toStringValue(item.state),
      createdAt: toStringValue(item.createdAt),
      completedAt: toNullableString(item.completedAt),
    })),
    failedJobs: asArray(raw.failedJobs).map((item) => ({
      id: toStringValue(item.id),
      type: toStringValue(item.type),
      attemptId: toNullableString(item.attemptId),
      state: toStringValue(item.state),
      reason: toStringValue(item.reason),
      message: toStringValue(item.message),
      retryCount: toNumberValue(item.retryCount),
      createdAt: toStringValue(item.createdAt),
    })),
  };
}

export async function getAdminQualityAnalyticsData(params?: Parameters<typeof fetchAdminQualityAnalytics>[0]): Promise<AdminQualityAnalytics> {
  const raw = asRecord(await fetchAdminQualityAnalytics(params));
  return {
    aiHumanAgreement: {
      value: toNumberValue(asRecord(raw.aiHumanAgreement).value),
      trend: toNumberValue(asRecord(raw.aiHumanAgreement).trend),
    },
    appealsRate: {
      value: toNumberValue(asRecord(raw.appealsRate).value),
      trend: toNumberValue(asRecord(raw.appealsRate).trend),
    },
    avgReviewTime: {
      value: toNumberValue(asRecord(raw.avgReviewTime).value),
      unit: toStringValue(asRecord(raw.avgReviewTime).unit, 'hours'),
    },
    contentPerformance: {
      publishedCount: toNumberValue(asRecord(raw.contentPerformance).publishedCount),
      activeContent: toNumberValue(asRecord(raw.contentPerformance).activeContent),
    },
    reviewSLA: {
      metPercent: toNumberValue(asRecord(raw.reviewSLA).metPercent),
      avgTurnaround: toStringValue(asRecord(raw.reviewSLA).avgTurnaround),
    },
    featureAdoption: {
      activeUsers: toNumberValue(asRecord(raw.featureAdoption).activeUsers),
      adoptionRate: toNumberValue(asRecord(raw.featureAdoption).adoptionRate),
    },
    riskCases: {
      count: toNumberValue(asRecord(raw.riskCases).count),
      severity: toStringValue(asRecord(raw.riskCases).severity),
    },
    filters: {
      timeRange: toStringValue(asRecord(raw.filters).timeRange, '30d'),
      subtest: toStringValue(asRecord(raw.filters).subtest, 'all'),
      profession: toStringValue(asRecord(raw.filters).profession, 'all'),
    },
    freshness: {
      generatedAt: toStringValue(asRecord(raw.freshness).generatedAt, new Date().toISOString()),
      evaluationSampleCount: toNumberValue(asRecord(raw.freshness).evaluationSampleCount),
      reviewSampleCount: toNumberValue(asRecord(raw.freshness).reviewSampleCount),
      windowDays: toNumberValue(asRecord(raw.freshness).windowDays, 30),
    },
    trendSeries: {
      agreement: asArray(asRecord(raw.trendSeries).agreement).map((point) => ({
        label: toStringValue(point.label),
        value: toNumberValue(point.value),
      })),
      appeals: asArray(asRecord(raw.trendSeries).appeals).map((point) => ({
        label: toStringValue(point.label),
        value: toNumberValue(point.value),
      })),
      reviewTime: asArray(asRecord(raw.trendSeries).reviewTime).map((point) => ({
        label: toStringValue(point.label),
        value: toNumberValue(point.value),
      })),
      riskCases: asArray(asRecord(raw.trendSeries).riskCases).map((point) => ({
        label: toStringValue(point.label),
        value: toNumberValue(point.value),
      })),
    },
  };
}
