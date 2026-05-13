import {
  apiClient,
  fetchAdminAIConfig,
  fetchAdminCohortAnalysis,
  fetchAdminAuditLogDetail,
  fetchAdminAuditLogs,
  fetchAdminBillingAddOns,
  fetchAdminBillingAddOnVersions,
  fetchAdminBillingCouponRedemptions,
  fetchAdminBillingCoupons,
  fetchAdminBillingCouponVersions,
  fetchAdminBillingEntitlementDiagnostics,
  fetchAdminBillingInvoiceEvidence,
  fetchAdminBillingInvoices,
  fetchAdminBillingPaymentTransactions,
  fetchAdminBillingPlans,
  fetchAdminBillingPlanVersions,
  fetchAdminBillingProviderLifecycleSignals,
  fetchAdminBillingSubscriptions,
  fetchAdminContent,
  fetchAdminContentEffectiveness,
  fetchAdminContentDetail,
  fetchAdminContentImpact,
  fetchAdminContentRevisions,
  fetchAdminCriteria,
  fetchAdminDashboard,
  fetchAdminFlags,
  fetchAdminExpertEfficiency,
  fetchAdminQualityAnalytics,
  fetchAdminReviewFailures,
  fetchAdminReviewOpsQueue,
  fetchAdminReviewOpsSummary,
  fetchAdminTaxonomy,
  fetchAdminTaxonomyImpact,
  fetchAdminSubscriptionHealth,
  fetchAdminUserDetail,
  fetchAdminUsers,
  fetchPublishRequests,
  fetchReviewEscalations,
  fetchWebhookEvents,
  fetchWebhookSummary,
  fetchAdminScoreGuaranteeClaims,
} from './api';
import type {
  AdminAIConfig,
  AdminAuditLogDetail,
  AdminAuditLogRow,
  AdminBillingAddOn,
  AdminBillingCatalogSubject,
  AdminBillingCatalogVersion,
  AdminBillingCatalogVersionHistory,
  AdminBillingEntitlementDiagnosticCheck,
  AdminBillingEntitlementDiagnostics,
  AdminBillingEntitlementDiagnosticExample,
  AdminBillingInvoice,
  AdminBillingInvoiceEvidence,
  AdminBillingPaymentTransaction,
  AdminBillingPaymentTransactionResponse,
  AdminBillingProviderLifecycleSignal,
  AdminBillingProviderLifecycleSignalsResponse,
  AdminBillingCoupon,
  AdminBillingCouponRedemption,
  AdminBillingPlan,
  AdminBillingSubscription,
  AdminContentDetail,
  AdminContentImpact,
  AdminContentRow,
  AdminCriterion,
  AdminDashboardData,
  AdminBusinessIntelligenceData,
  AdminCohortAnalysisData,
  AdminContentEffectivenessData,
  AdminExpertEfficiencyData,
  AdminEscalationsResponse,
  AdminFlag,
  AdminPublishRequest,
  AdminPublishRequestsResponse,
  AdminQualityAnalytics,
  AdminRevisionRow,
  AdminReviewFailures,
  AdminReviewOpsSummary,
  AdminReviewQueueItem,
  AdminSubscriptionHealthData,
  AdminTaxonomyImpact,
  AdminTaxonomyNode,
  AdminUserDetail,
  AdminUserRow,
  AdminUsersPageData,
  AdminWebhookEventsResponse,
  AdminWebhookSummary,
  AdminScoreGuaranteeClaim,
  AdminScoreGuaranteeClaimsResponse,
} from './types/admin';

type ApiRecord = Record<string, any>;

function asRecord(value: unknown): ApiRecord {
  return value && typeof value === 'object' ? (value as ApiRecord) : {};
}

function asArray(value: unknown): ApiRecord[] {
  return Array.isArray(value) ? value.map(asRecord) : [];
}

function asStringRecord(value: unknown): Record<string, string> {
  const record = asRecord(value);
  return Object.fromEntries(
    Object.entries(record)
      .map(([key, entry]) => [key, toStringValue(entry)] as const)
      .filter(([, entry]) => entry.length > 0),
  );
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

function toNullableNumber(value: unknown): number | null {
  if (value == null) return null;
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : null;
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

function normalizeUserRole(value: unknown): 'learner' | 'expert' | 'admin' | 'sponsor' {
  const normalized = toStringValue(value, 'learner').toLowerCase();
  if (normalized === 'expert') return 'expert';
  if (normalized === 'admin') return 'admin';
  if (normalized === 'sponsor') return 'sponsor';
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
    providerName: toStringValue(item.providerName),
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
  const summaryRaw = asRecord(raw.summary);
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
      createdAt: toNullableString(item.createdAt),
      profession: toNullableString(item.profession),
      mfaEnabled: toBooleanValue(item.mfaEnabled),
      lockedOut: toBooleanValue(item.lockedOut),
    })),
    summary: raw.summary != null ? {
      total: toNumberValue(summaryRaw.total),
      active: toNumberValue(summaryRaw.active),
      suspended: toNumberValue(summaryRaw.suspended),
      deleted: toNumberValue(summaryRaw.deleted),
      mfaEnabled: toNumberValue(summaryRaw.mfaEnabled),
      lockedOut: toNumberValue(summaryRaw.lockedOut),
    } : undefined,
  };
}

