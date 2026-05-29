'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, Eye } from 'lucide-react';

import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  getReadingStructureLearner,
  type ReadingLearnerStructureDto,
} from '@/lib/reading-authoring-api';

const OPTION_LABELS = ['A', 'B', 'C', 'D', 'E', 'F'];

function asOptionList(options: unknown): string[] {
  if (Array.isArray(options)) {
    return options.map((o) => (typeof o === 'string' ? o : String(o)));
  }
  return [];
}

export default function ReadingPreviewAsStudentPage() {
  const params = useParams<{ paperId: string }>();
  const paperId = params?.paperId ?? '';

  const [structure, setStructure] = useState<ReadingLearnerStructureDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!paperId) return;
    let cancelled = false;
    setLoading(true);
    setError(null);
    getReadingStructureLearner(paperId)
      .then((data) => {
        if (!cancelled) setStructure(data);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Failed to load preview');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, [paperId]);

  return (
    <AdminSettingsLayout
      title="Preview as student"
      description="Read-only rendering using the learner endpoint. Correct answers are never exposed here."
      eyebrow="Reading authoring"
      icon={<Eye className="h-5 w-5" />}
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Reading', href: '/admin/content/reading' },
        { label: 'Paper', href: `/admin/content/reading/${paperId}` },
        { label: 'Preview' },
      ]}
      actions={
        <Button asChild variant="ghost" size="sm" startIcon={<ArrowLeft className="h-4 w-4" />}>
          <Link href={`/admin/content/reading/${paperId}`}>Back to paper</Link>
        </Button>
      }
    >
      <div className="space-y-6">
        <InlineAlert variant="info">
          This is a read-only preview built from the same data a learner receives. It does not start
          an attempt and shows no correct answers, explanations, or accepted variants.
        </InlineAlert>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {loading ? (
          <div className="space-y-4">
            <Skeleton variant="card" />
            <Skeleton variant="card" />
          </div>
        ) : structure ? (
          <>
            <div>
              <h2 className="text-lg font-semibold text-admin-fg-strong">{structure.paper.title}</h2>
              <p className="text-sm text-admin-fg-muted">{structure.paper.subtestCode}</p>
            </div>

            {structure.parts.map((part) => (
              <SettingsSection
                key={part.id}
                title={`Part ${part.partCode}`}
                description={`${part.timeLimitMinutes} min · ${part.questions.length} question${part.questions.length !== 1 ? 's' : ''}`}
              >
                <div className="space-y-6">
                  {part.texts.length > 0 && (
                    <div className="space-y-4">
                      {part.texts.map((text) => (
                        <article
                          key={text.id}
                          className="rounded-lg border border-admin-border bg-admin-bg-subtle p-4"
                        >
                          <h3 className="text-sm font-semibold text-admin-fg-strong">
                            {text.displayOrder}. {text.title}
                          </h3>
                          {text.source && (
                            <p className="mt-0.5 text-xs text-admin-fg-muted">{text.source}</p>
                          )}
                          <div
                            className="prose prose-sm mt-3 max-w-none text-admin-fg-default"
                            // Learner-facing body HTML, identical to the student player.
                            dangerouslySetInnerHTML={{ __html: text.bodyHtml }}
                          />
                        </article>
                      ))}
                    </div>
                  )}

                  <ol className="space-y-3">
                    {part.questions.map((q) => {
                      const options = asOptionList(q.options);
                      return (
                        <li
                          key={q.id}
                          className="rounded-lg border border-admin-border p-3"
                        >
                          <div className="flex items-start gap-2">
                            <span className="text-xs font-mono text-admin-fg-muted">
                              Q{q.displayOrder}
                            </span>
                            <p className="flex-1 text-sm text-admin-fg-strong">{q.stem}</p>
                            <Badge variant="muted" size="sm">{q.points}pt</Badge>
                          </div>
                          {options.length > 0 && (
                            <ul className="mt-2 space-y-1 pl-6">
                              {options.map((opt, idx) => (
                                <li key={idx} className="text-sm text-admin-fg-default">
                                  <span className="font-medium">{OPTION_LABELS[idx] ?? idx + 1}.</span> {opt}
                                </li>
                              ))}
                            </ul>
                          )}
                        </li>
                      );
                    })}
                  </ol>
                </div>
              </SettingsSection>
            ))}
          </>
        ) : null}
      </div>
    </AdminSettingsLayout>
  );
}
