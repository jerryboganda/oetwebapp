'use client';

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useMemo } from 'react';
import {
  ArrowRight,
  BarChart3,
  BookOpenText,
  Copy,
  FileSearch,
  FileCheck2,
  GitBranch,
  Headphones,
  Image as ImageIcon,
  Library,
  MessageSquareText,
  Mic,
  PenSquare,
  ScrollText,
  Sparkles,
  Upload,
  Users,
} from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { hasPermission, sidebarPermissionMap } from '@/lib/admin-permissions';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';

type HubLink = {
  href: string;
  label: string;
  description: string;
  icon: React.ReactNode;
  badge?: string;
};

type HubSection = {
  id: string;
  title: string;
  description: string;
  links: HubLink[];
};

const hubSections: HubSection[] = [
  {
    id: 'papers',
    title: 'Papers (canonical content)',
    description:
      'The mission-critical ContentPaper → Asset → MediaAsset model. All Reading, Listening, Writing, and Speaking practice papers are authored, versioned, and published from here.',
    links: [
      {
        href: '/admin/content/library',
        label: 'Content Library',
        description: 'Browse, search, edit, and publish every published or draft content item across professions.',
        icon: <Library className="h-5 w-5" />,
      },
      {
        href: '/admin/content/papers',
        label: 'Content Papers',
        description: 'Canonical paper records with typed asset slots (case notes, audio, scripts, role cards).',
        icon: <FileCheck2 className="h-5 w-5" />,
        badge: 'Canonical',
      },
      {
        href: '/admin/content/mocks',
        label: 'Full Mocks',
        description: 'Bundle Listening + Reading + Writing + Speaking into a complete OET mock paper.',
        icon: <ScrollText className="h-5 w-5" />,
      },
      {
        href: '/admin/content/analytics',
        label: 'Item Analytics',
        description: 'Deep-dive into per-item usage, completion rates, and learner outcomes.',
        icon: <BarChart3 className="h-5 w-5" />,
      },
    ],
  },
  {
    id: 'lessons',
    title: 'Lessons & micro-content',
    description: 'Authoring surfaces for the supporting modules that wrap practice papers — vocabulary, conversation, grammar, pronunciation, strategies.',
    links: [
      {
        href: '/admin/content/vocabulary',
        label: 'Vocabulary',
        description: 'Term banks tagged by profession with examples and audio.',
        icon: <BookOpenText className="h-5 w-5" />,
      },
      {
        href: '/admin/content/conversation',
        label: 'Conversation Templates',
        description: 'Role-play scenarios, objectives, and patient context for AI-led conversation practice.',
        icon: <MessageSquareText className="h-5 w-5" />,
      },
      {
        href: '/admin/content/grammar',
        label: 'Grammar Lessons',
        description: 'Server-authoritative grammar rulebook content (free tier capped, AI-drafted).',
        icon: <PenSquare className="h-5 w-5" />,
      },
      {
        href: '/admin/content/pronunciation',
        label: 'Pronunciation Drills',
        description: 'Phonemes, example words, sentences and ASR-graded drills.',
        icon: <Mic className="h-5 w-5" />,
      },
      {
        href: '/admin/content/strategies',
        label: 'Strategy Guides',
        description: 'How-to guides per skill and band level.',
        icon: <Headphones className="h-5 w-5" />,
      },
    ],
  },
  {
    id: 'pipelines',
    title: 'Pipelines',
    description: 'Bring content into the system at scale, draft new items with AI, and keep the catalogue organised.',
    links: [
      {
        href: '/admin/content/import',
        label: 'Bulk Import',
        description: 'CSV / ZIP imports with required source provenance and audit trail.',
        icon: <Upload className="h-5 w-5" />,
      },
      {
        href: '/admin/content/papers/import',
        label: 'Paper ZIP Import',
        description: 'Mission-critical chunked ZIP import that maps files to typed paper assets.',
        icon: <Upload className="h-5 w-5" />,
        badge: 'Canonical',
      },
      {
        href: '/admin/content/generation',
        label: 'AI Generation',
        description: 'Grounded AI drafts (rulebook + scoring + guardrails) routed via the AI gateway.',
        icon: <Sparkles className="h-5 w-5" />,
      },
      {
        href: '/admin/content/hierarchy',
        label: 'Hierarchy',
        description: 'Programs → Tracks → Modules → Lessons → Packages.',
        icon: <GitBranch className="h-5 w-5" />,
      },
      {
        href: '/admin/content/media',
        label: 'Media Assets',
        description: 'Content-addressed (SHA-256) storage of every uploaded file.',
        icon: <ImageIcon className="h-5 w-5" />,
      },
    ],
  },
  {
    id: 'governance',
    title: 'Quality & governance',
    description: 'Pre-publish checks, approvals, and ongoing quality control for the entire catalogue.',
    links: [
      {
        href: '/admin/content/publish-requests',
        label: 'Publish Requests',
        description: 'Approval queue for content moving from draft to published.',
        icon: <FileCheck2 className="h-5 w-5" />,
      },
      {
        href: '/admin/content/quality',
        label: 'Quality Review',
        description: 'Review automated QA status for recent content before human review and publishing.',
        icon: <FileSearch className="h-5 w-5" />,
      },
      {
        href: '/admin/content/dedup',
        label: 'Deduplication',
        description: 'Detect and merge near-duplicate items before they reach learners.',
        icon: <Copy className="h-5 w-5" />,
      },
      {
        href: '/admin/rulebooks',
        label: 'Rulebooks',
        description: 'The single source of truth that grounds every grade and AI prompt.',
        icon: <BookOpenText className="h-5 w-5" />,
      },
      {
        href: '/admin/criteria',
        label: 'Rubrics & Criteria',
        description: 'Writing and Speaking criterion definitions per profession.',
        icon: <ScrollText className="h-5 w-5" />,
      },
      {
        href: '/admin/signup-catalog',
        label: 'Signup Catalog & Professions',
        description: 'Single source of truth for the profession registry that tags every paper, lesson, and rubric.',
        icon: <Users className="h-5 w-5" />,
      },
    ],
  },
];

