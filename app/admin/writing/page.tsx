'use client';

/**
 * Admin · Writing hub.
 *
 * Landing page for the OET Writing exam-closure admin surface. Surfaces the
 * authoring, analytics, and policy tools as navigation cards so each is
 * reachable in one click. The individual pages enforce their own permission
 * gates; this hub is intentionally a simple, link-only index.
 */

import { useMemo } from 'react';
import {
  BarChart3,
  Cpu,
  Eye,
  FilePlus2,
  ListChecks,
  PenSquare,
  Sparkles,
} from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { AdminHubSection, type AdminHubLink } from '@/components/admin/ui/hub-card';
import { canAccessAdminRoute } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';

type WritingHubSection = {
  title: string;
  description: string;
  links: AdminHubLink[];
};

const AUTHORING_LINKS: AdminHubLink[] = [
  {
    href: '/admin/writing/tasks/new',
    title: 'Create task',
    description: 'Author a new OET Writing task with a prompt, fixed instructions, and optional stimulus PDF.',
    icon: <FilePlus2 className="h-5 w-5" />,
  },
  {
    href: '/admin/writing/tasks',
    title: 'Task library',
    description: 'Browse, edit, clone, publish, and export existing Writing tasks.',
    icon: <ListChecks className="h-5 w-5" />,
  },
];

const QUALITY_LINKS: AdminHubLink[] = [
  {
    href: '/admin/writing/analytics',
    title: 'Analytics',
    description: 'Outcome, quality, and integrity telemetry with rule-violation drill-down.',
    icon: <BarChart3 className="h-5 w-5" />,
  },
  {
    href: '/admin/writing/result-visibility',
    title: 'Result visibility',
    description: 'Control when graded results are released to candidates.',
    icon: <Eye className="h-5 w-5" />,
  },
];

const AI_LINKS: AdminHubLink[] = [
  {
    href: '/admin/writing/options',
    title: 'AI options',
    description: 'Kill switches for AI grading and coach, plus free-tier entitlement.',
    icon: <Cpu className="h-5 w-5" />,
  },
  {
    href: '/admin/writing/ai-draft',
    title: 'AI draft',
    description: 'Configure the AI draft-generation pipeline for Writing.',
    icon: <Sparkles className="h-5 w-5" />,
  },
];

const writingHubSections: WritingHubSection[] = [
  { title: 'Authoring workspace', description: 'Create and manage Writing tasks.', links: AUTHORING_LINKS },
  { title: 'Quality & release', description: 'Monitor exam integrity and control how results reach candidates.', links: QUALITY_LINKS },
  { title: 'AI assistance', description: 'AI grading, coaching, and draft generation.', links: AI_LINKS },
];

export default function AdminWritingHubPage() {
  const { user } = useCurrentUser();
  const userPermissions = user?.adminPermissions;
  const visibleSections = useMemo(
    () =>
      writingHubSections
        .map((section) => ({
          ...section,
          links: section.links.filter((link) => canAccessAdminRoute(userPermissions, link.href)),
        }))
        .filter((section) => section.links.length > 0),
    [userPermissions],
  );

  return (
    <AdminCatalogLayout
      title="Writing"
      description="Author Writing tasks, review integrity analytics, and manage result-release policy."
      eyebrow="Writing"
      icon={<PenSquare className="h-5 w-5" />}
      breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Writing' }]}
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
            columns="two"
          />
        ))
      ) : (
        <EmptyState
          title="No available Writing workflows"
          description="Your admin account does not currently have permission to open any Writing workflows."
        />
      )}
    </AdminCatalogLayout>
  );
}
