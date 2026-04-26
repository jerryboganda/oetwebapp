import type { ReactNode } from 'react';
import { screen } from '@testing-library/react';
const navigation = vi.hoisted(() => ({
  pathname: '/admin',
  searchParams: new URLSearchParams(),
  params: {} as Record<string, string>,
  push: vi.fn(),
  replace: vi.fn(),
  back: vi.fn(),
  refresh: vi.fn(),
}));

const admin = vi.hoisted(() => ({
  getAdminDashboardData: vi.fn(),
  getAdminAIConfigData: vi.fn(),
  getAdminAuditLogPageData: vi.fn(),
  getAdminAuditLogDetailData: vi.fn(),
  getAdminContentLibraryData: vi.fn(),
  getAdminContentDetailData: vi.fn(),
  getAdminContentImpactData: vi.fn(),
  getAdminReviewOpsSummaryData: vi.fn(),
  getAdminReviewQueueData: vi.fn(),
  getAdminReviewFailureData: vi.fn(),
  getAdminCriteriaData: vi.fn(),
  getAdminFlagData: vi.fn(),
  getAdminTaxonomyData: vi.fn(),
  getAdminTaxonomyImpactData: vi.fn(),
  getAdminUsersPageData: vi.fn(),
  getAdminBillingPlanData: vi.fn(),
  getAdminBillingAddOnData: vi.fn(),
  getAdminBillingCouponData: vi.fn(),
  getAdminBillingCouponRedemptionData: vi.fn(),
  getAdminBillingInvoiceData: vi.fn(),
  getAdminBillingSubscriptionData: vi.fn(),
  getAdminQualityAnalyticsData: vi.fn(),
  getAdminUserDetailData: vi.fn(),
  getAdminContentRevisionData: vi.fn(),
}));

const api = vi.hoisted(() => ({
  activateAdminAIConfig: vi.fn(),
  activateAdminFlag: vi.fn(),
  assignAdminReview: vi.fn(),
  archiveAdminTaxonomy: vi.fn(),
  cancelAdminReview: vi.fn(),
  createAdminAIConfig: vi.fn(),
  reopenAdminReview: vi.fn(),
  createAdminContent: vi.fn(),
  createAdminCriterion: vi.fn(),
  createAdminFlag: vi.fn(),
  createAdminTaxonomy: vi.fn(),
  createAdminBillingPlan: vi.fn(),
  updateAdminBillingPlan: vi.fn(),
  createAdminBillingAddOn: vi.fn(),
  updateAdminBillingAddOn: vi.fn(),
  createAdminBillingCoupon: vi.fn(),
  updateAdminBillingCoupon: vi.fn(),
  deactivateAdminFlag: vi.fn(),
  exportAdminAuditLogs: vi.fn(),
  fetchAdminAuditLogs: vi.fn(),
  inviteAdminUser: vi.fn(),
  publishAdminContent: vi.fn(),
  triggerAdminUserPasswordReset: vi.fn(),
  updateAdminAIConfig: vi.fn(),
  updateAdminContent: vi.fn(),
  updateAdminCriterion: vi.fn(),
  updateAdminFlag: vi.fn(),
  updateAdminTaxonomy: vi.fn(),
  updateAdminUserStatus: vi.fn(),
  deleteAdminUser: vi.fn(),
  restoreAdminUser: vi.fn(),
  adjustAdminUserCredits: vi.fn(),
  restoreAdminContentRevision: vi.fn(),
}));

const notificationsApi = vi.hoisted(() => ({
  fetchAdminNotificationCatalog: vi.fn(),
  fetchAdminNotificationPolicies: vi.fn(),
  fetchAdminNotificationHealth: vi.fn(),
  fetchAdminNotificationDeliveries: vi.fn(),
  updateAdminNotificationPolicy: vi.fn(),
  sendAdminNotificationTestEmail: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({
    children,
    href,
    asChild,
    ...props
  }: {
    children: ReactNode;
    href: string;
    asChild?: boolean;
  }) => (
    <a href={href} {...props}>
      {children}
    </a>
  ),
}));

