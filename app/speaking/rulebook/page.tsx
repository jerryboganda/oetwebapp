import Link from 'next/link';
import { ArrowLeft, BookOpen, ShieldAlert, ChevronRight } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { loadRulebook } from '@/lib/rulebook';
import type { Rule, RuleSeverity } from '@/lib/rulebook';

const severityVariant: Record<RuleSeverity, 'danger' | 'warning' | 'muted'> = {
  critical: 'danger',
  major: 'warning',
  minor: 'muted',
  info: 'muted',
};

const severityRank: Record<RuleSeverity, number> = {
  critical: 0,
  major: 1,
  minor: 2,
  info: 3,
};

/**
 * Speaking Rulebook index — lists every rule grouped by section and ordered by
 * severity within each section. Replaces the old behaviour where the surface
 * button "Open Speaking Rules" deep-linked into a single rule (RULE_22) and
 * made it look like there was only one rule.
 *
 * Content is loaded server-side via `loadRulebook('speaking', 'medicine')` so
 * the canonical rulebook JSON (rulebooks/speaking/medicine/rulebook.v1.json)
 * is the single source of truth — no separate index data file to keep in sync.
 */
export default async function SpeakingRulebookIndexPage() {
  const book = loadRulebook('speaking', 'medicine');

  // Group rules by section, preserving rulebook section order.
  const sectionOrder = book.sections
    .slice()
    .sort((a, b) => (a.order ?? 0) - (b.order ?? 0));

  const rulesBySection = new Map<string, Rule[]>();
  for (const rule of book.rules) {
    const list = rulesBySection.get(rule.section) ?? [];
    list.push(rule);
    rulesBySection.set(rule.section, list);
  }

  // Sort each section's rules by severity then id for predictable order.
  for (const list of rulesBySection.values()) {
    list.sort((a, b) => {
      const sev = severityRank[a.severity] - severityRank[b.severity];
      if (sev !== 0) return sev;
      return a.id.localeCompare(b.id, undefined, { numeric: true });
    });
  }

  const totals = book.rules.reduce(
    (acc, rule) => {
      acc[rule.severity] = (acc[rule.severity] ?? 0) + 1;
      return acc;
    },
    { critical: 0, major: 0, minor: 0, info: 0 } as Record<RuleSeverity, number>,
  );

  return (
    <LearnerDashboardShell pageTitle="Speaking Rulebook">
      <div className="space-y-6">
        <div className="flex items-center gap-3 text-sm text-muted">
          <Link href="/speaking" className="inline-flex items-center gap-2 font-semibold text-primary hover:underline">
            <ArrowLeft className="h-4 w-4" /> Back to speaking
          </Link>
        </div>

        <Card className="border-border bg-surface p-8">
          <div className="flex flex-wrap items-center gap-3">
            <Badge variant="muted" size="sm">v{book.version}</Badge>
            <Badge variant="muted" size="sm">{book.profession}</Badge>
            <Badge variant="muted" size="sm">{book.rules.length} rules</Badge>
          </div>
          <div className="mt-5 flex items-start gap-4">
            <BookOpen className="mt-1 h-6 w-6 shrink-0 text-primary" />
            <div>
              <p className="text-xs font-black uppercase tracking-widest text-muted">Speaking Rulebook</p>
              <h1 className="mt-2 text-3xl font-black tracking-tight text-navy">
                Every rule your speaking is judged on
              </h1>
              <p className="mt-3 max-w-3xl text-base leading-7 text-muted">
                These are the canonical rules our AI audit and your tutor reviewers apply to every role-play.
                Critical rules must be followed; major rules cost band points; minor rules are polish.
              </p>
              <div className="mt-4 flex flex-wrap gap-2">
                <Badge variant="danger" size="sm">{totals.critical} critical</Badge>
                <Badge variant="warning" size="sm">{totals.major} major</Badge>
                <Badge variant="muted" size="sm">{totals.minor} minor</Badge>
                <Badge variant="muted" size="sm">{totals.info} info</Badge>
              </div>
            </div>
          </div>
        </Card>

        {sectionOrder.map((section) => {
          const rules = rulesBySection.get(section.id) ?? [];
          if (rules.length === 0) return null;
          return (
            <section key={section.id} aria-labelledby={`section-${section.id}`} className="space-y-3">
              <div className="flex items-baseline justify-between">
                <h2 id={`section-${section.id}`} className="text-lg font-black tracking-tight text-navy">
                  {section.title}
                </h2>
                <span className="text-xs font-semibold uppercase tracking-wider text-muted">
                  Section {section.id}
                </span>
              </div>
              <ul className="grid grid-cols-1 gap-3 md:grid-cols-2">
                {rules.map((rule) => (
                  <li key={rule.id}>
                    <Link
                      href={`/speaking/rulebook/${rule.id}`}
                      className="group block rounded-2xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
                      aria-label={`Open rule ${rule.id}: ${rule.title}`}
                    >
                      <Card hoverable padding="md" className="h-full">
                        <div className="flex items-start gap-3">
                          {rule.severity === 'critical' ? (
                            <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0 text-danger" aria-hidden />
                          ) : (
                            <BookOpen className="mt-0.5 h-5 w-5 shrink-0 text-primary" aria-hidden />
                          )}
                          <div className="min-w-0 flex-1">
                            <div className="flex flex-wrap items-center gap-2">
                              <Badge variant={severityVariant[rule.severity]} size="sm">
                                {rule.severity}
                              </Badge>
                              <span className="font-mono text-[11px] text-muted">{rule.id}</span>
                            </div>
                            <h3 className="mt-2 text-base font-bold text-navy group-hover:text-primary">
                              {rule.title}
                            </h3>
                            <p className="mt-1 line-clamp-3 text-sm leading-6 text-muted">{rule.body}</p>
                          </div>
                          <ChevronRight
                            className="mt-1 h-4 w-4 shrink-0 text-muted transition group-hover:translate-x-0.5 group-hover:text-primary"
                            aria-hidden
                          />
                        </div>
                      </Card>
                    </Link>
                  </li>
                ))}
              </ul>
            </section>
          );
        })}
      </div>
    </LearnerDashboardShell>
  );
}
