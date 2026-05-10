'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import { ArrowLeft, BookOpen, ShieldAlert } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { fetchWritingRulebook } from '@/lib/api';
import type { ExamProfession, Rulebook } from '@/lib/rulebook';

const severityVariant = {
  critical: 'danger',
  major: 'warning',
  minor: 'muted',
  info: 'muted',
} as const;

const supportedWritingProfessions = new Set<ExamProfession>([
  'medicine',
  'nursing',
  'dentistry',
  'pharmacy',
  'physiotherapy',
  'veterinary',
  'optometry',
  'radiography',
  'occupational-therapy',
  'speech-pathology',
  'podiatry',
  'dietetics',
  'other-allied-health',
]);

function normalizeWritingProfession(value: string | null): ExamProfession {
  const candidate = value?.trim().toLowerCase().replace(/[\s_]+/g, '-');
  return candidate && supportedWritingProfessions.has(candidate as ExamProfession)
    ? (candidate as ExamProfession)
    : 'medicine';
}

type IndexState =
  | { status: 'loading'; book: null; message: null }
  | { status: 'ready'; book: Rulebook; message: null }
  | { status: 'error'; book: null; message: string };

export default function WritingRulebookIndexPage() {
  const searchParams = useSearchParams();
  const profession = normalizeWritingProfession(searchParams?.get('profession') ?? null);
  const [state, setState] = useState<IndexState>({ status: 'loading', book: null, message: null });

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setState({ status: 'loading', book: null, message: null });
      try {
        const book = await fetchWritingRulebook(profession);
        if (cancelled) return;
        setState({ status: 'ready', book, message: null });
      } catch (error) {
        if (cancelled) return;
        setState({
          status: 'error',
          book: null,
          message:
            error instanceof Error ? error.message : 'Could not load the active Writing rulebook.',
        });
      }
    }
    void load();
    return () => {
      cancelled = true;
    };
  }, [profession]);

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <header className="space-y-1">
          <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
            Rulebook
          </p>
          <h1 className="text-2xl font-semibold">OET Writing rulebook</h1>
          <p className="text-sm text-muted-foreground">
            Every rule the live checker applies to your draft, grouped by section. Pick a rule to
            see its full criteria, examples, and remediation tips.
          </p>
        </header>
        <div className="flex items-center justify-between">
          <Link
            href="/writing"
            className="inline-flex items-center gap-2 text-sm font-medium text-muted-foreground hover:text-foreground"
          >
            <ArrowLeft className="h-4 w-4" /> Back to Writing
          </Link>
          <Badge variant="muted" className="capitalize">
            {profession.replaceAll('-', ' ')}
          </Badge>
        </div>

        {state.status === 'loading' && (
          <Card className="p-8 text-center text-sm text-muted-foreground">
            Loading the active Writing rulebook…
          </Card>
        )}

        {state.status === 'error' && (
          <Card className="p-8 text-center">
            <ShieldAlert className="mx-auto h-8 w-8 text-destructive" />
            <p className="mt-3 text-sm text-destructive">{state.message}</p>
          </Card>
        )}

        {state.status === 'ready' && (
          <div className="space-y-6">
            {state.book.sections.map((section) => {
              const sectionRules = state.book.rules.filter((r) => r.section === section.id);
              if (sectionRules.length === 0) return null;
              return (
                <Card key={section.id} className="p-6">
                  <div className="mb-4 flex items-start gap-3">
                    <BookOpen className="mt-1 h-5 w-5 text-primary" />
                    <div>
                      <h2 className="text-lg font-semibold">{section.title}</h2>
                    </div>
                  </div>
                  <ul className="space-y-2">
                    {sectionRules.map((rule) => (
                      <li key={rule.id}>
                        <Link
                          href={`/writing/rulebook/${encodeURIComponent(rule.id)}?profession=${encodeURIComponent(profession)}`}
                          className="flex items-start justify-between gap-3 rounded-md border border-border/60 bg-card px-3 py-2 transition-colors hover:bg-accent/40"
                        >
                          <div>
                            <div className="text-sm font-medium">
                              {rule.id} · {rule.title}
                            </div>
                            {rule.body && (
                              <div className="line-clamp-2 text-xs text-muted-foreground">
                                {rule.body}
                              </div>
                            )}
                          </div>
                          <Badge
                            variant={severityVariant[rule.severity] ?? 'muted'}
                            className="capitalize"
                          >
                            {rule.severity}
                          </Badge>
                        </Link>
                      </li>
                    ))}
                  </ul>
                </Card>
              );
            })}
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
