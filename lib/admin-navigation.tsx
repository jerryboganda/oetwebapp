import type { MobileMenuSection, NavGroup, NavItem } from '@/components/layout';
import { AdminPermission } from '@/lib/admin-permissions';
import {
  Activity,
  Brain,
  Bell,
  BookOpen,
  BookOpenText,
  BrainCircuit,
  CalendarDays,
  Cog,
  Cpu,
  CreditCard,
  FileQuestion,
  Flag,
  Headphones,
  LayoutDashboard,
  Library,
  Mic,
  Rocket,
  Scale,
  Server,
  ShieldCheck,
  Sparkles,
  Target,
  TrendingUp,
  UserCog,
  Users,
  Webhook,
} from 'lucide-react';

export type AdminNavItem = NavItem & {
  requiredPermissions?: string[];
};

export type AdminNavGroup = Omit<NavGroup, 'items'> & {
  items: AdminNavItem[];
};

type AdminPageTitleRule = {
  prefix: string;
  title: string;
  exact?: boolean;
};

const iconClassName = 'w-5 h-5';

export const adminNavGroups: AdminNavGroup[] = [
  {
    label: 'Command Center',
    items: [
      { href: '/admin', label: 'Operations', icon: <LayoutDashboard className={iconClassName} />, exact: true },
      {
        href: '/admin/launch-readiness',
        label: 'Launch Readiness',
        icon: <Rocket className={iconClassName} />,
        matchPrefix: '/admin/launch-readiness',
        requiredPermissions: [AdminPermission.SystemAdmin],
      },
      {
        href: '/admin/alerts',
        label: 'Alerts',
        icon: <Bell className={iconClassName} />,
        matchPrefix: '/admin/alerts',
        requiredPermissions: [AdminPermission.SystemAdmin],
      },
      {
        href: '/admin/audit-logs',
        label: 'Audit Logs',
        icon: <ShieldCheck className={iconClassName} />,
        matchPrefix: '/admin/audit-logs',
        requiredPermissions: [AdminPermission.AuditLogs],
      },
    ],
  },
  {
    label: 'Content & Exams',
    items: [
      {
        href: '/admin/content',
        label: 'Content Home',
        icon: <Library className={iconClassName} />,
        matchPrefix: '/admin/content',
        requiredPermissions: [
          AdminPermission.ContentRead,
          AdminPermission.ContentWrite,
          AdminPermission.ContentPublish,
          AdminPermission.ContentEditorReview,
          AdminPermission.ContentPublisherApproval,
        ],
      },
      {
        href: '/admin/content/reading',
        label: 'Reading',
        icon: <BookOpen className={iconClassName} />,
        matchPrefix: '/admin/content/reading',
        requiredPermissions: [AdminPermission.ContentRead],
      },
      {
        href: '/admin/content/listening',
        label: 'Listening',
        icon: <Headphones className={iconClassName} />,
        matchPrefix: '/admin/content/listening',
        requiredPermissions: [AdminPermission.ContentRead],
      },
      {
        href: '/admin/writing',
        label: 'Writing',
        icon: <BookOpenText className={iconClassName} />,
        matchPrefix: '/admin/writing',
        requiredPermissions: [AdminPermission.ContentRead],
      },
      {
        href: '/admin/speaking',
        label: 'Speaking',
        icon: <Mic className={iconClassName} />,
        matchPrefix: '/admin/speaking',
        requiredPermissions: [
          AdminPermission.ContentRead,
          AdminPermission.ReviewOps,
          AdminPermission.QualityAnalytics,
          AdminPermission.ContentPublish,
        ],
      },
      {
        href: '/admin/content/mocks',
        label: 'Mocks',
        icon: <FileQuestion className={iconClassName} />,
        matchPrefix: '/admin/content/mocks',
        requiredPermissions: [AdminPermission.ContentRead],
      },
      {
        href: '/admin/recalls',
        label: 'Recalls',
        icon: <Brain className={iconClassName} />,
        matchPrefix: '/admin/recalls',
        requiredPermissions: [AdminPermission.ContentWrite],
      },
    ],
  },
  {
    label: 'Quality & Analytics',
    items: [
      {
        href: '/admin/review-ops',
        label: 'Review Ops',
        icon: <Activity className={iconClassName} />,
        matchPrefix: '/admin/review-ops',
        requiredPermissions: [AdminPermission.ReviewOps],
      },
      {
        href: '/admin/analytics/quality',
        label: 'Quality Analytics',
        icon: <TrendingUp className={iconClassName} />,
        matchPrefix: '/admin/analytics',
        requiredPermissions: [AdminPermission.QualityAnalytics],
      },
      {
        href: '/admin/criteria',
        label: 'Rubrics & Criteria',
        icon: <Target className={iconClassName} />,
        matchPrefix: '/admin/criteria',
        requiredPermissions: [AdminPermission.ContentRead],
      },
      {
        href: '/admin/rulebooks',
        label: 'Rulebooks',
        icon: <BookOpenText className={iconClassName} />,
        matchPrefix: '/admin/rulebooks',
        requiredPermissions: [AdminPermission.ContentRead],
      },
      {
        href: '/admin/escalations',
        label: 'Escalations',
        icon: <Scale className={iconClassName} />,
        matchPrefix: '/admin/escalations',
        requiredPermissions: [AdminPermission.SystemAdmin],
      },
    ],
  },
  {
    label: 'People & Access',
    items: [
      {
        href: '/admin/users',
        label: 'User Management',
        icon: <Users className={iconClassName} />,
        matchPrefix: '/admin/users',
        requiredPermissions: [AdminPermission.UsersRead],
      },
      {
        href: '/admin/readiness',
        label: 'Learner Readiness',
        icon: <TrendingUp className={iconClassName} />,
        matchPrefix: '/admin/readiness',
        requiredPermissions: [AdminPermission.LearnerRead],
      },
      {
        href: '/admin/policies/reading/users',
        label: 'Reading Overrides',
        icon: <UserCog className={iconClassName} />,
        matchPrefix: '/admin/policies/reading',
        requiredPermissions: [AdminPermission.UsersWrite],
      },
      {
        href: '/admin/community',
        label: 'Community',
        icon: <Users className={iconClassName} />,
        matchPrefix: '/admin/community',
        requiredPermissions: [AdminPermission.SystemAdmin],
      },
      {
        href: '/admin/study-plan-templates',
        label: 'Study Plan Templates',
        icon: <CalendarDays className={iconClassName} />,
        matchPrefix: '/admin/study-plan-templates',
        requiredPermissions: [AdminPermission.ContentRead],
      },
    ],
  },
  {
    label: 'AI & Automation',
    items: [
      {
        href: '/admin/ai-usage',
        label: 'AI Usage & Budget',
        icon: <Cpu className={iconClassName} />,
        matchPrefix: '/admin/ai-usage',
        requiredPermissions: [AdminPermission.AiConfig],
      },
      {
        href: '/admin/ai-config',
        label: 'AI Eval Config',
        icon: <BrainCircuit className={iconClassName} />,
        matchPrefix: '/admin/ai-config',
        requiredPermissions: [AdminPermission.AiConfig],
      },
      {
        href: '/admin/ai-providers',
        label: 'AI Providers',
        icon: <Server className={iconClassName} />,
        matchPrefix: '/admin/ai-providers',
        requiredPermissions: [AdminPermission.AiConfig],
      },
      {
        href: '/admin/voice-design',
        label: 'Voice Design',
        icon: <Sparkles className={iconClassName} />,
        matchPrefix: '/admin/voice-design',
        requiredPermissions: [AdminPermission.AiConfig],
      },
      {
        href: '/admin/notifications',
        label: 'Notifications',
        icon: <Bell className={iconClassName} />,
        matchPrefix: '/admin/notifications',
        requiredPermissions: [AdminPermission.SystemAdmin],
      },
      {
        href: '/admin/webhooks',
        label: 'Webhooks',
        icon: <Webhook className={iconClassName} />,
        matchPrefix: '/admin/webhooks',
        requiredPermissions: [AdminPermission.SystemAdmin],
      },
    ],
  },
  {
    label: 'Commerce & Settings',
    items: [
      {
        href: '/admin/billing',
        label: 'Billing Ops',
        icon: <CreditCard className={iconClassName} />,
        matchPrefix: '/admin/billing',
        requiredPermissions: [AdminPermission.BillingRead],
      },
      {
        href: '/admin/billing/wallet-tiers',
        label: 'Wallet Tiers',
        icon: <CreditCard className={iconClassName} />,
        matchPrefix: '/admin/billing/wallet-tiers',
        requiredPermissions: [AdminPermission.BillingRead],
      },
      {
        href: '/admin/freeze',
        label: 'Subscription Freezes',
        icon: <CreditCard className={iconClassName} />,
        matchPrefix: '/admin/freeze',
        requiredPermissions: [
          AdminPermission.BillingRead,
          AdminPermission.BillingWrite,
          AdminPermission.UsersRead,
          AdminPermission.UsersWrite,
        ],
      },
      {
        href: '/admin/signup-catalog',
        label: 'Signup Catalog',
        icon: <Library className={iconClassName} />,
        matchPrefix: '/admin/signup-catalog',
        requiredPermissions: [AdminPermission.ContentRead],
      },
      {
        href: '/admin/flags',
        label: 'Feature Flags',
        icon: <Flag className={iconClassName} />,
        matchPrefix: '/admin/flags',
        requiredPermissions: [AdminPermission.FeatureFlags],
      },
      {
        href: '/admin/settings',
        label: 'Runtime Settings',
        icon: <Cog className={iconClassName} />,
        matchPrefix: '/admin/settings',
        requiredPermissions: [AdminPermission.SystemAdmin],
      },
    ],
  },
];

