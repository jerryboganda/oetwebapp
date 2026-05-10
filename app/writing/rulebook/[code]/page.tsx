'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useParams, useSearchParams } from 'next/navigation';
import { AlertTriangle, ArrowLeft, BookOpen, ShieldAlert } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { fetchWritingRulebook } from '@/lib/api';
import type { ExamProfession, Rule, Rulebook, RulebookSection } from '@/lib/rulebook';

const severityVariant = {
  critical: 'danger',
  major: 'warning',
  minor: 'muted',
  info: 'muted',
} as const;

type RulePageState =
  | { status: 'loading'; book: null; rule: null; section: null; message: null }
  | { status: 'ready'; book: Rulebook; rule: Rule; section: RulebookSection | null; message: null }
  | { status: 'missing' | 'error'; book: null; rule: null; section: null; message: string };

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
    ? candidate as ExamProfession
    : 'medicine';
}

export default function WritingRulePage() {
  const params = useParams<{ code?: string | string[] }>();
  const searchParams = useSearchParams();
  const codeParam = params?.code;
  const code = Array.isArray(codeParam) ? codeParam[0] : codeParam;
  const profession = normalizeWritingProfession(searchParams?.get('profession') ?? null);
  const [state, setState] = useState<RulePageState>({
    status: 'loading',
    book: null,
    rule: null,
    section: null,
    message: null,
  });

  useEffect(() => {
    let cancelled = false;
    async function load() {
      if (!code) {
        setState({ status: 'missing', book: null, rule: null, section: null, message: 'Rule code is missing.' });
        return;
      }

      setState({ status: 'loading', book: null, rule: null, section: null, message: null });
      try {
        const activeBook = await fetchWritingRulebook(profession);
        if (cancelled) return;
        const activeRule = activeBook.rules.find((entry) => entry.id.toLowerCase() === code.toLowerCase());
        if (!activeRule) {
          setState({ status: 'missing', book: null, rule: null, section: null, message: `Rule ${code} was not found in the active ${profession} Writing rulebook.` });
          return;
        }
        setState({
          status: 'ready',
          book: activeBook,
          rule: activeRule,
          section: activeBook.sections.find((entry) => entry.id === activeRule.section) ?? null,
          message: null,
        });
      } catch (error) {
        if (cancelled) return;
        setState({
          status: 'error',
          book: null,
          rule: null,
          section: null,
          message: error instanceof Error ? error.message : 'Could not load the active Writing rulebook.',
        });
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, [code, profession]);

  if (state.status !== 'ready') {
    return (
      <LearnerDashboardShell pageTitle="Writing Rulebook">
        <div className="space-y-6">
          <div className="flex items-center gap-3 text-sm text-muted">
            <Link href="/writing/library" className="inline-flex items-center gap-2 font-semibold text-primary hover:underline">
              <ArrowLeft className="h-4 w-4" /> Back to writing library
            </Link>
          </div>
          <Card className="border-border bg-surface p-8">
            <div className="flex items-center gap-3">
              <BookOpen className="h-5 w-5 text-primary" />
              <p className="text-sm font-semibold text-muted">
                {state.status === 'loading' ? 'Loading active Writing rulebook...' : state.message}
              </p>
            </div>
          </Card>
        </div>
      </LearnerDashboardShell>
    );
  }

  const { book, rule, section } = state;

  return (
    <LearnerDashboardShell pageTitle={`Writing Rule ${rule.id}`}>
      <div className="space-y-6">
        <div className="flex items-center gap-3 text-sm text-muted">
          <Link href="/writing/library" className="inline-flex items-center gap-2 font-semibold text-primary hover:underline">
            <ArrowLeft className="h-4 w-4" /> Back to writing library
          </Link>
        </div>

        <Card className="border-border bg-surface p-8">
          <div className="flex flex-wrap items-center gap-3">
            <Badge variant={severityVariant[rule.severity]} size="sm">{rule.severity}</Badge>
            <Badge variant="muted" size="sm">{rule.id}</Badge>
            <Badge variant="muted" size="sm">v{book.version}</Badge>
          </div>

          <div className="mt-5 flex items-start gap-4">
            {rule.severity === 'critical' ? (
              <ShieldAlert className="mt-1 h-6 w-6 shrink-0 text-primary" />
            ) : (
              <BookOpen className="mt-1 h-6 w-6 shrink-0 text-primary" />
            )}
            <div>
              <p className="text-xs font-black uppercase tracking-widest text-muted">{section?.title ?? `Section ${rule.section}`}</p>
              <h1 className="mt-2 text-3xl font-black tracking-tight text-navy">{rule.title}</h1>
              <p className="mt-4 text-base leading-7 text-muted">{rule.body}</p>
            </div>
          </div>
        </Card>

        {rule.exemplarPhrases?.length ? (
          <Card className="border-border bg-surface p-6">
            <div className="flex items-center gap-2">
              <BookOpen className="h-5 w-5 text-primary" />
              <h2 className="text-lg font-black text-navy">Exemplar phrasing</h2>
            </div>
            <div className="mt-4 space-y-3">
              {rule.exemplarPhrases.map((phrase) => (
                <div key={phrase} className="rounded-2xl border border-border bg-background-light p-4 text-sm text-navy">
                  “{phrase}”
                </div>
              ))}
            </div>
          </Card>
        ) : null}

        {rule.forbiddenPatterns?.length ? (
          <Card className="border-warning/30 bg-warning/10 p-6">
            <div className="flex items-center gap-2">
              <AlertTriangle className="h-5 w-5 text-warning" />
              <h2 className="text-lg font-black text-warning">Engine-enforced forbidden patterns</h2>
            </div>
            <ul className="mt-4 space-y-2 text-sm text-warning">
              {rule.forbiddenPatterns.map((pattern) => (
                <li key={pattern} className="rounded-xl border border-warning/30 bg-white/70 px-3 py-2 font-mono text-xs">{pattern}</li>
              ))}
            </ul>
          </Card>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
