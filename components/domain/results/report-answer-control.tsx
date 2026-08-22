'use client';

import { useEffect, useState } from 'react';
import { Flag } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { Textarea } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { createListeningAnswerKeyReport } from '@/lib/listening-api';
import { createReadingAnswerKeyReport } from '@/lib/reading-authoring-api';
import { readErrorMessage } from '@/lib/read-error-message';

export type AnswerKeyReportReasonCode =
  | 'wrong_official_answer'
  | 'missing_accepted_variant'
  | 'other';

const REASONS: { value: AnswerKeyReportReasonCode; label: string }[] = [
  { value: 'wrong_official_answer', label: 'Official answer looks wrong' },
  { value: 'missing_accepted_variant', label: 'My wording should also be accepted' },
  { value: 'other', label: 'Something else about this key' },
];

interface ReportAnswerControlProps {
  assessment: 'reading' | 'listening';
  attemptId: string;
  questionId: string;
  alreadyReported?: boolean;
}

export function ReportAnswerControl({
  assessment,
  attemptId,
  questionId,
  alreadyReported = false,
}: ReportAnswerControlProps) {
  const [open, setOpen] = useState(false);
  const [reported, setReported] = useState(alreadyReported);
  const [reason, setReason] = useState<AnswerKeyReportReasonCode | ''>('');
  const [details, setDetails] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [toast, setToast] = useState<string | null>(null);

  useEffect(() => {
    setReported(alreadyReported);
  }, [alreadyReported]);

  if (!attemptId || !questionId) return null;

  async function handleSubmit() {
    if (!reason) {
      setError('Choose a reason before sending this report.');
      return;
    }

    setSubmitting(true);
    setError(null);
    try {
      const payload = {
        questionId,
        reasonCode: reason,
        details: details.trim() || undefined,
      };
      if (assessment === 'listening') {
        await createListeningAnswerKeyReport(attemptId, payload);
      } else {
        await createReadingAnswerKeyReport(attemptId, payload);
      }
      setReported(true);
      setOpen(false);
      setToast('Report received. This does not change your mark immediately.');
    } catch (err) {
      setError(readErrorMessage(err, 'Could not send this report. Please try again.'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <>
      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="outline"
          onClick={() => setOpen(true)}
          disabled={reported}
          aria-label={reported ? 'Reported' : 'Report this answer'}
        >
          <Flag className="mr-2 h-4 w-4" aria-hidden />
          {reported ? 'Reported' : 'Report this answer'}
        </Button>
        <p className="text-xs text-muted">
          Use this if the official answer looks wrong. Flag stays a private bookmark.
        </p>
      </div>

      <Modal
        open={open}
        onClose={() => {
          if (!submitting) setOpen(false);
        }}
        title="Report this official answer"
        size="md"
      >
        <div className="space-y-4">
          <p className="text-sm leading-6 text-muted">
            This does not change your mark immediately. A content reviewer will check the official key.
          </p>
          <fieldset className="space-y-2">
            <legend className="text-sm font-semibold text-navy">Why are you reporting this?</legend>
            <div className="flex flex-wrap gap-2">
              {REASONS.map((option) => (
                <Button
                  key={option.value}
                  type="button"
                  variant={reason === option.value ? 'primary' : 'secondary'}
                  aria-pressed={reason === option.value}
                  onClick={() => {
                    setReason(option.value);
                    setError(null);
                  }}
                >
                  {option.label}
                </Button>
              ))}
            </div>
          </fieldset>
          <Textarea
            label="More detail (optional)"
            value={details}
            onChange={(event) => setDetails(event.target.value)}
            maxLength={2000}
            rows={4}
            hint="Tell the reviewer what you expected the official answer to be."
          />
          {error ? <p className="text-sm text-red-600">{error}</p> : null}
          <div className="flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={() => setOpen(false)} disabled={submitting}>
              Cancel
            </Button>
            <Button type="button" variant="primary" onClick={() => void handleSubmit()} disabled={submitting}>
              {submitting ? 'Sending…' : 'Send report'}
            </Button>
          </div>
        </div>
      </Modal>

      {toast ? (
        <Toast variant="success" message={toast} onClose={() => setToast(null)} />
      ) : null}
    </>
  );
}
