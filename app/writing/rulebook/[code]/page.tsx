import Link from 'next/link';
import { notFound } from 'next/navigation';
import { AlertTriangle, ArrowLeft, BookOpen, ShieldAlert } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { loadRulebook } from '@/lib/rulebook';

const severityVariant = {
  critical: 'danger',
  major: 'warning',
  minor: 'muted',
  info: 'muted',
} as const;

export default async function WritingRulePage({ params }: { params: Promise<{ code: string }> }) {
  const { code } = await params;
  const book = loadRulebook('writing', 'medicine');
  const rule = book.rules.find((entry) => entry.id.toLowerCase() === code.toLowerCase());
  if (!rule) notFound();

  const section = book.sections.find((entry) => entry.id === rule.section);

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
