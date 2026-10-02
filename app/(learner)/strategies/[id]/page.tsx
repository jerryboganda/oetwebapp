'use client';

import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import { useParams } from 'next/navigation';
import {
  ArrowLeft,
  ArrowRight,
  Bookmark,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  Clock,
  Lightbulb,
  Sparkles,
} from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Badge, Button, Card, CardLink, EmptyState, InlineAlert, MotionItem, MotionSection, ProgressBar, Skeleton } from '@/components/ui';
import { fetchStrategyGuide, isApiError, setStrategyGuideBookmark, updateStrategyGuideProgress } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { cn } from '@/lib/utils';
import type {
  StrategyGuideDetail,
  StrategyGuideStructuredContent,
  StrategyGuideStructuredSection,
} from '@/lib/types/strategies';

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
    <>
      <Skeleton className="h-36 rounded-2xl" />
      <Skeleton className="h-96 rounded-2xl" />
    </>
  );
}

function BackToStrategies() {
  return (
    <div>
      <Button variant="outline" asChild>
        <Link href="/strategies">
          <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
          Back to strategies
        </Link>
      </Button>
    </div>
  );
}

function DisabledState() {
  return (
    <>
      <LearnerPageHero
        title="Strategy guide unavailable"
        description="This learner strategy guide is behind a release flag right now."
        icon={Lightbulb}
        accent="amber"
      />
      <BackToStrategies />
    </>
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
      <>
        <InlineAlert variant="error" title="Strategy guide did not load">{error ?? 'Strategy guide not found.'}</InlineAlert>
        <BackToStrategies />
      </>
    );
  }

  const locked = !guide.isAccessible && guide.requiresUpgrade && !guide.isPreviewEligible;
  const readable = guide.isAccessible || guide.isPreviewEligible;
  const sections = content?.sections ?? [];
  const takeaways = content?.keyTakeaways ?? [];

  // The shell breadcrumb (Dashboard › Strategies › …) is the way back.
  return (
    <>
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
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-1">
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
            <Button asChild>
              <Link href="/subscriptions">
                View plans
                <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
              </Link>
            </Button>
          }
        >
          This guide is attached to package content that is not included in your current access.
        </InlineAlert>
      ) : null}

      {!guide.isAccessible && guide.isPreviewEligible ? (
        <InlineAlert variant="info" title="Preview access">You can preview this strategy guide. Upgrade when you are ready to unlock its linked module content.</InlineAlert>
      ) : null}

      <MotionSection className="grid grid-cols-1 gap-6 xl:grid-cols-[minmax(0,1fr)_320px]">
        <article className="min-w-0 space-y-6">
          {content?.overview ? (
            <Card>
              <p className="text-base leading-8 text-navy">{content.overview}</p>
            </Card>
          ) : null}

          {sections.map((section, index) => (
            <MotionItem key={`${section.heading ?? 'section'}-${index}`} delayIndex={Math.min(index, 5)}>
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
              <h2 className="text-lg font-bold text-success-strong">Key takeaways</h2>
              <ul className="mt-3 space-y-2">
                {takeaways.map((item) => (
                  <li key={item} className="flex gap-2 text-sm leading-6 text-success-strong">
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
            <EmptyState className="py-8" icon={<Lightbulb className="h-7 w-7" aria-hidden="true" />} title="Content for this guide is being prepared." />
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
              <div className="grid grid-cols-1 gap-2">
                {guide.previousGuideId ? (
                  <Button variant="outline" fullWidth asChild className="justify-start">
                    <Link href={`/strategies/${encodeURIComponent(guide.previousGuideId)}`}>
                      <ChevronLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
                      Previous guide
                    </Link>
                  </Button>
                ) : null}
                {guide.nextGuideId ? (
                  <Button variant="outline" fullWidth asChild className="justify-between">
                    <Link href={`/strategies/${encodeURIComponent(guide.nextGuideId)}`}>
                      Next guide
                      <ChevronRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
                    </Link>
                  </Button>
                ) : null}
              </div>
            </Card>
          ) : null}
        </aside>
      </MotionSection>

      {guide.relatedGuides.length > 0 ? (
        <MotionSection className="space-y-4">
          <LearnerSurfaceSectionHeader title="Related Guides" icon={Lightbulb} />
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
            {guide.relatedGuides.map((related, index) => (
              <MotionItem key={related.id} delayIndex={Math.min(index, 5)} className="h-full">
                <CardLink href={`/strategies/${encodeURIComponent(related.id)}`} className="h-full">
                  <Badge variant="outline">{formatLabel(related.subtestCode)}</Badge>
                  <h3 className="mt-3 text-base font-bold text-navy">{related.title}</h3>
                  {related.summary ? <p className="mt-2 text-sm leading-6 text-muted">{related.summary}</p> : null}
                </CardLink>
              </MotionItem>
            ))}
          </div>
        </MotionSection>
      ) : null}
    </>
  );
}
