'use client';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { MotionItem, MotionSection } from "@/components/ui/motion-primitives";
import { ProgressBar } from "@/components/ui/progress";
import { Skeleton } from "@/components/ui/skeleton";
import { analytics } from '@/lib/analytics';
import { fetchStrategyGuide, isApiError, setStrategyGuideBookmark, updateStrategyGuideProgress } from '@/lib/api';
import type {
    StrategyGuideDetail,
    StrategyGuideStructuredContent,
    StrategyGuideStructuredSection
} from '@/lib/types/strategies';
import { cn } from '@/lib/utils';
import {
    ArrowLeft,
    ArrowRight,
    Bookmark,
    CheckCircle2,
    ChevronLeft,
    ChevronRight,
    Clock,
    Lightbulb,
    Lock,
    Sparkles
} from 'lucide-react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { useEffect, useMemo, useState } from 'react';

function formatLabel(value: string | null | undefined) {
  if (!value) return 'General';
  return value
    .split(/[_-]/g)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

function decodeHtmlEntities(value: string) {
  return value
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'");
}

function htmlToParagraphs(html: string | null) {
  if (!html) return [];

  return decodeHtmlEntities(
    html
      .replace(/<br\s*\/?>/gi, '\n')
      .replace(/<\/(p|div|h[1-6]|li)>/gi, '\n')
      .replace(/<li[^>]*>/gi, '- ')
      .replace(/<[^>]+>/g, ' '),
  )
    .split('\n')
    .map((line) => line.replace(/\s+/g, ' ').trim())
    .filter(Boolean);
}

function normaliseSections(raw: unknown): StrategyGuideStructuredSection[] {
  if (!Array.isArray(raw)) return [];

  const sections: StrategyGuideStructuredSection[] = [];
  for (const section of raw) {
    if (!section || typeof section !== 'object') continue;
    const record = section as Record<string, unknown>;
    const bullets = Array.isArray(record.bullets)
      ? record.bullets.filter((item): item is string => typeof item === 'string' && item.trim().length > 0)
      : [];

    const candidate: StrategyGuideStructuredSection = {
      heading: typeof record.heading === 'string' ? record.heading : undefined,
      body: typeof record.body === 'string' ? record.body : undefined,
      bullets,
    };

    if (candidate.heading || candidate.body || (candidate.bullets && candidate.bullets.length)) {
      sections.push(candidate);
    }
  }
  return sections;
}

function parseStructuredContent(json: string | null): StrategyGuideStructuredContent | null {
  if (!json) return null;

  try {
    const parsed = JSON.parse(json) as Record<string, unknown>;
    if (!parsed || typeof parsed !== 'object') return null;

    return {
      version: typeof parsed.version === 'number' ? parsed.version : undefined,
      overview: typeof parsed.overview === 'string' ? parsed.overview : undefined,
      sections: normaliseSections(parsed.sections),
      keyTakeaways: Array.isArray(parsed.keyTakeaways)
        ? parsed.keyTakeaways.filter((item): item is string => typeof item === 'string' && item.trim().length > 0)
        : [],
    };
  } catch {
    return null;
  }
}

function LoadingState() {
  return (
    <LearnerDashboardShell>
      <div className="space-y-5">
        <Skeleton className="h-36 rounded-[24px]" />
        <Skeleton className="h-96 rounded-2xl" />
      </div>
    </LearnerDashboardShell>
  );
}

function DisabledState() {
  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Strategy guide unavailable"
        description="This learner strategy guide is behind a release flag right now."
        icon={Lightbulb}
        accent="amber"
        highlights={[
          { label: 'Status', value: 'Coming soon', icon: Sparkles },
          { label: 'Access', value: 'Learner only', icon: Lock },
        ]}
      />
      <Link href="/strategies" className="mt-5 inline-flex min-h-11 items-center gap-2 rounded-lg border border-border px-4 py-2 text-sm font-semibold text-navy hover:bg-surface">
        <ArrowLeft className="h-4 w-4" aria-hidden="true" />
        Back to strategies
      </Link>
    </LearnerDashboardShell>
  );
}