export async function getAdminUserDetailData(userId: string): Promise<AdminUserDetail> {
  const raw = asRecord(await fetchAdminUserDetail(userId));
  const securityRaw = asRecord(raw.security);
  const subscriptionRaw = asRecord(raw.subscription);
  const security = raw.security
    ? {
        mfaEnabled: toBooleanValue(securityRaw.mfaEnabled),
        failedSignInCount: toNumberValue(securityRaw.failedSignInCount),
        lockoutUntil: toNullableString(securityRaw.lockoutUntil),
        lockedOut: toBooleanValue(securityRaw.lockedOut),
        emailVerifiedAt: toNullableString(securityRaw.emailVerifiedAt),
        activeSessionCount: toNumberValue(securityRaw.activeSessionCount),
        lastSessionAt: toNullableString(securityRaw.lastSessionAt),
        lastSessionIp: toNullableString(securityRaw.lastSessionIp),
        lastSessionDevice: toNullableString(securityRaw.lastSessionDevice),
      }
    : null;
  const subscription = raw.subscription
    ? {
        id: toStringValue(subscriptionRaw.id),
        planId: toStringValue(subscriptionRaw.planId),
        planName: toStringValue(subscriptionRaw.planName),
        planCode: toNullableString(subscriptionRaw.planCode),
        status: toStringValue(subscriptionRaw.status),
        startedAt: toNullableString(subscriptionRaw.startedAt),
        nextRenewalAt: toNullableString(subscriptionRaw.nextRenewalAt),
        changedAt: toNullableString(subscriptionRaw.changedAt),
        priceAmount: toNumberValue(subscriptionRaw.priceAmount),
        currency: toStringValue(subscriptionRaw.currency, 'AUD'),
        interval: toStringValue(subscriptionRaw.interval, 'monthly'),
      }
    : null;
  const recentActivity = asArray(raw.recentActivity).map((event) => {
    const ev = asRecord(event);
    return {
      id: toStringValue(ev.id),
      occurredAt: toStringValue(ev.occurredAt),
      action: toStringValue(ev.action),
      actorName: toStringValue(ev.actorName),
      resourceType: toNullableString(ev.resourceType),
      resourceId: toNullableString(ev.resourceId),
      details: toNullableString(ev.details),
    };
  });
  const actionsRaw = asRecord(raw.availableActions);
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
    security,
    subscription,
    recentActivity,
    availableActions: {
      canSuspend: toBooleanValue(actionsRaw.canSuspend),
      canDelete: toBooleanValue(actionsRaw.canDelete),
      canRestore: toBooleanValue(actionsRaw.canRestore),
      canAdjustCredits: toBooleanValue(actionsRaw.canAdjustCredits),
      canTriggerPasswordReset: toBooleanValue(actionsRaw.canTriggerPasswordReset),
      canForceSignOut: toBooleanValue(actionsRaw.canForceSignOut),
      canUnlock: toBooleanValue(actionsRaw.canUnlock),
      canResendInvite: toBooleanValue(actionsRaw.canResendInvite),
    },
  };
}

