'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, CheckCircle2, XCircle, AlertTriangle, RefreshCw, Rocket } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Toast } from '@/components/ui/alert';
import {
  AdminRouteWorkspace,
  AdminRoutePanel,
  AdminRouteSectionHeader,
} from '@/components/domain/admin-route-surface';
import { ReadingWizardSteps } from '@/components/domain/admin/reading/ReadingWizardSteps';
import {
  validateReadingPaper,
  getReadingStructureAdmin,
  type ReadingValidationReport,
  type ReadingStructureAdminDto,
} from '@/lib/reading-authoring-api';
import { apiClient } from '@/lib/api';

type PublishState = 'idle' | 'confirming' | 'publishing' | 'published' | 'error';

export default function ReadingValidatePublishPage() {
  const params = useParams<{ paperId: string }>();
  const paperId = params?.paperId ?? '';

  const [report, setReport] = useState<ReadingValidationReport | null>(null);
  const [structure, setStructure] = useState<ReadingStructureAdminDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [validating, setValidating] = useState(false);
  const [publishState, setPublishState] = useState<PublishState>('idle');
  const [toast, setToast] = useState<{ message: string; variant: 'success' | 'error' } | null>(null);

  const fetchData = async () => {
    if (!paperId) return;
    setLoading(true);
    try {
      const [validationResult, structureResult] = await Promise.all([
        validateReadingPaper(paperId),
        getReadingStructureAdmin(paperId),
      ]);
      setReport(validationResult);
      setStructure(structureResult);
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
    if (publishState === 'idle') {
      setPublishState('confirming');
      return;
    }
    if (publishState === 'confirming') {
      setPublishState('publishing');
      try {
        await apiClient.put(`/v1/admin/papers/${encodeURIComponent(paperId)}/status`, { status: 'Published' });
        setPublishState('published');
        setToast({ message: 'Paper published successfully!', variant: 'success' });
      } catch {
        setPublishState('error');
        setToast({ message: 'Failed to publish paper. Please try again.', variant: 'error' });
      }
    }
  };

  const cancelPublish = () => setPublishState('idle');

  // Derive counts from structure
  const partACounts = structure?.parts.find((p) => p.partCode === 'A')?.questions.length ?? 0;
  const partBCounts = structure?.parts.find((p) => p.partCode === 'B')?.questions.length ?? 0;
  const partCCounts = structure?.parts.find((p) => p.partCode === 'C')?.questions.length ?? 0;
  const totalQuestions = partACounts + partBCounts + partCCounts;
  const totalPoints = report?.counts.totalPoints ?? 0;

  const countStatus = (actual: number, expected: number) =>
    actual === expected ? 'text-emerald-400' : 'text-red-400';

  return (
    <AdminRouteWorkspace>
      <AdminRoutePanel>
        {/* Toast */}
        {toast && (
          <Toast
            message={toast.message}
            variant={toast.variant}
            onClose={() => setToast(null)}
          />
        )}

        {/* Wizard Steps */}
        <ReadingWizardSteps paperId={paperId} currentStep="validate" />

        <AdminRouteSectionHeader
          title="Validate & Publish"
          description="Check that this paper meets the 20+6+16 = 42 question structure before publishing."
        />

        {/* Navigation Links */}
        <div className="flex items-center gap-4 mb-6">
          <Link
            href={`/admin/content/reading/${paperId}/questions`}
            className="flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            <ArrowLeft className="h-4 w-4" />
            Back to Questions
          </Link>
          <Link
            href="/admin/content/reading"
            className="flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            <ArrowLeft className="h-4 w-4" />
            Back to Reading Papers
          </Link>
        </div>

        {loading ? (
          <div className="space-y-4">
            <div className="h-8 w-48 rounded bg-muted animate-pulse" />
            <div className="h-32 w-full rounded bg-muted animate-pulse" />
          </div>
        ) : (
          <div className="space-y-6">
            {/* Summary Panel */}
            <div className="rounded-lg border border-border bg-card p-6">
              <h3 className="text-lg font-semibold text-foreground mb-4">Question Counts</h3>
              <div className="grid grid-cols-2 gap-4 sm:grid-cols-5">
                <div className="text-center">
                  <p className="text-xs text-muted-foreground uppercase tracking-wider">Part A</p>
                  <p className={`text-2xl font-bold ${countStatus(partACounts, 20)}`}>
                    {partACounts}<span className="text-sm text-muted-foreground">/20</span>
                  </p>
                </div>
                <div className="text-center">
                  <p className="text-xs text-muted-foreground uppercase tracking-wider">Part B</p>
                  <p className={`text-2xl font-bold ${countStatus(partBCounts, 6)}`}>
                    {partBCounts}<span className="text-sm text-muted-foreground">/6</span>
                  </p>
                </div>
                <div className="text-center">
                  <p className="text-xs text-muted-foreground uppercase tracking-wider">Part C</p>
                  <p className={`text-2xl font-bold ${countStatus(partCCounts, 16)}`}>
                    {partCCounts}<span className="text-sm text-muted-foreground">/16</span>
                  </p>
                </div>
                <div className="text-center">
                  <p className="text-xs text-muted-foreground uppercase tracking-wider">Total</p>
                  <p className={`text-2xl font-bold ${countStatus(totalQuestions, 42)}`}>
                    {totalQuestions}<span className="text-sm text-muted-foreground">/42</span>
                  </p>
                </div>
                <div className="text-center">
                  <p className="text-xs text-muted-foreground uppercase tracking-wider">Points</p>
                  <p className="text-2xl font-bold text-foreground">{totalPoints}</p>
                </div>
              </div>
            </div>

            {/* Validation Status */}
            <div className="rounded-lg border border-border bg-card p-6">
              <h3 className="text-lg font-semibold text-foreground mb-4">Validation Status</h3>
              {report?.isPublishReady ? (
                <div className="flex items-center gap-3">
                  <CheckCircle2 className="h-8 w-8 text-emerald-500" />
                  <Badge variant="emerald" className="text-base px-4 py-1">
                    Ready to Publish
                  </Badge>
                </div>
              ) : (
                <div className="flex items-center gap-3">
                  <XCircle className="h-8 w-8 text-red-500" />
                  <Badge variant="danger" className="text-base px-4 py-1">
                    Not Ready — {report?.issues.length ?? 0} issue{(report?.issues.length ?? 0) !== 1 ? 's' : ''}
                  </Badge>
                </div>
              )}
            </div>

            {/* Issues List */}
            {report && report.issues.length > 0 && (
              <div className="rounded-lg border border-border bg-card p-6">
                <h3 className="text-lg font-semibold text-foreground mb-4">Issues</h3>
                <ul className="space-y-2">
                  {report.issues.map((issue, idx) => (
                    <li key={idx} className="flex items-start gap-3 p-3 rounded-md bg-muted/50">
                      {issue.severity === 'error' ? (
                        <XCircle className="h-5 w-5 text-red-500 shrink-0 mt-0.5" />
                      ) : (
                        <AlertTriangle className="h-5 w-5 text-yellow-500 shrink-0 mt-0.5" />
                      )}
                      <div className="min-w-0">
                        <span className="text-xs font-mono text-muted-foreground">{issue.code}</span>
                        <p className="text-sm text-foreground">{issue.message}</p>
                      </div>
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {/* Action Buttons */}
            <div className="flex items-center gap-3 flex-wrap">
              <Button
                variant="primary"
                onClick={handleRevalidate}
                disabled={validating}
              >
                <RefreshCw className={`h-4 w-4 mr-2 ${validating ? 'animate-spin' : ''}`} />
                {validating ? 'Validating…' : 'Re-validate'}
              </Button>

              {publishState === 'published' ? (
                <Badge variant="emerald" className="text-base px-4 py-2">
                  <CheckCircle2 className="h-4 w-4 mr-2" />
                  Published!
                </Badge>
              ) : (
                <div className="flex items-center gap-2">
                  <Button
                    variant="primary"
                    onClick={handlePublishClick}
                    disabled={!report?.isPublishReady || publishState === 'publishing'}
                  >
                    <Rocket className="h-4 w-4 mr-2" />
                    {publishState === 'confirming'
                      ? 'Click again to confirm'
                      : publishState === 'publishing'
                        ? 'Publishing…'
                        : 'Publish Paper'}
                  </Button>
                  {publishState === 'confirming' && (
                    <Button variant="primary" onClick={cancelPublish}>
                      Cancel
                    </Button>
                  )}
                  {publishState === 'error' && (
                    <span className="text-sm text-red-400">Publish failed. Try again.</span>
                  )}
                </div>
              )}
            </div>
          </div>
        )}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