export const adminNavItems: AdminNavItem[] = adminNavGroups.flatMap((group) => group.items);

export const adminMobileNavItems: AdminNavItem[] = [
  adminNavItems.find((item) => item.href === '/admin')!,
  adminNavItems.find((item) => item.href === '/admin/content')!,
  adminNavItems.find((item) => item.href === '/admin/review-ops')!,
  adminNavItems.find((item) => item.href === '/admin/users')!,
  adminNavItems.find((item) => item.href === '/admin/ai-usage')!,
  adminNavItems.find((item) => item.href === '/admin/billing')!,
];

export const adminMobileMenuSections: MobileMenuSection[] = adminNavGroups.map((group) => ({
  label: group.label,
  items: group.items,
}));

function normalizeAdminPath(pathname: string | null | undefined): string {
  if (!pathname) return '/admin';
  const withoutQuery = pathname.split('?')[0]?.split('#')[0] ?? pathname;
  return withoutQuery.length > 1 ? withoutQuery.replace(/\/+$/, '') : withoutQuery;
}

function matchesTitleRule(pathname: string, rule: AdminPageTitleRule): boolean {
  if (rule.exact) return pathname === rule.prefix;
  return pathname === rule.prefix || pathname.startsWith(`${rule.prefix}/`);
}

export function isContentWorkspace(pathname: string | null | undefined): boolean {
  const normalized = normalizeAdminPath(pathname);
  if (normalized === '/admin/content/new') return true;

  const hubSubRoutes = [
    'analytics',
    'conversation',
    'dedup',
    'generation',
    'grammar',
    'hierarchy',
    'import',
    'imports',
    'library',
    'listening',
    'media',
    'mocks',
    'papers',
    'pronunciation',
    'publish-requests',
    'quality',
    'reading',
    'result-templates',
    'scoring-system',
    'speaking',
    'strategies',
    'vocabulary',
    'writing',
  ];

  return Boolean(normalized.match(/^\/admin\/content\/[^/]+$/))
    && !hubSubRoutes.some((segment) => normalized.startsWith(`/admin/content/${segment}`));
}

