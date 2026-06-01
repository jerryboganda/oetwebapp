'use client';

import { useRouter } from 'next/navigation';
import { useMemo } from 'react';
import {
  BarChart3,
  BookOpenText,
  Calculator,
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
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { AdminHubSection, type AdminHubLink } from '@/components/admin/ui/hub-card';
import { hasPermission, sidebarPermissionMap } from '@/lib/admin-permissions';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';

type HubSection = {
  id: string;
  title: string;
  description: string;
  links: AdminHubLink[];
  columns?: 'two' | 'three' | 'five';
};

const hubSections: HubSection[] = [
  {
    id: 'subtests',
    title: 'OET subtest workspaces',
    description: 'Start with the module you are operating. Each hub gathers the authoring, review, analytics, and support routes for that subtest.',
    columns: 'five',
    links: [
      { href: '/admin/content/reading', title: 'Reading', description: 'Author, validate, and publish Reading papers, texts, questions, previews, and extraction workflows.', icon: <BookOpenText className="h-5 w-5" />, badge: 'Subtest', badgeVariant: 'primary' },
      { href: '/admin/content/listening', title: 'Listening', description: 'Manage Listening papers, structure, audio imports, sequencing, and question-level edits.', icon: <Headphones className="h-5 w-5" />, badge: 'Subtest', badgeVariant: 'primary' },
      { href: '/admin/writing', title: 'Writing', description: 'Create Writing tasks, manage task libraries, release policy, analytics, and AI-assisted workflows.', icon: <PenSquare className="h-5 w-5" />, badge: 'Subtest', badgeVariant: 'primary' },
      { href: '/admin/speaking', title: 'Speaking', description: 'Operate Speaking visibility, analytics, recordings, role-play authoring, mock sets, and shared resources.', icon: <Mic className="h-5 w-5" />, badge: 'Subtest', badgeVariant: 'primary' },
      { href: '/admin/content/mocks', title: 'Mocks', description: 'Bundle subtest content into complete mocks, run operations, inspect leaks, and review item analysis.', icon: <ScrollText className="h-5 w-5" />, badge: 'Bundle', badgeVariant: 'primary' },
    ],
  },
  {
    id: 'assets',
    title: 'Library & learning assets',
    description: 'Shared assets that support every subtest: paper records, lessons, media, vocabulary, conversation, grammar, pronunciation, and strategies.',
    links: [
      { href: '/admin/content/library', title: 'Content Library', description: 'Browse, search, edit, and publish every published or draft item across professions.', icon: <Library className="h-5 w-5" /> },
      { href: '/admin/content/papers', title: 'Content Papers', description: 'Canonical paper records with typed asset slots for case notes, audio, scripts, and role cards.', icon: <FileCheck2 className="h-5 w-5" />, badge: 'Canonical', badgeVariant: 'success' },
      { href: '/admin/content/vocabulary', title: 'Vocabulary', description: 'Term banks tagged by profession with examples and audio.', icon: <BookOpenText className="h-5 w-5" /> },
      { href: '/admin/content/conversation', title: 'Conversation Templates', description: 'Role-play scenarios, objectives, and patient context for AI-led conversation practice.', icon: <MessageSquareText className="h-5 w-5" /> },
      { href: '/admin/content/grammar', title: 'Grammar Lessons', description: 'Server-authoritative grammar rulebook content, free-tier gates, and AI-drafted lesson structure.', icon: <PenSquare className="h-5 w-5" /> },
      { href: '/admin/content/pronunciation', title: 'Pronunciation Drills', description: 'Phonemes, example words, sentences, and ASR-graded drills.', icon: <Mic className="h-5 w-5" /> },
      { href: '/admin/content/strategies', title: 'Strategy Guides', description: 'How-to guides per skill and band level.', icon: <Headphones className="h-5 w-5" /> },
      { href: '/admin/content/media', title: 'Media Assets', description: 'Content-addressed storage for every uploaded source and learner-facing file.', icon: <ImageIcon className="h-5 w-5" /> },
    ],
  },
  {
    id: 'automation',
    title: 'Import & automation',
    description: 'Bring content into the system at scale, draft new items with AI, and keep the catalogue organised.',
    links: [
      { href: '/admin/content/import', title: 'Bulk Import', description: 'CSV and ZIP imports with required source provenance and audit trail.', icon: <Upload className="h-5 w-5" /> },
      { href: '/admin/content/papers/import', title: 'Paper ZIP Import', description: 'Mission-critical chunked ZIP import that maps files to typed paper assets.', icon: <Upload className="h-5 w-5" />, badge: 'Canonical', badgeVariant: 'success' },
      { href: '/admin/content/imports/real-content-folder', title: 'Real Content Folder Import', description: 'Upload the Project Real Content folder ZIP, review parsed proposals, and commit drafts.', icon: <Upload className="h-5 w-5" />, badge: 'OET', badgeVariant: 'primary' },
      { href: '/admin/content/generation', title: 'AI Generation', description: 'Grounded AI drafts routed via the AI gateway with rulebook and scoring guardrails.', icon: <Sparkles className="h-5 w-5" /> },
      { href: '/admin/content/hierarchy', title: 'Hierarchy', description: 'Programs, tracks, modules, lessons, and packages.', icon: <GitBranch className="h-5 w-5" /> },
    ],
  },
  {
    id: 'governance',
    title: 'Quality & governance',
    description: 'Pre-publish checks, approvals, scoring references, and ongoing quality control for the whole catalogue.',
    links: [
      { href: '/admin/content/publish-requests', title: 'Publish Requests', description: 'Approval queue for content moving from draft to published.', icon: <FileCheck2 className="h-5 w-5" /> },
      { href: '/admin/content/quality', title: 'Quality Review', description: 'Review automated QA status for recent content before human review and publishing.', icon: <FileSearch className="h-5 w-5" /> },
      { href: '/admin/content/dedup', title: 'Deduplication', description: 'Detect and merge near-duplicate items before they reach learners.', icon: <Copy className="h-5 w-5" /> },
      { href: '/admin/rulebooks', title: 'Rulebooks', description: 'The single source of truth that grounds every grade and AI prompt.', icon: <BookOpenText className="h-5 w-5" /> },
      { href: '/admin/content/scoring-system', title: 'Scoring System', description: 'Edit the learner-facing scoring policy reference and structured threshold document.', icon: <Calculator className="h-5 w-5" /> },
      { href: '/admin/content/result-templates', title: 'Result Templates', description: 'Upload and activate OET-style score-report images for learner mock-result pages.', icon: <ImageIcon className="h-5 w-5" /> },
      { href: '/admin/criteria', title: 'Rubrics & Criteria', description: 'Writing and Speaking criterion definitions per profession.', icon: <ScrollText className="h-5 w-5" /> },
      { href: '/admin/signup-catalog', title: 'Signup Catalog & Professions', description: 'Single source of truth for the profession registry that tags every paper, lesson, and rubric.', icon: <Users className="h-5 w-5" /> },
      { href: '/admin/content/analytics', title: 'Item Analytics', description: 'Deep-dive into per-item usage, completion rates, and learner outcomes.', icon: <BarChart3 className="h-5 w-5" /> },
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
    <>
      {canOpenLibrary ? (
        <Button variant="outline" onClick={() => router.push('/admin/content/library')} startIcon={<Library className="h-4 w-4" />}>
          Open Library
        </Button>
      ) : null}
      {canCreateContent ? (
        <Button onClick={() => router.push('/admin/content/new')} startIcon={<PenSquare className="h-4 w-4" />}>
          New Content
        </Button>
      ) : null}
    </>
  ) : undefined;

  if (!isAuthenticated || role !== 'admin') return null;

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content' },
  ];

  return (
    <AdminCatalogLayout
      title="Content Hub"
      description="Single front-door for every content workflow: papers, lessons, mocks, imports, AI drafts, hierarchy, media, and governance."
      breadcrumbs={breadcrumbs}
      actions={headerActions}
      hideViewModeToggle
      itemsClassName="flex flex-col gap-6"
    >
      {visibleHubSections.length > 0 ? (
        visibleHubSections.map((section) => (
          <AdminHubSection
            key={section.id}
            title={section.title}
            description={section.description}
            links={section.links}
            columns={section.columns}
          />
        ))
      ) : (
        <EmptyState
          title="No available content workflows"
          description="Your admin account does not currently have permission to open any content workflows."
        />
      )}
    </AdminCatalogLayout>
  );
}
