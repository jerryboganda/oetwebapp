'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { BookOpen, Download, FileText, Library } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { Toast } from '@/components/ui/alert';
import { Select } from '@/components/ui/form-controls';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import {
  downloadRecallDocumentMedia,
  learnerListRecallDocuments,
  type RecallDocumentLearnerDto,
  type RecallSubtest,
} from '@/lib/api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const SUBTEST_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: 'All sections' },
  { value: 'listening', label: 'Listening' },
  { value: 'reading', label: 'Reading' },
  { value: 'writing', label: 'Writing' },
  { value: 'speaking', label: 'Speaking' },
  { value: 'cross', label: 'Cross-cutting' },
];

export default function RecallsDocumentsPage() {
  const [items, setItems] = useState<RecallDocumentLearnerDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [filterSubtest, setFilterSubtest] = useState<string>('');
  const [toast, setToast] = useState<ToastState>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const data = await learnerListRecallDocuments((filterSubtest as RecallSubtest) || undefined);
      setItems(data ?? []);
    } catch (e) {
      setToast({ variant: 'error', message: `Could not load recall PDFs: ${(e as Error).message}` });
    } finally {
      setLoading(false);
    }
  }, [filterSubtest]);

  useEffect(() => { void reload(); }, [reload]);

  async function downloadMedia(doc: RecallDocumentLearnerDto) {
    if (!doc.media?.id) return;
    setBusyId(doc.id);
    try {
      const blob = await downloadRecallDocumentMedia(doc.media.id);
      const blobUrl = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = blobUrl;
      a.download = doc.media.originalFilename || `${doc.title}.pdf`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(blobUrl);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  const groupedBySubtest = useMemo(() => {
    const map = new Map<string, RecallDocumentLearnerDto[]>();
    for (const d of items) {
      const k = d.subtestCode;
      if (!map.has(k)) map.set(k, []);
      map.get(k)!.push(d);
    }
    return Array.from(map.entries()).sort(([a], [b]) => a.localeCompare(b));
  }, [items]);

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Recalls / Documents"
          title="Recall PDFs"
          description="Recent OET exam recall digests uploaded by your tutor. Open or download to revise."
          icon={Library}
          highlights={[
            { icon: FileText, label: 'Documents', value: `${items.length}` },
          ]}
        />

        <Card className="p-4">
          <Select
            label="Filter by section"
            value={filterSubtest}
            onChange={(e) => setFilterSubtest(e.target.value)}
            options={SUBTEST_OPTIONS}
          />
        </Card>

        {loading ? (
          <div className="space-y-3">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-24" />)}
          </div>
        ) : items.length === 0 ? (
          <Card className="p-10 text-center text-muted space-y-2">
            <FileText className="h-8 w-8 mx-auto opacity-60" aria-hidden />
            <p>No recall documents are published yet for your profession.</p>
            <p className="text-xs">Check back soon - your tutor uploads new monthly digests to this library.</p>
          </Card>
        ) : (
          <div className="space-y-6">
            {groupedBySubtest.map(([subtest, docs]) => (
              <section key={subtest} aria-labelledby={`section-${subtest}`} className="space-y-3">
                <h2 id={`section-${subtest}`} className="text-lg font-semibold capitalize flex items-center gap-2">
                  <BookOpen className="h-5 w-5 text-primary" aria-hidden />
                  {subtest === 'cross' ? 'Cross-cutting' : subtest}
                </h2>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  {docs.map((doc) => (
                    <Card key={doc.id} className="p-4 space-y-2">
                      <div className="flex items-start justify-between gap-2">
                        <div className="min-w-0 flex-1">
                          <h3 className="font-semibold truncate">{doc.title}</h3>
                          <div className="flex items-center gap-2 mt-1 flex-wrap">
                            <Badge variant="outline">{doc.periodLabel}</Badge>
                            {doc.professionId ? <Badge variant="outline">{doc.professionId}</Badge> : null}
                          </div>
                        </div>
                      </div>
                      {doc.descriptionMarkdown ? (
                        <p className="text-sm text-muted line-clamp-3">{doc.descriptionMarkdown}</p>
                      ) : null}
                      <div className="flex items-center justify-between pt-2">
                        <span className="text-xs text-muted">
                          {doc.media?.sizeBytes
                            ? `${(doc.media.sizeBytes / 1024 / 1024).toFixed(2)} MB`
                            : ''}
                        </span>
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => void downloadMedia(doc)}
                          disabled={busyId === doc.id || !doc.media}
                        >
                          <Download className="h-4 w-4 mr-1" />
                          {busyId === doc.id ? 'Downloading...' : 'Download PDF'}
                        </Button>
                      </div>
                    </Card>
                  ))}
                </div>
              </section>
            ))}
          </div>
        )}

        {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
      </div>
    </LearnerDashboardShell>
  );
}