const adminPageTitleRules: AdminPageTitleRule[] = [
  { prefix: '/admin', title: 'Operations', exact: true },
  { prefix: '/admin/content/reading', title: 'Reading' },
  { prefix: '/admin/content/listening', title: 'Listening' },
  { prefix: '/admin/content/mocks', title: 'Mocks' },
  { prefix: '/admin/content/writing', title: 'Writing' },
  { prefix: '/admin/content/speaking/shared-resources', title: 'Speaking Shared Resources' },
  { prefix: '/admin/content/scoring-system', title: 'Scoring System' },
  { prefix: '/admin/content/result-templates', title: 'Result Templates' },
  { prefix: '/admin/content/vocabulary/recall-set-tags', title: 'Recall Set Tags' },
  { prefix: '/admin/content/vocabulary', title: 'Vocabulary CMS' },
  { prefix: '/admin/content/conversation', title: 'Conversation CMS' },
  { prefix: '/admin/content/library', title: 'Content Library' },
  { prefix: '/admin/content/analytics', title: 'Content Analytics' },
  { prefix: '/admin/content/quality', title: 'Content Quality' },
  { prefix: '/admin/content/papers', title: 'Content Papers' },
  { prefix: '/admin/content/imports/real-content-folder', title: 'Real Content Import' },
  { prefix: '/admin/content/import', title: 'Content Import' },
  { prefix: '/admin/content/generation', title: 'Content Generation' },
  { prefix: '/admin/content/hierarchy', title: 'Content Hierarchy' },
  { prefix: '/admin/content/media', title: 'Media Assets' },
  { prefix: '/admin/content/dedup', title: 'Deduplication' },
  { prefix: '/admin/content/grammar', title: 'Grammar CMS' },
  { prefix: '/admin/content/pronunciation', title: 'Pronunciation CMS' },
  { prefix: '/admin/content/strategies', title: 'Strategy Guides' },
  { prefix: '/admin/content/publish-requests', title: 'Publish Requests' },
  { prefix: '/admin/content', title: 'Content Home' },
  { prefix: '/admin/writing/options', title: 'Writing AI Options' },
  { prefix: '/admin/writing/ai-draft', title: 'Writing AI Draft' },
  { prefix: '/admin/writing/analytics/rule-violations', title: 'Writing Rule Violations' },
  { prefix: '/admin/writing/analytics', title: 'Writing Analytics' },
  { prefix: '/admin/writing/calibration', title: 'Writing Calibration' },
  { prefix: '/admin/writing/audit', title: 'Writing Audit' },
  { prefix: '/admin/writing', title: 'Writing' },
  { prefix: '/admin/speaking/recordings/audit', title: 'Speaking Recording Audit' },
  { prefix: '/admin/speaking/result-visibility', title: 'Speaking Result Visibility' },
  { prefix: '/admin/speaking', title: 'Speaking' },
  { prefix: '/admin/analytics/reading', title: 'Reading Analytics' },
  { prefix: '/admin/analytics/listening', title: 'Listening Analytics' },
  { prefix: '/admin/analytics/subscription-health', title: 'Subscription Health' },
  { prefix: '/admin/analytics', title: 'Quality Analytics' },
  { prefix: '/admin/review-ops', title: 'Review Ops' },
  { prefix: '/admin/calibration/speaking', title: 'Speaking Calibration' },
  { prefix: '/admin/calibration', title: 'Calibration' },
  { prefix: '/admin/criteria', title: 'Rubrics & Criteria' },
  { prefix: '/admin/rulebooks', title: 'Rulebooks' },
  { prefix: '/admin/escalations', title: 'Escalations' },
  { prefix: '/admin/marketplace-review', title: 'Marketplace Review' },
  { prefix: '/admin/live-classes', title: 'Live Classes' },
  { prefix: '/admin/private-speaking', title: 'Private Speaking' },
  { prefix: '/admin/users', title: 'User Management' },
  { prefix: '/admin/experts', title: 'User Management' },
  { prefix: '/admin/permissions', title: 'User Management' },
  { prefix: '/admin/roles', title: 'User Management' },
  { prefix: '/admin/institutions', title: 'Institutions' },
  { prefix: '/admin/policies/reading', title: 'Reading Overrides' },
  { prefix: '/admin/readiness', title: 'Learner Readiness' },
  { prefix: '/admin/learners', title: 'Learners' },
  { prefix: '/admin/study-plan-templates', title: 'Study Plan Templates' },
  { prefix: '/admin/community', title: 'Community Moderation' },
  { prefix: '/admin/ai-assistant', title: 'AI Assistant' },
  { prefix: '/admin/ai-config', title: 'AI Eval Config' },
  { prefix: '/admin/ai-providers', title: 'AI Providers' },
  { prefix: '/admin/ai-usage', title: 'AI Usage & Budget' },
  { prefix: '/admin/ai-analytics', title: 'AI Analytics' },
  { prefix: '/admin/voice-design', title: 'Voice Design' },
  { prefix: '/admin/notifications/templates', title: 'Notification Templates' },
  { prefix: '/admin/notifications/campaigns', title: 'Notification Campaigns' },
  { prefix: '/admin/notifications', title: 'Notifications' },
  { prefix: '/admin/webhooks', title: 'Webhooks' },
  { prefix: '/admin/billing/wallet-tiers', title: 'Wallet Tiers' },
  { prefix: '/admin/billing', title: 'Billing Ops' },
  { prefix: '/admin/free-tier', title: 'Free Tier' },
  { prefix: '/admin/credit-lifecycle', title: 'Credit Lifecycle' },
  { prefix: '/admin/score-guarantee-claims', title: 'Score Guarantee Claims' },
  { prefix: '/admin/fx-rates', title: 'FX Rates' },
  { prefix: '/admin/pricing-experiments', title: 'Pricing Experiments' },
  { prefix: '/admin/freeze', title: 'Subscription Freezes' },
  { prefix: '/admin/signup-catalog', title: 'Signup Catalog' },
  { prefix: '/admin/flags', title: 'Feature Flags' },
  { prefix: '/admin/settings', title: 'Runtime Settings' },
  { prefix: '/admin/audit-logs', title: 'Audit Logs' },
  { prefix: '/admin/launch-readiness', title: 'Launch Readiness' },
  { prefix: '/admin/alerts', title: 'Alerts' },
  { prefix: '/admin/business-intelligence', title: 'Business Intelligence' },
  { prefix: '/admin/bulk-operations', title: 'Bulk Operations' },
  { prefix: '/admin/enterprise', title: 'Enterprise' },
  { prefix: '/admin/playbook', title: 'Playbook' },
  { prefix: '/admin/recalls', title: 'Recalls' },
  { prefix: '/admin/sla-health', title: 'SLA Health' },
  { prefix: '/admin/taxonomy', title: 'Professions' },
];

export function getAdminPageTitle(pathname: string | null | undefined): string | undefined {
  const normalized = normalizeAdminPath(pathname);

  if (isContentWorkspace(normalized)) return 'Content Workspace';
  if (normalized.startsWith('/admin/content/') && normalized.endsWith('/revisions')) return 'Revision History';

  return adminPageTitleRules.find((rule) => matchesTitleRule(normalized, rule))?.title;
}