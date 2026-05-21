'use client';

import { useState } from 'react';
import { AlertTriangle, CheckCircle2, Clock, FileWarning, ShieldAlert, Trash2 } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import type { ReadingExtractionDraftDto, ReadingExtractionStatus } from '@/lib/reading-authoring-api';

interface Props {
  draft: ReadingExtractionDraftDto;
  isActive: boolean;
  isApproving: boolean;
  isRejecting: boolean;
  onSelect: () => void;
  onApprove: () => void;
  onReject: (reason: string) => void;
}

function statusBadgeVariant(status: ReadingExtractionStatus): 'success' | 'warning' | 'danger' | 'info' | 'outline' {
  switch (status) {
    case 'Approved': return 'success';
    case 'Rejected': return 'outline';
    case 'Failed': return 'danger';
    case 'Pending':
    default: return 'info';
  }
}

/**
 * Phase 4 closure — one card per ReadingExtractionDraft. Shows status,
 * stub warning, per-part question counts, an "Approve" CTA gated on
 * `!draft.isStub` (matches the backend rule at
 * `ReadingExtractionService.cs:197`), and an inline reject form whose
 * non-empty `reason` is enforced client-side (matches `ReadingExtractionService.cs:247`).
 */
export function ReadingExtractionDraftCard({
  draft,
  isActive,
  isApproving,
  isRejecting,
  onSelect,
  onApprove,
  onReject,
}: Props) {
  const [reason, setReason] = useState('');
  const [showRejectForm, setShowRejectForm] = useState(false);

  const partCounts = draft.manifest?.parts.map((p) => ({
    code: p.partCode,
    questions: p.questions.length,
    texts: p.texts.length,
  })) ?? [];

  const isPending = draft.status === 'Pending';
  const canApprove = isPending && !draft.isStub && draft.manifest != null;

  return (
    <article
      className={
        'space-y-3 rounded-2xl border bg-surface p-4 shadow-sm transition-colors ' +
        (isActive ? 'border-primary ring-2 ring-primary/20' : 'border-border hover:border-border-hover')
      }
      data-testid="reading-extraction-draft-card"
    >
      <header className="flex items-start justify-between gap-3">
        <button
          type="button"
          className="min-w-0 flex-1 text-left"
          onClick={onSelect}
          aria-label={`Select extraction draft ${draft.id}`}
        >
          <p className="truncate font-mono text-xs text-muted">{draft.id}</p>
          <p className="text-sm font-semibold text-navy">
            {draft.manifest ? `${partCounts.reduce((sum, p) => sum + p.questions, 0)} questions` : 'No manifest'}
          </p>
          <p className="text-xs text-muted">
            Created {new Date(draft.createdAt).toLocaleString()} by {draft.createdByAdminId}
          </p>
        </button>
        <Badge variant={statusBadgeVariant(draft.status)} className="text-[10px] uppercase">
          {draft.status}
        </Badge>
      </header>

      <div className="flex flex-wrap items-center gap-2">
        {draft.isStub ? (
          <Badge variant="warning" className="text-[10px]">
            <ShieldAlert className="mr-1 inline h-3 w-3" aria-hidden />
            Stub
          </Badge>
        ) : null}
        {partCounts.map((p) => (
          <Badge key={p.code} variant="outline" className="text-[10px]">
            Part {p.code} · {p.questions}Q · {p.texts}T
          </Badge>
        ))}
        {draft.resolvedAt ? (
          <Badge variant="outline" className="text-[10px]">
            <Clock className="mr-1 inline h-3 w-3" aria-hidden />
            Resolved {new Date(draft.resolvedAt).toLocaleDateString()}
          </Badge>
        ) : null}
      </div>

      {draft.notes ? (
        <p className="rounded-lg bg-background-light p-2 text-xs italic text-muted">
          {draft.notes}
        </p>
      ) : null}

      {draft.isStub && isPending ? (
        <InlineAlert variant="warning">
          <FileWarning className="mr-1 inline h-3.5 w-3.5" aria-hidden />
          Stub drafts cannot be approved. Re-run extraction with a configured AI gateway.
        </InlineAlert>
      ) : null}

      {isPending ? (
        <div className="space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              variant="primary"
              size="sm"
              disabled={!canApprove || isApproving}
              onClick={onApprove}
            >
              <CheckCircle2 className="mr-1.5 h-4 w-4" aria-hidden />
              {isApproving ? 'Approving…' : 'Approve & import'}
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => setShowRejectForm((v) => !v)}
              disabled={isRejecting}
            >
              <Trash2 className="mr-1.5 h-4 w-4" aria-hidden />
              {showRejectForm ? 'Cancel reject' : 'Reject'}
            </Button>
          </div>
          {showRejectForm ? (
            <div className="space-y-2 rounded-lg border border-border bg-background-light p-3">
              <Textarea
                label="Reason for rejecting"
                rows={2}
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                hint="Captured on the draft row + AuditEvent. Required."
              />
              <div className="flex items-center justify-end gap-2">
                <Button
                  type="button"
                  variant="destructive"
                  size="sm"
                  disabled={!reason.trim() || isRejecting}
                  onClick={() => {
                    onReject(reason.trim());
                    setReason('');
                    setShowRejectForm(false);
                  }}
                >
                  {isRejecting ? 'Rejecting…' : 'Confirm reject'}
                </Button>
              </div>
            </div>
          ) : null}
        </div>
      ) : null}

      {draft.status === 'Failed' ? (
        <InlineAlert variant="error">
          <AlertTriangle className="mr-1 inline h-3.5 w-3.5" aria-hidden />
          Extraction failed. See notes above for the upstream error.
        </InlineAlert>
      ) : null}
    </article>
  );
}