export async function getAdminBillingPlanData(params?: Parameters<typeof fetchAdminBillingPlans>[0]): Promise<AdminBillingPlan[]> {
  const raw = await fetchAdminBillingPlans(params);
  return asArray(raw).map((item) => ({
    id: toStringValue(item.id),
    code: toNullableString(item.code) ?? toStringValue(item.id),
    activeVersionId: toNullableString(item.activeVersionId),
    activeVersionNumber: toNullableNumber(item.activeVersionNumber),
    latestVersionId: toNullableString(item.latestVersionId),
    latestVersionNumber: toNullableNumber(item.latestVersionNumber),
    versionCount: toNumberValue(item.versionCount),
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
    activeVersionId: toNullableString(item.activeVersionId),
    activeVersionNumber: toNullableNumber(item.activeVersionNumber),
    latestVersionId: toNullableString(item.latestVersionId),
    latestVersionNumber: toNullableNumber(item.latestVersionNumber),
    versionCount: toNumberValue(item.versionCount),
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
    activeVersionId: toNullableString(item.activeVersionId),
    activeVersionNumber: toNullableNumber(item.activeVersionNumber),
    latestVersionId: toNullableString(item.latestVersionId),
    latestVersionNumber: toNullableNumber(item.latestVersionNumber),
    versionCount: toNumberValue(item.versionCount),
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

function mapBillingCatalogSubject(raw: ApiRecord): AdminBillingCatalogSubject {
  const kind = toStringValue(raw.kind, 'plan');
  return {
    kind: kind === 'add_on' || kind === 'coupon' ? kind : 'plan',
    id: toStringValue(raw.id),
    code: toStringValue(raw.code),
    name: toStringValue(raw.name),
    activeVersionId: toNullableString(raw.activeVersionId),
    activeVersionNumber: toNullableNumber(raw.activeVersionNumber),
    latestVersionId: toNullableString(raw.latestVersionId),
    latestVersionNumber: toNullableNumber(raw.latestVersionNumber),
    versionCount: toNumberValue(raw.versionCount),
  };
}

function mapBillingCatalogVersion(raw: ApiRecord): AdminBillingCatalogVersion {
  return {
    id: toStringValue(raw.id),
    parentId: toStringValue(raw.parentId),
    versionNumber: toNumberValue(raw.versionNumber),
    code: toStringValue(raw.code),
    name: toStringValue(raw.name),
    description: toStringValue(raw.description),
    status: toStringValue(raw.status),
    isActive: toBooleanValue(raw.isActive),
    isLatest: toBooleanValue(raw.isLatest),
    createdByAdminId: toNullableString(raw.createdByAdminId),
    createdByAdminName: toNullableString(raw.createdByAdminName),
    createdAt: toStringValue(raw.createdAt),
    summary: asRecord(raw.summary),
  };
}

function mapBillingCatalogVersionHistory(raw: unknown): AdminBillingCatalogVersionHistory {
  const record = asRecord(raw);
  return {
    subject: mapBillingCatalogSubject(asRecord(record.subject)),
    items: asArray(record.items).map(mapBillingCatalogVersion),
  };
}

export async function getAdminBillingPlanVersionHistoryData(planId: string): Promise<AdminBillingCatalogVersionHistory> {
  return mapBillingCatalogVersionHistory(await fetchAdminBillingPlanVersions(planId));
}

export async function getAdminBillingAddOnVersionHistoryData(addOnId: string): Promise<AdminBillingCatalogVersionHistory> {
  return mapBillingCatalogVersionHistory(await fetchAdminBillingAddOnVersions(addOnId));
}

export async function getAdminBillingCouponVersionHistoryData(couponId: string): Promise<AdminBillingCatalogVersionHistory> {
  return mapBillingCatalogVersionHistory(await fetchAdminBillingCouponVersions(couponId));
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

export async function getAdminBillingInvoiceEvidenceData(invoiceId: string): Promise<AdminBillingInvoiceEvidence> {
  const raw = asRecord(await fetchAdminBillingInvoiceEvidence(invoiceId));
  const invoice = asRecord(raw.invoice);
  const quote = raw.quote == null ? null : asRecord(raw.quote);

  return {
    invoice: {
      id: toStringValue(invoice.id),
      userId: toStringValue(invoice.userId),
      userName: toStringValue(invoice.userName, toStringValue(invoice.userId)),
      amount: toNumberValue(invoice.amount),
      currency: toStringValue(invoice.currency, 'AUD'),
      status: toStringValue(invoice.status).toLowerCase(),
      description: toStringValue(invoice.description),
      issuedAt: toStringValue(invoice.issuedAt),
      planVersionId: toNullableString(invoice.planVersionId),
      addOnVersionIds: asStringRecord(invoice.addOnVersionIds),
      couponVersionId: toNullableString(invoice.couponVersionId),
      quoteId: toNullableString(invoice.quoteId),
      checkoutSessionId: toNullableString(invoice.checkoutSessionId),
    },
    quote: quote
      ? {
        id: toStringValue(quote.id),
        status: toStringValue(quote.status).toLowerCase(),
        currency: toStringValue(quote.currency, 'AUD'),
        subtotalAmount: toNumberValue(quote.subtotalAmount),
        discountAmount: toNumberValue(quote.discountAmount),
        totalAmount: toNumberValue(quote.totalAmount),
        planCode: toNullableString(quote.planCode),
        couponCode: toNullableString(quote.couponCode),
        addOnCodes: parseJsonArray(quote.addOnCodes),
        items: asArray(quote.items).map((item) => ({
          kind: toStringValue(item.kind),
          code: toStringValue(item.code),
          name: toStringValue(item.name),
          amount: toNumberValue(item.amount),
          currency: toStringValue(item.currency, 'AUD'),
          quantity: toNumberValue(item.quantity, 1),
          description: toNullableString(item.description),
        })),
        createdAt: toStringValue(quote.createdAt),
        expiresAt: toStringValue(quote.expiresAt),
        checkoutSessionId: toNullableString(quote.checkoutSessionId),
        planVersionId: toNullableString(quote.planVersionId),
        addOnVersionIds: asStringRecord(quote.addOnVersionIds),
        couponVersionId: toNullableString(quote.couponVersionId),
        summary: toStringValue(quote.summary),
      }
      : null,
    payments: asArray(raw.payments).map((payment) => ({
      id: toStringValue(payment.id),
      gateway: toStringValue(payment.gateway),
      gatewayTransactionId: toStringValue(payment.gatewayTransactionId),
      transactionType: toStringValue(payment.transactionType),
      status: toStringValue(payment.status).toLowerCase(),
      amount: toNumberValue(payment.amount),
      currency: toStringValue(payment.currency, 'AUD').trim().toUpperCase() || 'AUD',
      productType: toStringValue(payment.productType),
      productId: toStringValue(payment.productId),
      quoteId: toNullableString(payment.quoteId),
      planVersionId: toNullableString(payment.planVersionId),
      addOnVersionIds: asStringRecord(payment.addOnVersionIds),
      couponVersionId: toNullableString(payment.couponVersionId),
      createdAt: toStringValue(payment.createdAt),
      updatedAt: toStringValue(payment.updatedAt),
    })),
    redemptions: asArray(raw.redemptions).map((redemption) => ({
      id: toStringValue(redemption.id),
      couponCode: toStringValue(redemption.couponCode),
      couponId: toNullableString(redemption.couponId),
      couponVersionId: toNullableString(redemption.couponVersionId),
      userId: toStringValue(redemption.userId),
      quoteId: toNullableString(redemption.quoteId),
      checkoutSessionId: toNullableString(redemption.checkoutSessionId),
      subscriptionId: toNullableString(redemption.subscriptionId),
      discountAmount: toNumberValue(redemption.discountAmount),
      currency: toStringValue(redemption.currency, 'AUD'),
      status: toStringValue(redemption.status).toLowerCase(),
      redeemedAt: toStringValue(redemption.redeemedAt),
    })),
    subscriptionItems: asArray(raw.subscriptionItems).map((item) => ({
      id: toStringValue(item.id),
      subscriptionId: toStringValue(item.subscriptionId),
      itemType: toStringValue(item.itemType),
      itemCode: toStringValue(item.itemCode),
      addOnVersionId: toNullableString(item.addOnVersionId),
      quantity: toNumberValue(item.quantity, 1),
      status: toStringValue(item.status).toLowerCase(),
      quoteId: toNullableString(item.quoteId),
      checkoutSessionId: toNullableString(item.checkoutSessionId),
      startsAt: toStringValue(item.startsAt),
      endsAt: toNullableString(item.endsAt),
    })),
    events: asArray(raw.events).map((event) => ({
      id: toStringValue(event.id),
      eventType: toStringValue(event.eventType),
      entityType: toStringValue(event.entityType),
      entityId: toStringValue(event.entityId),
      subscriptionId: toNullableString(event.subscriptionId),
      quoteId: toNullableString(event.quoteId),
      occurredAt: toStringValue(event.occurredAt),
    })),
    catalogAnchors: {
      planVersionId: toNullableString(asRecord(raw.catalogAnchors).planVersionId),
      addOnVersionIds: asStringRecord(asRecord(raw.catalogAnchors).addOnVersionIds),
      couponVersionId: toNullableString(asRecord(raw.catalogAnchors).couponVersionId),
      source: toStringValue(asRecord(raw.catalogAnchors).source, 'not_recorded'),
    },
    notRecorded: parseJsonArray(raw.notRecorded),
    integrityFlags: parseJsonArray(raw.integrityFlags),
  };
}

export async function getAdminBillingPaymentTransactionData(
  params?: Parameters<typeof fetchAdminBillingPaymentTransactions>[0],
): Promise<AdminBillingPaymentTransactionResponse> {
  const raw = asRecord(await fetchAdminBillingPaymentTransactions(params));
  return {
    total: toNumberValue(raw.total),
    page: toNumberValue(raw.page, 1),
    pageSize: toNumberValue(raw.pageSize, 20),
    items: asArray(raw.items).map<AdminBillingPaymentTransaction>((payment) => ({
      id: toStringValue(payment.id),
      learnerUserId: toStringValue(payment.learnerUserId),
      learnerName: toStringValue(payment.learnerName, toStringValue(payment.learnerUserId)),
      gateway: toStringValue(payment.gateway).toLowerCase(),
      gatewayTransactionId: toStringValue(payment.gatewayTransactionId),
      transactionType: toStringValue(payment.transactionType).toLowerCase(),
      status: toStringValue(payment.status).toLowerCase(),
      amount: toNumberValue(payment.amount),
      currency: toStringValue(payment.currency, 'AUD').trim().toUpperCase() || 'AUD',
      productType: toStringValue(payment.productType),
      productId: toStringValue(payment.productId),
      quoteId: toNullableString(payment.quoteId),
      planVersionId: toNullableString(payment.planVersionId),
      addOnVersionIds: asStringRecord(payment.addOnVersionIds),
      couponVersionId: toNullableString(payment.couponVersionId),
      createdAt: toStringValue(payment.createdAt),
      updatedAt: toStringValue(payment.updatedAt),
    })),
  };
}

export async function getAdminBillingProviderLifecycleSignalsData(
  params?: Parameters<typeof fetchAdminBillingProviderLifecycleSignals>[0],
): Promise<AdminBillingProviderLifecycleSignalsResponse> {
  const raw = asRecord(await fetchAdminBillingProviderLifecycleSignals(params));
  const summary = asRecord(raw.summary);

  return {
    total: toNumberValue(raw.total),
    page: toNumberValue(raw.page, 1),
    pageSize: toNumberValue(raw.pageSize, 20),
    summary: {
      totalSignals: toNumberValue(summary.totalSignals),
      failedSignals: toNumberValue(summary.failedSignals),
      unverifiedSignals: toNumberValue(summary.unverifiedSignals),
      unmatchedSignals: toNumberValue(summary.unmatchedSignals),
      refundSignals: toNumberValue(summary.refundSignals),
      disputeSignals: toNumberValue(summary.disputeSignals),
      cancellationSignals: toNumberValue(summary.cancellationSignals),
    },
    items: asArray(raw.items).map<AdminBillingProviderLifecycleSignal>((signal) => {
      const linkedLocalIds = asRecord(signal.linkedLocalIds);
      return {
        id: toStringValue(signal.id),
        source: toStringValue(signal.source, 'payment_webhook_event'),
        category: toStringValue(signal.category, 'unknown').toLowerCase(),
        correlationStatus: toStringValue(signal.correlationStatus, 'not_recorded').toLowerCase(),
        confidence: toStringValue(signal.confidence, 'low').toLowerCase(),
        gateway: toStringValue(signal.gateway).toLowerCase(),
        eventType: toStringValue(signal.eventType),
        maskedProviderEventId: toStringValue(signal.maskedProviderEventId, 'not_recorded'),
        maskedProviderTransactionId: toNullableString(signal.maskedProviderTransactionId),
        processingStatus: toStringValue(signal.processingStatus, 'received').toLowerCase(),
        verificationStatus: toStringValue(signal.verificationStatus, 'legacy').toLowerCase(),
        normalizedStatus: toNullableString(signal.normalizedStatus),
        receivedAt: toStringValue(signal.receivedAt),
        processedAt: toNullableString(signal.processedAt),
        linkedLocalIds: {
          paymentTransactionIds: parseJsonArray(linkedLocalIds.paymentTransactionIds),
          invoiceIds: parseJsonArray(linkedLocalIds.invoiceIds),
          quoteIds: parseJsonArray(linkedLocalIds.quoteIds),
          subscriptionIds: parseJsonArray(linkedLocalIds.subscriptionIds),
          billingEventIds: parseJsonArray(linkedLocalIds.billingEventIds),
        },
        billingEventCount: toNumberValue(signal.billingEventCount),
        integrityFlags: parseJsonArray(signal.integrityFlags),
      };
    }),
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

function normalizeDiagnosticsSeverity(value: unknown): 'info' | 'warning' | 'danger' {
  const severity = toStringValue(value).toLowerCase();
  return severity === 'danger' || severity === 'warning' ? severity : 'info';
}

export async function getAdminBillingEntitlementDiagnosticsData(): Promise<AdminBillingEntitlementDiagnostics> {
  const raw = asRecord(await fetchAdminBillingEntitlementDiagnostics());
  const summary = asRecord(raw.summary);
  return {
    generatedAt: toStringValue(raw.generatedAt),
    summary: {
      invalidAiQuotaMappings: toNumberValue(summary.invalidAiQuotaMappings),
      missingPlanSubscriptions: toNumberValue(summary.missingPlanSubscriptions),
      fallbackMappings: toNumberValue(summary.fallbackMappings),
      legacyContentShape: toNumberValue(summary.legacyContentShape),
      totalWarnings: toNumberValue(summary.totalWarnings),
    },
    checks: asArray(raw.checks).map<AdminBillingEntitlementDiagnosticCheck>((check) => ({
      key: toStringValue(check.key),
      label: toStringValue(check.label),
      severity: normalizeDiagnosticsSeverity(check.severity),
      count: toNumberValue(check.count),
      examples: asArray(check.examples).map<AdminBillingEntitlementDiagnosticExample>((example) => ({
        subjectType: toStringValue(example.subjectType),
        subjectId: toStringValue(example.subjectId),
        subjectCode: toStringValue(example.subjectCode),
        subjectName: toStringValue(example.subjectName),
        message: toStringValue(example.message),
        metadata: asStringRecord(example.metadata),
      })),
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
    submissionMode: toStringValue(item.submissionMode, 'computer'),
    assessorType: toStringValue(item.assessorType, 'instructor'),
    paperAssetCount: toNumberValue(item.paperAssetCount),
    paperAssetsExtracted: toNumberValue(item.paperAssetsExtracted),
    voiceNoteCount: toNumberValue(item.voiceNoteCount),
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

// ── Content Publishing Workflow ─────────────────────────

export async function getAdminPublishRequestsData(
  params?: Parameters<typeof fetchPublishRequests>[0],
): Promise<AdminPublishRequestsResponse> {
  const raw = asRecord(await fetchPublishRequests(params));
  return {
    items: asArray(raw.items).map((r) => ({
      id: toStringValue(r.id),
      contentItemId: toStringValue(r.contentItemId),
      requestedBy: toStringValue(r.requestedBy),
      requestedByName: toStringValue(r.requestedByName),
      reviewedBy: toNullableString(r.reviewedBy),
      reviewedByName: toNullableString(r.reviewedByName),
      status: normalizePublishRequestStatus(r.status),
      stage: normalizePublishRequestStage(r.stage),
      requestNote: toNullableString(r.requestNote),
      reviewNote: toNullableString(r.reviewNote),
      requestedAt: toStringValue(r.requestedAt),
      reviewedAt: toNullableString(r.reviewedAt),
      editorReviewedBy: toNullableString(r.editorReviewedBy),
      editorReviewedByName: toNullableString(r.editorReviewedByName),
      editorReviewedAt: toNullableString(r.editorReviewedAt),
      editorNotes: toNullableString(r.editorNotes),
      publisherApprovedBy: toNullableString(r.publisherApprovedBy),
      publisherApprovedByName: toNullableString(r.publisherApprovedByName),
      publisherApprovedAt: toNullableString(r.publisherApprovedAt),
      publisherNotes: toNullableString(r.publisherNotes),
      rejectedBy: toNullableString(r.rejectedBy),
      rejectedByName: toNullableString(r.rejectedByName),
      rejectedAt: toNullableString(r.rejectedAt),
      rejectionReason: toNullableString(r.rejectionReason),
      rejectionStage: toNullableString(r.rejectionStage),
    })),
    total: toNumberValue(raw.total),
    page: toNumberValue(raw.page, 1),
    pageSize: toNumberValue(raw.pageSize, 20),
  };
}

function normalizePublishRequestStatus(value: unknown): AdminPublishRequest['status'] {
  const normalized = toStringValue(value, 'pending').toLowerCase();
  if (normalized === 'editor_review') return 'editor_review';
  if (normalized === 'publisher_approval') return 'publisher_approval';
  if (normalized === 'approved') return 'approved';
  if (normalized === 'rejected') return 'rejected';
  return 'pending';
}

function normalizePublishRequestStage(value: unknown): AdminPublishRequest['stage'] {
  const normalized = toStringValue(value, 'editor_review').toLowerCase();
  if (normalized === 'publisher_approval') return 'publisher_approval';
  return 'editor_review';
}

// ── Webhook Monitoring ──────────────────────────────────

export async function getAdminWebhookEventsData(
  params?: Parameters<typeof fetchWebhookEvents>[0],
): Promise<AdminWebhookEventsResponse> {
  const raw = asRecord(await fetchWebhookEvents(params));
  return {
    items: asArray(raw.items).map((e) => ({
      id: toStringValue(e.id),
      gateway: toStringValue(e.gateway),
      eventType: toStringValue(e.eventType),
      gatewayEventId: toStringValue(e.gatewayEventId),
      processingStatus: normalizeWebhookStatus(e.processingStatus),
      verificationStatus: normalizeWebhookVerificationStatus(e.verificationStatus),
      gatewayTransactionId: toNullableString(e.gatewayTransactionId),
      normalizedStatus: toNullableString(e.normalizedStatus),
      attemptCount: toNumberValue(e.attemptCount),
      retryCount: toNumberValue(e.retryCount),
      lastAttemptedAt: toNullableString(e.lastAttemptedAt),
      lastRetriedAt: toNullableString(e.lastRetriedAt),
      retryable: Boolean(e.retryable),
      retryBlockedReason: toNullableString(e.retryBlockedReason),
      errorMessage: toNullableString(e.errorMessage),
      receivedAt: toStringValue(e.receivedAt),
      processedAt: toNullableString(e.processedAt),
    })),
    total: toNumberValue(raw.total),
    page: toNumberValue(raw.page, 1),
    pageSize: toNumberValue(raw.pageSize, 20),
  };
}

function normalizeWebhookStatus(value: unknown): 'received' | 'processing' | 'completed' | 'failed' | 'ignored' {
  const normalized = toStringValue(value, 'received').toLowerCase();
  if (normalized === 'processing') return 'processing';
  if (normalized === 'completed') return 'completed';
  if (normalized === 'failed') return 'failed';
  if (normalized === 'ignored') return 'ignored';
  return 'received';
}

function normalizeWebhookVerificationStatus(value: unknown): 'legacy' | 'verified' | 'failed' {
  const normalized = toStringValue(value, 'legacy').toLowerCase();
  if (normalized === 'verified') return 'verified';
  if (normalized === 'failed') return 'failed';
  return 'legacy';
}

export async function getAdminWebhookSummaryData(): Promise<AdminWebhookSummary> {
  const raw = asRecord(await fetchWebhookSummary());
  return {
    total: toNumberValue(raw.total),
    recent24h: toNumberValue(raw.recent24h),
    failed: toNumberValue(raw.failed),
    failed24h: toNumberValue(raw.failed24h),
    byStatus: asArray(raw.byStatus).map((s) => ({
      status: toStringValue(s.status),
      count: toNumberValue(s.count),
    })),
    byGateway: asArray(raw.byGateway).map((g) => ({
      gateway: toStringValue(g.gateway),
      count: toNumberValue(g.count),
    })),
    recentFailures: asArray(raw.recentFailures).map((f) => ({
      id: toStringValue(f.id),
      eventType: toStringValue(f.eventType),
      errorMessage: toNullableString(f.errorMessage),
      receivedAt: toStringValue(f.receivedAt),
      retryable: Boolean(f.retryable),
      retryBlockedReason: toNullableString(f.retryBlockedReason),
    })),
  };
}

// ── Review Escalation ───────────────────────────────────

export async function getAdminEscalationsData(
  params?: Parameters<typeof fetchReviewEscalations>[0],
): Promise<AdminEscalationsResponse> {
  const raw = asRecord(await fetchReviewEscalations(params));
  return {
    items: asArray(raw.items).map((e) => ({
      id: toStringValue(e.id),
      reviewRequestId: toStringValue(e.reviewRequestId),
      originalReviewerId: toStringValue(e.originalReviewerId),
      secondReviewerId: toNullableString(e.secondReviewerId),
      subtestCode: toStringValue(e.subtestCode),
      triggerCriterion: toStringValue(e.triggerCriterion),
      aiScore: toNumberValue(e.aiScore),
      humanScore: toNumberValue(e.humanScore),
      divergence: toNumberValue(e.divergence),
      status: normalizeEscalationStatus(e.status),
      resolutionNote: toNullableString(e.resolutionNote),
      finalScore: e.finalScore != null ? toNumberValue(e.finalScore) : null,
      createdAt: toStringValue(e.createdAt),
      resolvedAt: toNullableString(e.resolvedAt),
    })),
    total: toNumberValue(raw.total),
    page: toNumberValue(raw.page, 1),
    pageSize: toNumberValue(raw.pageSize, 20),
  };
}

function normalizeEscalationStatus(value: unknown): 'pending' | 'assigned' | 'resolved' {
  const normalized = toStringValue(value, 'pending').toLowerCase();
  if (normalized === 'assigned') return 'assigned';
  if (normalized === 'resolved') return 'resolved';
  return 'pending';
}

// ── Score Guarantee Claims (Admin) ──────────────────

export async function getAdminScoreGuaranteeClaimsData(
  params?: Parameters<typeof fetchAdminScoreGuaranteeClaims>[0],
): Promise<AdminScoreGuaranteeClaimsResponse> {
  const raw = asRecord(await fetchAdminScoreGuaranteeClaims(params));
  return {
    items: asArray(raw.items).map(
      (p): AdminScoreGuaranteeClaim => ({
        id: toStringValue(p.id),
        userId: toStringValue(p.userId),
        subscriptionId: toStringValue(p.subscriptionId),
        baselineScore: toNumberValue(p.baselineScore),
        guaranteedImprovement: toNumberValue(p.guaranteedImprovement),
        actualScore: p.actualScore != null ? toNumberValue(p.actualScore) : null,
        status: normalizeGuaranteeClaimStatus(p.status),
        proofDocumentUrl: toNullableString(p.proofDocumentUrl),
        claimNote: toNullableString(p.claimNote),
        reviewNote: toNullableString(p.reviewNote),
        reviewedBy: toNullableString(p.reviewedBy),
        activatedAt: toStringValue(p.activatedAt),
        expiresAt: toStringValue(p.expiresAt),
      }),
    ),
    total: toNumberValue(raw.total),
    page: toNumberValue(raw.page, 1),
    pageSize: toNumberValue(raw.pageSize, 20),
  };
}

function normalizeGuaranteeClaimStatus(
  value: unknown,
): 'active' | 'claim_submitted' | 'claim_approved' | 'claim_rejected' | 'expired' {
  const s = toStringValue(value, 'active').toLowerCase();
  if (s === 'claim_submitted') return 'claim_submitted';
  if (s === 'claim_approved') return 'claim_approved';
  if (s === 'claim_rejected') return 'claim_rejected';
  if (s === 'expired') return 'expired';
  return 'active';
}

export async function getAdminSubscriptionHealthData(): Promise<AdminSubscriptionHealthData> {
  const raw = asRecord(await fetchAdminSubscriptionHealth());
  return {
    mrr: toNumberValue(raw.mrr),
    activeSubscriptions: toNumberValue(raw.activeSubscriptions),
    churnRate: toNumberValue(raw.churnRate),
    newSubscriptionsThisMonth: toNumberValue(raw.newSubscriptionsThisMonth),
    trialConversionRate: toNumberValue(raw.trialConversionRate),
    arpu: toNumberValue(raw.arpu),
    revenueByPlan: asArray(raw.revenueByPlan).map((item) => ({
      planId: toStringValue(item.planId),
      planName: toStringValue(item.planName),
      subscribers: toNumberValue(item.subscribers),
      monthlyRevenue: toNumberValue(item.monthlyRevenue),
    })),
    monthlyTrend: asArray(raw.monthlyTrend).map((item) => ({
      month: toStringValue(item.month),
      newSubscriptions: toNumberValue(item.newSubscriptions),
      cancellations: toNumberValue(item.cancellations),
    })),
    generatedAt: toStringValue(raw.generatedAt, new Date().toISOString()),
  };
}

export async function getAdminCohortAnalysisData(params?: Parameters<typeof fetchAdminCohortAnalysis>[0]): Promise<AdminCohortAnalysisData> {
  const raw = asRecord(await fetchAdminCohortAnalysis(params));
  return {
    groupBy: toStringValue(raw.groupBy, params?.groupBy ?? 'profession'),
    cohorts: asArray(raw.cohorts).map((item) => ({
      cohortKey: toStringValue(item.cohortKey),
      cohortName: toStringValue(item.cohortName),
      learnerCount: toNumberValue(item.learnerCount),
      averageScore: item.averageScore == null ? null : toNumberValue(item.averageScore),
      evaluationCount: toNumberValue(item.evaluationCount),
      activeLastMonth: toNumberValue(item.activeLastMonth),
    })),
    totalLearners: toNumberValue(raw.totalLearners),
    generatedAt: toStringValue(raw.generatedAt, new Date().toISOString()),
  };
}

export async function getAdminContentEffectivenessData(params?: Parameters<typeof fetchAdminContentEffectiveness>[0]): Promise<AdminContentEffectivenessData> {
  const raw = asRecord(await fetchAdminContentEffectiveness(params));
  return {
    subtestFilter: toNullableString(raw.subtestFilter),
    items: asArray(raw.items).map((item) => ({
      contentId: toStringValue(item.contentId),
      title: toStringValue(item.title),
      subtestCode: toStringValue(item.subtestCode),
      difficulty: toStringValue(item.difficulty),
      totalAttempts: toNumberValue(item.totalAttempts),
      completionRate: toNumberValue(item.completionRate),
      averageScore: item.averageScore == null ? null : toNumberValue(item.averageScore),
      avgTimeSeconds: item.avgTimeSeconds == null ? null : toNumberValue(item.avgTimeSeconds),
      effectivenessScore: item.effectivenessScore == null ? null : toNumberValue(item.effectivenessScore),
    })),
    generatedAt: toStringValue(raw.generatedAt, new Date().toISOString()),
  };
}

export async function getAdminExpertEfficiencyData(params?: Parameters<typeof fetchAdminExpertEfficiency>[0]): Promise<AdminExpertEfficiencyData> {
  const raw = asRecord(await fetchAdminExpertEfficiency(params));
  return {
    period: toNumberValue(raw.period, params?.days ?? 30),
    experts: asArray(raw.experts).map((item) => ({
      expertId: toStringValue(item.expertId),
      expertName: toStringValue(item.expertName),
      period: toNumberValue(item.period, params?.days ?? 30),
      assignmentsReceived: toNumberValue(item.assignmentsReceived),
      reviewsCompleted: toNumberValue(item.reviewsCompleted),
      averageReviewTimeMinutes: item.averageReviewTimeMinutes == null ? null : toNumberValue(item.averageReviewTimeMinutes),
      reviewsPerDay: toNumberValue(item.reviewsPerDay),
      aiAlignmentScore: item.aiAlignmentScore == null ? null : toNumberValue(item.aiAlignmentScore),
      efficiency: normalizeEfficiency(item.efficiency),
    })),
    summary: {
      totalExperts: toNumberValue(asRecord(raw.summary).totalExperts),
      activeExperts: toNumberValue(asRecord(raw.summary).activeExperts),
      totalReviewsCompleted: toNumberValue(asRecord(raw.summary).totalReviewsCompleted),
      averageReviewsPerExpertPerDay: toNumberValue(asRecord(raw.summary).averageReviewsPerExpertPerDay),
    },
    generatedAt: toStringValue(raw.generatedAt, new Date().toISOString()),
  };
}

export async function getAdminBusinessIntelligenceData(): Promise<AdminBusinessIntelligenceData> {
  const [subscriptionHealth, cohortAnalysis, contentEffectiveness, expertEfficiency] = await Promise.all([
    getAdminSubscriptionHealthData(),
    getAdminCohortAnalysisData({ groupBy: 'profession' }),
    getAdminContentEffectivenessData({ top: 4 }),
    getAdminExpertEfficiencyData({ days: 30 }),
  ]);

  return {
    generatedAt: subscriptionHealth.generatedAt,
    subscriptionHealth,
    cohortAnalysis,
    contentEffectiveness,
    expertEfficiency,
  };
}

export async function fetchAdminAlerts() {
  return apiClient.get('/v1/admin/alerts');
}

function normalizeEfficiency(value: unknown): AdminExpertEfficiencyData['experts'][number]['efficiency'] {
  const normalized = toStringValue(value, 'no-data').toLowerCase();
  if (normalized === 'high' || normalized === 'medium' || normalized === 'low') return normalized;
  return 'no-data';
}