vi.mock('recharts', () => ({
  ResponsiveContainer: ({ children }: { children: ReactNode }) => <div data-testid="responsive-chart">{children}</div>,
  LineChart: ({ children }: { children: ReactNode }) => <div data-testid="line-chart">{children}</div>,
  Line: () => null,
  CartesianGrid: () => null,
  Legend: () => null,
  Tooltip: () => null,
  XAxis: () => null,
  YAxis: () => null,
}));

vi.mock('@/lib/hooks/use-admin-auth', () => ({
  useAdminAuth: () => ({
    isAuthenticated: true,
    role: 'admin',
  }),
}));

vi.mock('@/lib/hooks/use-current-user', () => ({
  useCurrentUser: () => ({
    user: {
      userId: 'admin-1',
      displayName: 'Admin User',
      email: 'admin@example.com',
      isEmailVerified: true,
      isAuthenticatorEnabled: false,
      adminPermissions: ['content:create', 'content:edit', 'content:publish', 'content:publisher_approval', 'content:archive', 'content:delete', 'user:manage', 'user:invite'],
    },
    role: 'admin' as const,
    isAuthenticated: true,
    isLoading: false,
    pendingMfaChallenge: null,
  }),
}));

vi.mock('@/lib/admin', () => ({
  getAdminDashboardData: admin.getAdminDashboardData,
  getAdminAIConfigData: admin.getAdminAIConfigData,
  getAdminAuditLogPageData: admin.getAdminAuditLogPageData,
  getAdminAuditLogDetailData: admin.getAdminAuditLogDetailData,
  getAdminContentLibraryData: admin.getAdminContentLibraryData,
  getAdminContentDetailData: admin.getAdminContentDetailData,
  getAdminContentImpactData: admin.getAdminContentImpactData,
  getAdminReviewOpsSummaryData: admin.getAdminReviewOpsSummaryData,
  getAdminReviewQueueData: admin.getAdminReviewQueueData,
  getAdminReviewFailureData: admin.getAdminReviewFailureData,
  getAdminCriteriaData: admin.getAdminCriteriaData,
  getAdminFlagData: admin.getAdminFlagData,
  getAdminTaxonomyData: admin.getAdminTaxonomyData,
  getAdminTaxonomyImpactData: admin.getAdminTaxonomyImpactData,
  getAdminUsersPageData: admin.getAdminUsersPageData,
  getAdminBillingPlanData: admin.getAdminBillingPlanData,
  getAdminBillingAddOnData: admin.getAdminBillingAddOnData,
  getAdminBillingCouponData: admin.getAdminBillingCouponData,
  getAdminBillingCouponRedemptionData: admin.getAdminBillingCouponRedemptionData,
  getAdminBillingInvoiceData: admin.getAdminBillingInvoiceData,
  getAdminBillingSubscriptionData: admin.getAdminBillingSubscriptionData,
  getAdminQualityAnalyticsData: admin.getAdminQualityAnalyticsData,
  getAdminUserDetailData: admin.getAdminUserDetailData,
  getAdminContentRevisionData: admin.getAdminContentRevisionData,
}));

