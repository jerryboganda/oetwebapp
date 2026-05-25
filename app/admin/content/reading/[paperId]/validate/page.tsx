'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, CheckCircle2, XCircle, AlertTriangle, RefreshCw, Rocket } from 'lucide-react';
import { AdminOperationsLayout, KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Toast } from '@/components/ui/alert';
import { ReadingWizardSteps } from '@/components/domain/admin/reading/ReadingWizardSteps';
import {
  validateReadingPaper,
  getReadingStructureAdmin,
  type ReadingValidationReport,
  type ReadingStructureAdminDto,
} from '@/lib/reading-authoring-api';
import { getContentPaper, publishContentPaper, unpublishContentPaper } from '@/lib/content-upload-api';

type PublishState = 'idle' | 'confirming' | 'publishing' | 'published' | 'error';
type UnpublishState = 'idle' | 'confirming' | 'unpublishing';

export default function ReadingValidatePublishPage() {
  const params = useParams<{ paperId: string }>();
  const paperId = params?.paperId ?? '';

  const [report, setReport] = useState<ReadingValidationReport | null>(null);
  const [structure, setStructure] = useState<ReadingStructureAdminDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [validating, setValidating] = useState(false);
  const [publishState, setPublishState] = useState<PublishState>('idle');
  const [unpublishState, setUnpublishState] = useState<UnpublishState>('idle');
  const [, setPaperStatus] = useState<string>('');
  const [toast, setToast] = useState<{ message: string; variant: 'success' | 'error' } | null>(null);

  const fetchData = async () => {
    if (!paperId) return;
    setLoading(true);
    try {
      const [validationResult, structureResult, paperResult] = await Promise.all([
        validateReadingPaper(paperId),
        getReadingStructureAdmin(paperId),
        getContentPaper(paperId),
      ]);
      setReport(validationResult);
      setStructure(structureResult);
      setPaperStatus(paperResult.status ?? '');
      if (paperResult.status?.toLowerCase() === 'published') {
        setPublishState('published');
      }
    } catch {
      setToast({ message: 'Failed to load validation data.', variant: 'error' });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchData();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [paperId]);

  const handleRevalidate = async () => {
    if (!paperId) return;
    setValidating(true);
    try {
      const result = await validateReadingPaper(paperId);
      setReport(result);
      setToast({ message: 'Validation refreshed.', variant: 'success' });
    } catch {
      setToast({ message: 'Re-validation failed.', variant: 'error' });
    } finally {
      setValidating(false);
    }
  };

  const handlePublishClick = async () => {
    if (publishState === 'idle' || publishState === 'error') {
      setPublishState('confirming');
      return;
    }
    if (publishState === 'confirming') {
      setPublishState('publishing');
      try {
        await publishContentPaper(paperId);
        setPublishState('published');
        setPaperStatus('Published');
        setToast({ message: 'Paper published successfully!', variant: 'success' });
      } catch {
        setPublishState('error');
        setToast({ message: 'Failed to publish paper. Please try again.', variant: 'error' });
      }
    }
  };

  const cancelPublish = () => setPublishState('idle');

  const handleUnpublish = async () => {
    if (unpublishState === 'idle') {
      setUnpublishState('confirming');
      return;
    }
    if (unpublishState === 'confirming') {
      setUnpublishState('unpublishing');
      try {
        await unpublishContentPaper(paperId);
        setPublishState('idle');
        setUnpublishState('idle');
        setPaperStatus('Draft');
        setToast({ message: 'Paper reverted to draft.', variant: 'success' });
      } catch {
        setUnpublishState('idle');
        setToast({ message: 'Unpublish failed.', variant: 'error' });
      }
    }
  };

  // Derive counts from structure
  const partACounts = structure?.parts.find((p) => p.partCode === 'A')?.questions.length ?? 0;
  const partBCounts = structure?.parts.find((p) => p.partCode === 'B')?.questions.length ?? 0;
  const partCCounts = structure?.parts.find((p) => p.partCode === 'C')?.questions.length ?? 0;
  const totalQuestions = partACounts + partBCounts + partCCounts;
  const totalPoints = report?.counts.totalPoints ?? 0;

  const toneFor = (actual: number, expected: number): 'success' | 'danger' =>
    actual === expected ? 'success' : 'danger';

  return (
    <AdminOperationsLayout
      title="Validate & Publish"
      description="Check that this paper meets the 20+6+16 = 42 question structure before publishing."
      eyebrow="Reading authoring"
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Reading', href: '/admin/content/reading' },
        { label: 'Paper', href: `/admin/content/reading/${paperId}` },
        { label: 'Validate' },
      ]}
      actions={
        <Button asChild variant="ghost" size="sm" startIcon={<ArrowLeft className="h-4 w-4" />}>
          <Link href={`/admin/content/reading/${paperId}/questions`}>Back to Questions</Link>
        </Button>
      }
      kpis={
        loading ? (
          <KpiStrip>
            {[0, 1, 2, 3, 4].map((i) => (
              <KpiTile key={i} label="…" value="" loading size="sm" />
            ))}
          </KpiStrip>
        ) : (
          <KpiStrip className="lg:grid-cols-5">
            <KpiTile
              label="Part A"
              value={`${partACounts} / 20`}
              tone={toneFor(partACounts, 20)}
              size="sm"
            />
            <KpiTile
              label="Part B"
              value={`${partBCounts} / 6`}
              tone={toneFor(partBCounts, 6)}
              size="sm"
            />
            <KpiTile
              label="Part C"
              value={`${partCCounts} / 16`}
              tone={toneFor(partCCounts, 16)}
              size="sm"
            />
            <KpiTile
              label="Total"
              value={`${totalQuestions} / 42`}
              tone={toneFor(totalQuestions, 42)}
              size="sm"
            />
            <KpiTile
              label="Points"
              value={totalPoints}
              tone="info"
              size="sm"
            />
          </KpiStrip>
        )
      }
      primaryGrid={
        <div className="space-y-6">
          <ReadingWizardSteps paperId={paperId} currentStep="validate" />

          {loading ? (
            <div className="space-y-4">
              <Skeleton variant="card" />
              <Skeleton variant="card" />
            </div>
          ) : (
            <>
              {/* Validation Status */}
              <Card>
                <CardHeader>
                  <CardTitle>Validation status</CardTitle>
                </CardHeader>
                <CardContent>
                  {report?.isPublishReady ? (
                    <div className="flex items-center gap-3">
                      <CheckCircle2 className="h-8 w-8 text-[var(--admin-success)]" />
                      <Badge variant="success" size="lg">Ready to Publish</Badge>
                    </div>
                  ) : (
                    <div className="flex items-center gap-3">
                      <XCircle className="h-8 w-8 text-[var(--admin-danger)]" />
                      <Badge variant="danger" size="lg">
                        Not Ready — {report?.issues.length ?? 0} issue
                        {(report?.issues.length ?? 0) !== 1 ? 's' : ''}
                      </Badge>
                    </div>
                  )}
                </CardContent>
              </Card>

              {/* Issues */}
              {report && report.issues.length > 0 && (
                <Card>
                  <CardHeader>
                    <CardTitle>Issues</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <ul className="space-y-2">
                      {report.issues.map((issue, idx) => (
                        <li
                          key={idx}
                          className="flex items-start gap-3 rounded-admin border border-admin-border bg-admin-bg-subtle/50 p-3"
                        >
                          {issue.severity === 'error' ? (
                            <XCircle className="h-5 w-5 shrink-0 mt-0.5 text-[var(--admin-danger)]" />
                          ) : (
                            <AlertTriangle className="h-5 w-5 shrink-0 mt-0.5 text-[var(--admin-warning)]" />
                          )}
                          <div className="min-w-0">
                            <span className="text-xs font-mono text-admin-fg-muted">
                              {issue.code}
                            </span>
                            <p className="text-sm text-admin-fg-strong">{issue.message}</p>
                          </div>
                        </li>
                      ))}
                    </ul>
                  </CardContent>
                </Card>
              )}

              {/* Actions */}
              <Card>
                <CardHeader>
                  <CardTitle>Actions</CardTitle>
                </CardHeader>
                <CardContent>
                  <div className="flex flex-wrap items-center gap-3">
                    <Button
                      variant="secondary"
                      size="md"
                      onClick={handleRevalidate}
                      disabled={validating}
                      startIcon={
                        <RefreshCw className={`h-4 w-4 ${validating ? 'animate-spin' : ''}`} />
                      }
                    >
                      {validating ? 'Validating…' : 'Re-validate'}
                    </Button>

                    {publishState === 'published' ? (
                      <div className="flex items-center gap-3">
                        <Badge variant="success" size="lg" startIcon={<CheckCircle2 className="h-4 w-4" />}>
                          Published!
                        </Badge>
                        {unpublishState === 'confirming' ? (
                          <div className="flex items-center gap-2">
                            <Button variant="destructive" size="sm" onClick={handleUnpublish}>
                              Confirm Unpublish
                            </Button>
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => setUnpublishState('idle')}
                            >
                              Cancel
                            </Button>
                          </div>
                        ) : (
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={handleUnpublish}
                            disabled={unpublishState === 'unpublishing'}
                          >
                            {unpublishState === 'unpublishing' ? 'Reverting…' : 'Unpublish'}
                          </Button>
                        )}
                      </div>
                    ) : (
                      <div className="flex items-center gap-2">
                        <Button
                          variant="primary"
                          size="md"
                          onClick={handlePublishClick}
                          disabled={!report?.isPublishReady || publishState === 'publishing'}
                          startIcon={<Rocket className="h-4 w-4" />}
                        >
                          {publishState === 'confirming'
                            ? 'Click again to confirm'
                            : publishState === 'publishing'
                              ? 'Publishing…'
                              : 'Publish Paper'}
                        </Button>
                        {publishState === 'confirming' && (
                          <Button variant="ghost" size="md" onClick={cancelPublish}>
                            Cancel
                          </Button>
                        )}
                        {publishState === 'error' && (
                          <span className="text-sm text-[var(--admin-danger)]">
                            Publish failed — click Publish to retry.
                          </span>
                        )}
                      </div>
                    )}
                  </div>
                </CardContent>
              </Card>
            </>
          )}

          {toast && (
            <Toast
              message={toast.message}
              variant={toast.variant}
              onClose={() => setToast(null)}
            />
          )}
        </div>
      }
    />
  );
}
