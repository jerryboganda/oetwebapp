'use client';

import { useMemo } from 'react';
import { BarChart3, Eye, FileSearch, Mic, MessageSquareText, NotebookPen } from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { AdminHubSection, type AdminHubLink } from '@/components/admin/ui/hub-card';
import { canAccessAdminRoute } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';

type SpeakingHubSection = {
  title: string;
  description: string;
  links: AdminHubLink[];
};

const operationsLinks: AdminHubLink[] = [
  {
    href: '/admin/speaking/result-visibility',
    title: 'Result visibility',
    description: 'Choose exactly which Speaking result fields learners can see, from submission receipt to drills and reattempts.',
    icon: <Eye className="h-5 w-5" />,
    badge: 'Policy',
    badgeVariant: 'info',
  },
  {
    href: '/admin/analytics/speaking',
    title: 'Speaking analytics',
    description: 'Review class, tutor-consistency, and content-difficulty metrics for the Speaking module.',
    icon: <BarChart3 className="h-5 w-5" />,
    badge: 'Insights',
    badgeVariant: 'success',
  },
  {
    href: '/admin/speaking/recordings/audit',
    title: 'Recording audit',
    description: 'Inspect access patterns and audit trails for learner speaking recordings.',
    icon: <FileSearch className="h-5 w-5" />,
    badge: 'Audit',
    badgeVariant: 'warning',
  },
];

const contentLinks: AdminHubLink[] = [
  {
    href: '/admin/content/papers?subtest=speaking',
    title: 'Speaking authoring',
    description: 'Open the role-play card authoring surface for candidate, interlocutor, and source assets.',
    icon: <Mic className="h-5 w-5" />,
    badge: 'Content',
    badgeVariant: 'primary',
  },
  {
    href: '/admin/content/speaking/mock-sets',
    title: 'Mock sets',
    description: 'Bundle published role-plays into the official Speaking mock shape.',
    icon: <MessageSquareText className="h-5 w-5" />,
    badge: 'OET',
    badgeVariant: 'primary',
  },
];

const assetLinks: AdminHubLink[] = [
  {
    href: '/admin/content/speaking/shared-resources',
    title: 'Shared resources',
    description: 'Manage shared warm-up prompts and assessment-criteria PDFs used across Speaking cards.',
    icon: <NotebookPen className="h-5 w-5" />,
    badge: 'Assets',
    badgeVariant: 'info',
  },
];

const speakingHubSections: SpeakingHubSection[] = [
  { title: 'Operations & quality', description: 'Release policy, performance signals, and audit tools.', links: operationsLinks },
  { title: 'Content authoring', description: 'Role-play authoring and Speaking mock composition.', links: contentLinks },
  { title: 'Shared assets', description: 'Common resources reused across Speaking cards.', links: assetLinks },
];

export default function AdminSpeakingPage() {
  const { user } = useCurrentUser();
  const userPermissions = user?.adminPermissions;
  const visibleSections = useMemo(
    () =>
      speakingHubSections
        .map((section) => ({
          ...section,
          links: section.links.filter((link) => canAccessAdminRoute(userPermissions, link.href)),
        }))
        .filter((section) => section.links.length > 0),
    [userPermissions],
  );

  return (
    <AdminCatalogLayout
      title="Speaking"
      description="Operational hub for Speaking result visibility, analytics, recording audits, and the content surfaces that feed the learner experience."
      eyebrow="Admin"
      hideViewModeToggle
      itemsClassName="flex flex-col gap-6"
    >
      {visibleSections.length > 0 ? (
        visibleSections.map((section) => (
          <AdminHubSection
            key={section.title}
            title={section.title}
            description={section.description}
            links={section.links}
            columns={section.links.length > 2 ? 'three' : 'two'}
          />
        ))
      ) : (
        <EmptyState
          title="No available Speaking workflows"
          description="Your admin account does not currently have permission to open any Speaking workflows."
        />
      )}
    </AdminCatalogLayout>
  );
}