vi.mock('@/lib/api', () => ({
  activateAdminAIConfig: api.activateAdminAIConfig,
  activateAdminFlag: api.activateAdminFlag,
  assignAdminReview: api.assignAdminReview,
  archiveAdminTaxonomy: api.archiveAdminTaxonomy,
  cancelAdminReview: api.cancelAdminReview,
  createAdminAIConfig: api.createAdminAIConfig,
  createAdminContent: api.createAdminContent,
  createAdminCriterion: api.createAdminCriterion,
  createAdminFlag: api.createAdminFlag,
  createAdminTaxonomy: api.createAdminTaxonomy,
  reopenAdminReview: api.reopenAdminReview,
  createAdminBillingPlan: api.createAdminBillingPlan,
  updateAdminBillingPlan: api.updateAdminBillingPlan,
  createAdminBillingAddOn: api.createAdminBillingAddOn,
  updateAdminBillingAddOn: api.updateAdminBillingAddOn,
  createAdminBillingCoupon: api.createAdminBillingCoupon,
  updateAdminBillingCoupon: api.updateAdminBillingCoupon,
  deactivateAdminFlag: api.deactivateAdminFlag,
  exportAdminAuditLogs: api.exportAdminAuditLogs,
  fetchAdminAuditLogs: api.fetchAdminAuditLogs,
  inviteAdminUser: api.inviteAdminUser,
  publishAdminContent: api.publishAdminContent,
  triggerAdminUserPasswordReset: api.triggerAdminUserPasswordReset,
  updateAdminAIConfig: api.updateAdminAIConfig,
  updateAdminContent: api.updateAdminContent,
  updateAdminCriterion: api.updateAdminCriterion,
  updateAdminFlag: api.updateAdminFlag,
  updateAdminTaxonomy: api.updateAdminTaxonomy,
  updateAdminUserStatus: api.updateAdminUserStatus,
  deleteAdminUser: api.deleteAdminUser,
  restoreAdminUser: api.restoreAdminUser,
  adjustAdminUserCredits: api.adjustAdminUserCredits,
  restoreAdminContentRevision: api.restoreAdminContentRevision,
  isApiError: () => false,
}));

vi.mock('@/lib/notifications-api', () => ({
  fetchAdminNotificationCatalog: notificationsApi.fetchAdminNotificationCatalog,
  fetchAdminNotificationPolicies: notificationsApi.fetchAdminNotificationPolicies,
  fetchAdminNotificationHealth: notificationsApi.fetchAdminNotificationHealth,
  fetchAdminNotificationDeliveries: notificationsApi.fetchAdminNotificationDeliveries,
  updateAdminNotificationPolicy: notificationsApi.updateAdminNotificationPolicy,
  sendAdminNotificationTestEmail: notificationsApi.sendAdminNotificationTestEmail,
}));

import AdminDashboardPage from './page';
import AIConfigPage from './ai-config/page';
import AuditLogsPage from './audit-logs/page';
import ContentEditPage from './content/[id]/page';
import ContentLibraryPage from './content/page';
import ContentNewPage from './content/new/page';
import CriteriaPage from './criteria/page';
import FlagsPage from './flags/page';
import QualityAnalyticsPage from './analytics/quality/page';
import BillingPage from './billing/page';
import NotificationsPage from './notifications/page';
import ReviewOpsPage from './review-ops/page';
import TaxonomyPage from './taxonomy/page';
import UserDetailPage from './users/[id]/page';
import UsersPage from './users/page';
import RevisionsPage from './content/[id]/revisions/page';
import { renderWithRouter } from '@/tests/test-utils';

function renderPage(ui: React.ReactElement) {
  return renderWithRouter(ui, {
    pathname: navigation.pathname,
    searchParams: navigation.searchParams,
    params: navigation.params,
    router: { push: navigation.push, replace: navigation.replace, back: navigation.back, refresh: navigation.refresh },
  });
}

beforeAll(() => {
  class ResizeObserverMock {
    observe() {}
    disconnect() {}
    unobserve() {}
  }

  vi.stubGlobal('ResizeObserver', ResizeObserverMock);
});

beforeEach(() => {
  vi.clearAllMocks();
  navigation.pathname = '/admin';
  navigation.searchParams = new URLSearchParams();
  navigation.params = {};
});