export default function StrategyGuidePage() {
  const params = useParams<{ id?: string | string[] }>();
  const guideId = useMemo(() => {
    const value = params?.id;
    if (typeof value === 'string') return value;
    if (Array.isArray(value)) return value[0] ?? null;
    return null;
  }, [params]);

  const [guide, setGuide] = useState<StrategyGuideDetail | null>(null);
  const [content, setContent] = useState<StrategyGuideStructuredContent | null>(null);
  const [fallbackParagraphs, setFallbackParagraphs] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [disabled, setDisabled] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [bookmarking, setBookmarking] = useState(false);
  const [completing, setCompleting] = useState(false);

  useEffect(() => {
    let cancelled = false;

    async function loadGuide(id: string) {
      setLoading(true);
      setError(null);
      setActionError(null);
      setDisabled(false);

      try {
        const data = await fetchStrategyGuide(id);
        if (cancelled) return;

        const parsed = parseStructuredContent(data.contentJson);
        setGuide(data);
        setContent(parsed);
        setFallbackParagraphs(parsed ? [] : htmlToParagraphs(data.contentHtml));
        setLoading(false);
        analytics.track('strategy_guide_viewed', { guideId: data.id });

        const canTrackProgress = data.isAccessible || data.isPreviewEligible;
        if (canTrackProgress && data.progress.readPercent < 15) {
          updateStrategyGuideProgress(data.id, 15)
            .then((result) => {
              if (cancelled) return;
              setGuide((current) => current ? { ...current, progress: result.progress, bookmarked: result.progress.bookmarked } : current);
            })
            .catch(() => undefined);
        }
      } catch (err) {
        if (cancelled) return;
        if (isApiError(err) && err.code === 'FEATURE_DISABLED') {
          setDisabled(true);
        } else {
          setError(isApiError(err) ? err.userMessage : 'Unable to load this strategy guide.');
        }
        setLoading(false);
      }
    }

    if (!guideId) {
      setError('Strategy guide route is missing an id.');
      setLoading(false);
      return () => {
        cancelled = true;
      };
    }

    void loadGuide(guideId);

    return () => {
      cancelled = true;
    };
  }, [guideId]);

  async function toggleBookmark() {
    if (!guide) return;
    setBookmarking(true);
    setActionError(null);

    try {
      const result = await setStrategyGuideBookmark(guide.id, !guide.bookmarked);
      setGuide((current) => current ? { ...current, progress: result.progress, bookmarked: result.progress.bookmarked } : current);
    } catch (err) {
      setActionError(isApiError(err) ? err.userMessage : 'Unable to update this bookmark.');
    } finally {
      setBookmarking(false);
    }
  }

  async function markComplete() {
    if (!guide) return;
    setCompleting(true);
    setActionError(null);

    try {
      const result = await updateStrategyGuideProgress(guide.id, 100);
      setGuide((current) => current ? { ...current, progress: result.progress, bookmarked: result.progress.bookmarked } : current);
    } catch (err) {
      setActionError(isApiError(err) ? err.userMessage : 'Unable to update reading progress.');
    } finally {
      setCompleting(false);
    }
  }

  if (loading) {
    return <LoadingState />;
  }

  if (disabled) {
    return <DisabledState />;
  }

  if (error || !guide) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="error" title="Strategy guide did not load">{error ?? 'Strategy guide not found.'}</InlineAlert>
        <Link href="/strategies" className="mt-5 inline-flex min-h-11 items-center gap-2 rounded-lg border border-border px-4 py-2 text-sm font-semibold text-navy hover:bg-surface">
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          Back to strategies
        </Link>
      </LearnerDashboardShell>
    );
  }

  const locked = !guide.isAccessible && guide.requiresUpgrade && !guide.isPreviewEligible;
  const readable = guide.isAccessible || guide.isPreviewEligible;
  const sections = content?.sections ?? [];
  const takeaways = content?.keyTakeaways ?? [];

  return (
    <LearnerDashboardShell>
      <div className="space-y-7">
        <Link href="/strategies" className="inline-flex min-h-11 items-center gap-2 rounded-lg border border-border px-4 py-2 text-sm font-semibold text-navy hover:bg-surface">
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          Back to strategies
        </Link>

        <LearnerPageHero
          title={guide.title}
          description={guide.summary ?? 'A guided OET strategy article for learner practice.'}
          icon={Lightbulb}
          accent="amber"
          highlights={[
            { label: 'Subtest', value: formatLabel(guide.subtestCode), icon: CheckCircle2 },
            { label: 'Category', value: formatLabel(guide.category), icon: Sparkles },
            { label: 'Reading time', value: `${guide.readingTimeMinutes} min`, icon: Clock },
          ]}
          aside={
            <div className="space-y-3 rounded-2xl border border-border bg-background-light p-4">
              <ProgressBar value={guide.progress.readPercent} showValue label="Reading progress" color={guide.progress.completed ? 'success' : 'primary'} />
              <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-1">
                <Button type="button" variant="outline" onClick={toggleBookmark} loading={bookmarking} disabled={locked}>
                  <Bookmark className={cn('h-4 w-4', guide.bookmarked && 'fill-primary text-primary')} aria-hidden="true" />
                  {guide.bookmarked ? 'Bookmarked' : 'Bookmark'}
                </Button>
                <Button type="button" onClick={markComplete} loading={completing} disabled={!readable || guide.progress.completed}>
                  <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
                  {guide.progress.completed ? 'Completed' : 'Mark read'}
                </Button>
              </div>
            </div>
          }
        />

        {actionError ? <InlineAlert variant="warning">{actionError}</InlineAlert> : null}

        {locked ? (
          <InlineAlert
            variant="warning"
            title="Upgrade required"
            action={
              <Link href="/billing" className="inline-flex min-h-11 items-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-semibold text-white hover:bg-primary/90">
                View plans
                <ArrowRight className="h-4 w-4" aria-hidden="true" />
              </Link>
            }
          >
            This guide is attached to package content that is not included in your current access.
          </InlineAlert>
        ) : null}

        {!guide.isAccessible && guide.isPreviewEligible ? (
          <InlineAlert variant="info" title="Preview access">You can preview this strategy guide. Upgrade when you are ready to unlock its linked module content.</InlineAlert>
        ) : null}

        <MotionSection className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_320px]">
          <article className="space-y-6">
            {content?.overview ? (
              <Card>
                <p className="text-base leading-8 text-navy">{content.overview}</p>
              </Card>
            ) : null}

            {sections.map((section, index) => (
              <MotionItem key={`${section.heading ?? 'section'}-${index}`} delayIndex={index}>
                <Card className="space-y-4">
                  {section.heading ? <h2 className="text-xl font-bold text-navy">{section.heading}</h2> : null}
                  {section.body ? <p className="text-sm leading-7 text-muted">{section.body}</p> : null}
                  {section.bullets && section.bullets.length > 0 ? (
                    <ul className="space-y-2">
                      {section.bullets.map((bullet) => (
                        <li key={bullet} className="flex gap-2 text-sm leading-6 text-muted">
                          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
                          <span>{bullet}</span>
                        </li>
                      ))}
                    </ul>
                  ) : null}
                </Card>
              </MotionItem>
            ))}

            {takeaways.length > 0 ? (
              <Card className="border-success/30 bg-success/10">
                <h2 className="text-lg font-bold text-success">Key takeaways</h2>
                <ul className="mt-3 space-y-2">
                  {takeaways.map((item) => (
                    <li key={item} className="flex gap-2 text-sm leading-6 text-success">
                      <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                      <span>{item}</span>
                    </li>
                  ))}
                </ul>
              </Card>
            ) : null}

            {!content && fallbackParagraphs.length > 0 ? (
              <Card className="space-y-4">
                {fallbackParagraphs.map((paragraph) => (
                  <p key={paragraph} className="text-sm leading-7 text-muted">{paragraph}</p>
                ))}
              </Card>
            ) : null}

            {!content && fallbackParagraphs.length === 0 ? (
              <Card className="border-dashed bg-background-light text-sm text-muted">Content for this guide is being prepared.</Card>
            ) : null}
          </article>

          <aside className="space-y-5">
            <Card className="space-y-3">
              <div className="flex items-center justify-between gap-3">
                <span className="text-sm font-bold text-navy">Guide status</span>
                <Badge variant={guide.progress.completed ? 'success' : guide.progress.readPercent > 0 ? 'info' : 'muted'}>
                  {guide.progress.completed ? 'Completed' : guide.progress.readPercent > 0 ? 'In progress' : 'Not started'}
                </Badge>
              </div>
              <div className="text-sm leading-6 text-muted">
                {guide.programTitle ? <p>Program: {guide.programTitle}</p> : null}
                {guide.moduleTitle ? <p>Module: {guide.moduleTitle}</p> : null}
                {guide.sourceProvenance ? <p>Source: {guide.sourceProvenance}</p> : null}
              </div>
            </Card>

            {(guide.previousGuideId || guide.nextGuideId) ? (
              <Card className="space-y-3">
                <h2 className="text-sm font-bold text-navy">Reading path</h2>
                <div className="grid gap-2">
                  {guide.previousGuideId ? (
                    <Link href={`/strategies/${encodeURIComponent(guide.previousGuideId)}`} className="inline-flex min-h-11 items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm font-semibold text-navy hover:bg-surface">
                      <ChevronLeft className="h-4 w-4" aria-hidden="true" />
                      Previous guide
                    </Link>
                  ) : null}
                  {guide.nextGuideId ? (
                    <Link href={`/strategies/${encodeURIComponent(guide.nextGuideId)}`} className="inline-flex min-h-11 items-center justify-between gap-2 rounded-lg border border-border px-3 py-2 text-sm font-semibold text-navy hover:bg-surface">
                      Next guide
                      <ChevronRight className="h-4 w-4" aria-hidden="true" />
                    </Link>
                  ) : null}
                </div>
              </Card>
            ) : null}
          </aside>
        </MotionSection>

        {guide.relatedGuides.length > 0 ? (
          <MotionSection className="space-y-4">
            <LearnerSurfaceSectionHeader title="Related Guides" icon={Lightbulb} />
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              {guide.relatedGuides.map((related) => (
                <Link key={related.id} href={`/strategies/${encodeURIComponent(related.id)}`} className="block h-full focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 rounded-2xl">
                  <Card hoverable className="h-full">
                    <Badge variant="outline">{formatLabel(related.subtestCode)}</Badge>
                    <h3 className="mt-3 text-base font-bold text-navy">{related.title}</h3>
                    {related.summary ? <p className="mt-2 text-sm leading-6 text-muted">{related.summary}</p> : null}
                  </Card>
                </Link>
              ))}
            </div>
          </MotionSection>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