function canAccessHubHref(href: string, userPermissions: string[] | null | undefined) {
  const required = sidebarPermissionMap[href];
  return !required || hasPermission(userPermissions, ...required);
}

export default function AdminContentHubPage() {
  const router = useRouter();
  const { isAuthenticated, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const userPermissions = user?.adminPermissions;

  const visibleHubSections = useMemo(
    () =>
      hubSections
        .map((section) => ({
          ...section,
          links: section.links.filter((link) => canAccessHubHref(link.href, userPermissions)),
        }))
        .filter((section) => section.links.length > 0),
    [userPermissions],
  );

  const canOpenLibrary = canAccessHubHref('/admin/content/library', userPermissions);
  const canCreateContent = canAccessHubHref('/admin/content/new', userPermissions);
  const headerActions = canOpenLibrary || canCreateContent ? (
    <div className="flex flex-wrap gap-2">
      {canOpenLibrary ? (
        <Button onClick={() => router.push('/admin/content/library')} variant="outline" className="gap-2">
          <Library className="h-4 w-4" /> Open Library
        </Button>
      ) : null}
      {canCreateContent ? (
        <Button onClick={() => router.push('/admin/content/new')} className="gap-2">
          <PenSquare className="h-4 w-4" /> New Content
        </Button>
      ) : null}
    </div>
  ) : undefined;

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Content Hub">
      <AdminRouteSectionHeader
        title="Content Hub"
        description="Single front-door for every content workflow: papers, lessons, mocks, imports, AI drafts, hierarchy, media, and governance."
        actions={headerActions}
      />

      {visibleHubSections.length > 0 ? visibleHubSections.map((section) => (
        <AdminRoutePanel key={section.id} title={section.title} description={section.description}>
          <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
            {section.links.map((link) => (
              <Link
                key={link.href}
                href={link.href}
                className="group flex h-full flex-col justify-between gap-3 rounded-2xl border border-border bg-background-light p-4 transition hover:-translate-y-0.5 hover:border-primary hover:shadow-sm"
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="flex items-center gap-3">
                    <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-primary/10 text-primary">
                      {link.icon}
                    </span>
                    <div>
                      <p className="font-semibold text-navy">{link.label}</p>
                      {link.badge ? (
                        <Badge variant="success" className="mt-1 text-[10px] uppercase tracking-wide">
                          {link.badge}
                        </Badge>
                      ) : null}
                    </div>
                  </div>
                  <ArrowRight className="h-4 w-4 text-muted transition group-hover:text-primary" />
                </div>
                <p className="text-sm text-muted">{link.description}</p>
              </Link>
            ))}
          </div>
        </AdminRoutePanel>
      )) : (
        <AdminRoutePanel
          title="No available content workflows"
          description="Your admin account does not currently have permission to open any content workflows."
        >
          <div />
        </AdminRoutePanel>
      )}
    </AdminRouteWorkspace>
  );
}