describe('Admin Non-Editor Pages', () => {
  it('renders the root dashboard inside the learner-style route surface', async () => {
    admin.getAdminDashboardData.mockResolvedValue({
      generatedAt: '2026-04-01T09:00:00.000Z',
      freshness: { qualityWindow: '30d' },
      contentHealth: { published: 12, drafts: 3, archived: 2, staleDrafts: 1 },
      reviewOps: { backlog: 5, overdue: 1, inProgress: 2, failedReviews: 1, failedJobs: 0 },
      billingRisk: { failedInvoices: 2, pendingInvoices: 1, legacyPlans: 1, activeSubscribers: 123 },
      quality: { agreementRate: 92, evaluationCount: 88, riskCases: 1 },
      flags: { enabled: 4, total: 7, liveExperiments: 2, recentChanges: 3 },
    });

    renderPage(<AdminDashboardPage />);

    expect(await screen.findByRole('main', { name: /admin operations/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /keep platform health, review risk, and rollout signals in one place/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /operational shortcuts/i })).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: /open review ops/i })).toHaveLength(2);
  });

  it('renders the content library inside the learner-style route surface', async () => {
    admin.getAdminContentLibraryData.mockResolvedValue({
      items: [
        {
          id: 'content-1',
          title: 'Discharge Letter Set A',
          type: 'writing_task',
          profession: 'medicine',
          author: 'Editorial Ops',
          status: 'published',
          revisionCount: 4,
          updatedAt: '2026-04-01T08:00:00.000Z',
        },
      ],
      total: 1,
    });

    renderPage(<ContentLibraryPage />);

    expect(await screen.findByRole('main', { name: /content library/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /^content library$/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /content items/i })).toBeInTheDocument();
    expect(screen.getAllByText('Discharge Letter Set A').length).toBeGreaterThan(0);
  });

  it('renders the AI config registry inside the learner-style route surface and keeps activation controls visible', async () => {
    admin.getAdminAIConfigData.mockResolvedValue([
      {
        id: 'config-1',
        model: 'gpt-5-mini',
        provider: 'openai',
        taskType: 'writing',
        status: 'testing',
        accuracy: 0.91,
        confidenceThreshold: 0.82,
        routingRule: 'Escalate below threshold',
        experimentFlag: 'ai_writing_eval_v2',
        promptLabel: 'writing-v2',
      },
    ]);

    renderPage(<AIConfigPage />);

    expect(await screen.findByRole('main', { name: /ai evaluation config/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /^ai evaluation config$/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /configuration registry/i })).toBeInTheDocument();
    expect(screen.getAllByText('gpt-5-mini').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /new configuration/i })).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /activate/i }).length).toBeGreaterThan(0);
  });

  it('renders the audit log registry inside the learner-style route surface', async () => {
    admin.getAdminAuditLogPageData.mockImplementation(async (params?: { pageSize?: number }) => ({
      total: 1,
      page: 1,
      pageSize: params?.pageSize ?? 100,
      items: [
        {
          id: 'audit-1',
          timestamp: '2026-04-01T09:20:00.000Z',
          actor: 'Admin User',
          action: 'policy.updated',
          resource: 'notifications/review_ready',
          details: 'Policy updated.',
        },
      ],
    }));
    admin.getAdminAuditLogDetailData.mockResolvedValue({
      id: 'audit-1',
      timestamp: '2026-04-01T09:20:00.000Z',
      actorId: 'admin-1',
      actorName: 'Admin User',
      action: 'policy.updated',
      resourceType: 'notification_policy',
      resourceId: 'review_ready',
      details: 'Policy updated.',
    });

    renderPage(<AuditLogsPage />);

    expect(await screen.findByRole('main', { name: /audit logs/i })).toBeInTheDocument();
    expect((await screen.findAllByText('Policy updated.')).length).toBeGreaterThan(0);
    expect(screen.getByRole('heading', { name: /^audit logs$/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /audit stream/i })).toBeInTheDocument();
    expect(screen.getAllByText('Admin User').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /export csv/i })).toBeInTheDocument();
  });

  it('renders the criteria registry inside the learner-style route surface', async () => {
    admin.getAdminCriteriaData.mockResolvedValue([
      {
        id: 'criterion-1',
        name: 'Purpose',
        type: 'writing',
        weight: 2,
        status: 'active',
        description: 'Checks whether the purpose is achieved clearly.',
      },
    ]);

    renderPage(<CriteriaPage />);

    expect(await screen.findByRole('main', { name: /rubrics and criteria/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /rubrics & criteria/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /criteria library/i })).toBeInTheDocument();
    expect(screen.getAllByText('Purpose').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /add criterion/i })).toBeInTheDocument();
  });

  it('renders the feature flag registry inside the learner-style route surface and keeps toggle actions visible', async () => {
    admin.getAdminFlagData.mockResolvedValue([
      {
        id: 'flag-1',
        name: 'AI Review Pilot',
        key: 'ai_review_pilot',
        enabled: false,
        type: 'experiment',
        rolloutPercentage: 25,
        description: 'Pilot rollout for AI review.',
        owner: 'Platform Ops',
      },
    ]);

    renderPage(<FlagsPage />);

    expect(await screen.findByRole('main', { name: /feature flags/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /^feature flags$/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /rollout registry/i })).toBeInTheDocument();
    expect(screen.getAllByText('AI Review Pilot').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /create flag/i })).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /enable/i }).length).toBeGreaterThan(0);
  });

  it('renders the review operations page inside the learner-style route surface', async () => {
    admin.getAdminReviewOpsSummaryData.mockResolvedValue({
      backlog: 5,
      overdue: 1,
      slaRisk: 2,
      statusDistribution: {
        pending: 2,
        inProgress: 2,
        completed: 1,
      },
    });
    admin.getAdminReviewQueueData.mockResolvedValue([
      {
        id: 'review-1',
        learnerName: 'Dr Amina Khan',
        taskId: 'task-1',
        subtestCode: 'writing',
        priority: 'high',
        status: 'pending',
        assignedExpertId: null,
      },
    ]);
    admin.getAdminReviewFailureData.mockResolvedValue({
      summary: {
        failedReviewCount: 1,
        stuckReviewCount: 1,
        failedJobCount: 1,
      },
      failedReviews: [
        {
          id: 'failed-review-1',
          attemptId: 'attempt-1',
          subtestCode: 'writing',
          createdAt: '2026-04-01T08:30:00.000Z',
        },
      ],
      stuckReviews: [
        {
          id: 'stuck-review-1',
          attemptId: 'attempt-2',
          createdAt: '2026-04-01T07:30:00.000Z',
        },
      ],
      failedJobs: [
        {
          id: 'job-1',
          type: 'grade-review',
          reason: 'timeout',
          retryCount: 3,
          createdAt: '2026-04-01T06:30:00.000Z',
        },
      ],
    });
    admin.getAdminUsersPageData.mockResolvedValue({
      items: [{ id: 'expert-1', name: 'Expert One', email: 'expert@example.com' }],
      total: 1,
      page: 1,
      pageSize: 100,
    });

    renderPage(<ReviewOpsPage />);

    expect(await screen.findByRole('main', { name: /review operations/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /^review operations$/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /live queue/i })).toBeInTheDocument();
    expect(screen.getAllByText('Dr Amina Khan').length).toBeGreaterThan(0);
  });

  it('renders the billing page inside the learner-style route surface', async () => {
    admin.getAdminBillingPlanData.mockResolvedValue([
      {
        id: 'plan-1',
        code: 'starter',
        name: 'Starter',
        description: 'Starter plan',
        price: 49,
        currency: 'AUD',
        interval: 'month',
        durationMonths: 1,
        includedCredits: 4,
        displayOrder: 1,
        isVisible: true,
        isRenewable: true,
        trialDays: 0,
        status: 'active',
        includedSubtests: ['writing', 'speaking'],
        entitlements: {},
        activeSubscribers: 12,
      },
    ]);
    admin.getAdminBillingAddOnData.mockResolvedValue([
      {
        id: 'addon-1',
        code: 'credits-5',
        name: 'Credits 5',
        description: 'Five extra credits',
        price: 15,
        currency: 'AUD',
        interval: 'one_time',
        durationDays: 0,
        grantCredits: 5,
        displayOrder: 1,
        isRecurring: false,
        appliesToAllPlans: true,
        isStackable: true,
        quantityStep: 1,
        maxQuantity: null,
        status: 'active',
        compatiblePlanCodes: [],
        grantEntitlements: {},
      },
    ]);
    admin.getAdminBillingCouponData.mockResolvedValue([
      {
        id: 'coupon-1',
        code: 'WELCOME10',
        name: 'Welcome 10',
        description: 'Ten percent off',
        discountType: 'percentage',
        discountValue: 10,
        currency: 'AUD',
        startsAt: '2026-04-01T00:00:00.000Z',
        endsAt: null,
        usageLimitTotal: null,
        usageLimitPerUser: null,
        minimumSubtotal: null,
        isStackable: true,
        status: 'active',
        applicablePlanCodes: [],
        applicableAddOnCodes: [],
        notes: '',
        redemptionCount: 2,
      },
    ]);
    admin.getAdminBillingSubscriptionData.mockResolvedValue({ items: [] });
    admin.getAdminBillingCouponRedemptionData.mockResolvedValue({ items: [] });
    admin.getAdminBillingInvoiceData.mockResolvedValue({ items: [] });

    renderPage(<BillingPage />);

    expect(await screen.findByRole('main', { name: /billing operations/i })).toBeInTheDocument();
    expect((await screen.findAllByText('Starter')).length).toBeGreaterThan(0);
    expect(screen.getByRole('heading', { name: /^billing operations$/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /^subscription plans$/i })).toBeInTheDocument();
  });

  it('renders the notifications page inside the learner-style route surface', async () => {
    notificationsApi.fetchAdminNotificationCatalog.mockResolvedValue([
      {
        audienceRole: 'learner',
        eventKey: 'review_ready',
        label: 'Review Ready',
        description: 'Learner review delivery notice',
        category: 'reviews',
        defaultSeverity: 'info',
        defaultEmailMode: 'immediate',
      },
    ]);
    notificationsApi.fetchAdminNotificationPolicies.mockResolvedValue({
      rows: [
        {
          audienceRole: 'learner',
          eventKey: 'review_ready',
          label: 'Review Ready',
          category: 'reviews',
          inAppEnabled: true,
          emailEnabled: true,
          pushEnabled: false,
          emailMode: 'immediate',
          isOverride: false,
        },
      ],
      globalEmailEnabledByAudience: {
        learner: true,
        expert: false,
        admin: true,
      },
    });
    notificationsApi.fetchAdminNotificationHealth.mockResolvedValue({
      queuedEvents: 3,
      failedDeliveriesLast24Hours: 1,
      unreadInboxItems: 6,
      activePushSubscriptions: 2,
      channels: [
        {
          channel: 'email',
          sentLast24Hours: 10,
          failedLast24Hours: 1,
          suppressedLast24Hours: 0,
        },
      ],
      failureQueue: [
        {
          eventId: 'event-1',
          eventKey: 'review_ready',
          audienceRole: 'learner',
          channel: 'email',
          status: 'failed',
          errorMessage: 'Mailbox unavailable',
          errorCode: 'smtp_error',
          attemptedAt: '2026-04-01T09:15:00.000Z',
        },
      ],
    });
    notificationsApi.fetchAdminNotificationDeliveries.mockResolvedValue({
      items: [
        {
          id: 'delivery-1',
          eventKey: 'review_ready',
          audienceRole: 'learner',
          channel: 'email',
          status: 'sent',
          provider: 'smtp',
          attemptedAt: '2026-04-01T09:10:00.000Z',
        },
      ],
    });
    api.fetchAdminAuditLogs.mockResolvedValue({
      items: [
        {
          id: 'audit-1',
          timestamp: '2026-04-01T09:20:00.000Z',
          actor: 'Admin User',
          resource: 'notifications/review_ready',
          details: 'Policy updated.',
        },
      ],
    });

    renderPage(<NotificationsPage />);

    expect(await screen.findByRole('main', { name: /notifications/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /^notifications$/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /global email governance/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /policy change audit trail/i })).toBeInTheDocument();
  });

  it('renders the profession taxonomy registry inside the learner-style route surface', async () => {
    admin.getAdminTaxonomyData.mockResolvedValue([
      {
        id: 'taxonomy-1',
        label: 'Nursing',
        slug: 'nursing',
        type: 'profession',
        status: 'active',
        contentCount: 24,
      },
    ]);
    admin.getAdminTaxonomyImpactData.mockResolvedValue({
      professionId: 'taxonomy-1',
      label: 'Nursing',
      status: 'active',
      usage: {
        contentCount: 24,
        learnerCount: 16,
        goalCount: 9,
      },
      safeToArchive: false,
    });

    renderPage(<TaxonomyPage />);

    expect(await screen.findByRole('main', { name: /profession taxonomy/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /^profession taxonomy$/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /^professions$/i })).toBeInTheDocument();
    expect(screen.getAllByText('Nursing').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /add profession/i })).toBeInTheDocument();
  });

  it('renders the users directory inside the learner-style route surface and keeps invite controls visible', async () => {
    admin.getAdminUsersPageData.mockResolvedValue({
      items: [
        {
          id: 'user-1',
          name: 'Dr Sana Malik',
          email: 'sana@example.com',
          role: 'learner',
          status: 'active',
          lastLogin: '2026-04-01T08:00:00.000Z',
        },
      ],
      total: 1,
      page: 1,
      pageSize: 20,
    });

    renderPage(<UsersPage />);

    expect(await screen.findByRole('main', { name: /user operations/i })).toBeInTheDocument();
    expect((await screen.findAllByRole('link', { name: /dr sana malik/i })).length).toBeGreaterThan(0);
    expect(screen.getByRole('heading', { name: /^user operations$/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /^directory$/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /invite user/i })).toBeInTheDocument();
  });

  it('renders the quality analytics page and keeps the responsive chart container mounted', async () => {
    admin.getAdminQualityAnalyticsData.mockResolvedValue({
      freshness: {
        generatedAt: '2026-04-01T09:30:00.000Z',
        evaluationSampleCount: 10,
        reviewSampleCount: 5,
        windowDays: 30,
      },
      filters: {
        subtest: 'all',
        profession: 'all',
      },
      aiHumanAgreement: { value: 92, trend: 3 },
      appealsRate: { value: 4, trend: -1 },
      avgReviewTime: { value: 2.5, unit: 'hours' },
      reviewSLA: { metPercent: 97 },
      riskCases: { count: 1, severity: 'moderate' },
      contentPerformance: { publishedCount: 12, activeContent: 10 },
      featureAdoption: { adoptionRate: 75, activeUsers: 30 },
      trendSeries: {
        agreement: [{ label: 'Week 1', value: 90 }],
        appeals: [{ label: 'Week 1', value: 5 }],
        reviewTime: [{ label: 'Week 1', value: 2.5 }],
        riskCases: [{ label: 'Week 1', value: 1 }],
      },
    });

    renderPage(<QualityAnalyticsPage />);

    expect(await screen.findByRole('main', { name: /quality analytics/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /^quality analytics$/i })).toBeInTheDocument();
    expect(await screen.findAllByTestId('responsive-chart')).toHaveLength(2);
    expect(await screen.findAllByTestId('line-chart')).toHaveLength(2);
  });

  it('renders the content creation workspace inside learner-style editor surfaces', async () => {
    admin.getAdminCriteriaData.mockResolvedValue([
      {
        id: 'criterion-1',
        name: 'Purpose',
        type: 'writing',
        weight: 2,
        status: 'active',
        description: 'Checks whether the purpose is achieved clearly.',
      },
    ]);

    renderPage(<ContentNewPage />);

    expect(await screen.findByRole('main', { name: /content workspace/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /^create content$/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /core content metadata/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /save draft/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /publish/i })).toBeInTheDocument();
  });

  it('renders the content edit workspace inside learner-style editor surfaces and preserves editorial actions', async () => {
    navigation.params = { id: 'content-1' };
    admin.getAdminContentDetailData.mockResolvedValue({
      id: 'content-1',
      title: 'Sepsis Handover',
      contentType: 'writing_task',
      subtestCode: 'writing',
      professionId: 'nursing',
      difficulty: 'medium',
      estimatedDurationMinutes: 45,
      status: 'draft',
      sourceType: 'original',
      qaStatus: 'approved',
      description: 'Practice handover task.',
      caseNotes: 'Patient admitted with sepsis symptoms.',
      modelAnswer: 'Reference answer',
      criteriaFocus: ['criterion-1'],
      revisions: [],
    });
    admin.getAdminContentImpactData.mockResolvedValue({
      contentId: 'content-1',
      title: 'Sepsis Handover',
      status: 'draft',
      usage: {
        attemptCount: 18,
        evaluationCount: 12,
        studyPlanReferences: 4,
        activeAttempts: 2,
      },
      safeToArchive: false,
      safeToDelete: false,
    });
    admin.getAdminCriteriaData.mockResolvedValue([
      {
        id: 'criterion-1',
        name: 'Purpose',
        type: 'writing',
        weight: 2,
        status: 'active',
        description: 'Checks whether the purpose is achieved clearly.',
      },
    ]);

    renderPage(<ContentEditPage />);

    expect(await screen.findByRole('main', { name: /content workspace/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /edit sepsis handover/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /core content metadata/i })).toBeInTheDocument();
    expect(screen.getByText('18')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /revisions/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /save draft/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /publish/i })).toBeInTheDocument();
  });

  it('renders the user detail page inside the learner-style route surface', async () => {
    navigation.params = { id: 'user-1' };
    admin.getAdminUserDetailData.mockResolvedValue({
      id: 'user-1',
      name: 'Dr Sana Malik',
      email: 'sana@example.com',
      role: 'learner',
      status: 'active',
      authAccountId: 'auth-user-1',
      createdAt: '2026-03-01T10:00:00.000Z',
      availableActions: {
        canTriggerPasswordReset: true,
        canAdjustCredits: true,
        canSuspend: true,
        canDelete: true,
        canRestore: false,
      },
      tasksCompleted: 4,
      tasksGraded: 0,
      creditBalance: 2,
      lastLogin: '2026-04-01T08:00:00.000Z',
      profession: 'nursing',
      specialties: ['icu'],
    });

    renderPage(<UserDetailPage />);

    expect(await screen.findByRole('main', { name: /user operations detail/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /dr sana malik/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /identity/i })).toBeInTheDocument();
    expect(screen.getByText('icu')).toBeInTheDocument();
  });

  it('renders the revision history page inside the learner-style route surface', async () => {
    navigation.params = { id: 'content-1' };
    admin.getAdminContentRevisionData.mockResolvedValue([
      {
        id: 'revision-1',
        date: '2026-04-01T08:00:00.000Z',
        author: 'Editorial Ops',
        state: 'published',
        note: 'Published revision.',
      },
    ]);

    renderPage(<RevisionsPage />);

    expect(await screen.findByRole('main', { name: /revision history/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /^revision history$/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /saved revisions/i })).toBeInTheDocument();
    expect(screen.getAllByText(/published revision\./i).length).toBeGreaterThan(0);
  });
});
