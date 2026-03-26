'use client';

import { cn } from '@/lib/utils';
import { Drawer } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import { Select, Textarea, RadioGroup, type RadioOption } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { useState } from 'react';

const turnaroundOptions: RadioOption[] = [
  { value: 'standard', label: 'Standard (3–5 days)', description: '1 credit' },
  { value: 'priority', label: 'Priority (1–2 days)', description: '2 credits' },
  { value: 'express', label: 'Express (24 hours)', description: '3 credits' },
];

const focusAreaOptions = [
  { value: 'overall', label: 'Overall assessment' },
  { value: 'grammar', label: 'Grammar & language' },
  { value: 'structure', label: 'Structure & organization' },
  { value: 'content', label: 'Clinical content accuracy' },
  { value: 'pronunciation', label: 'Pronunciation & fluency' },
  { value: 'empathy', label: 'Empathy & rapport' },
];

interface ReviewRequestDrawerProps {
  open: boolean;
  onClose: () => void;
  subtest: string;
  submissionTitle?: string;
  availableCredits?: number;
  onSubmit?: (data: { turnaround: string; focusArea: string; notes: string; paymentMethod: string }) => void;
  className?: string;
}

export function ReviewRequestDrawer({
  open, onClose, subtest, submissionTitle, availableCredits = 0, onSubmit, className,
}: ReviewRequestDrawerProps) {
  const [turnaround, setTurnaround] = useState('standard');
  const [focusArea, setFocusArea] = useState('overall');
  const [notes, setNotes] = useState('');
  const [paymentMethod, setPaymentMethod] = useState('credits');

  const creditCost = turnaround === 'express' ? 3 : turnaround === 'priority' ? 2 : 1;
  const hasCredits = availableCredits >= creditCost;

  return (
    <Drawer open={open} onClose={onClose} title="Request Expert Review" className={className}>
      <div className="flex flex-col gap-5">
        {submissionTitle && (
          <div className="p-3 bg-gray-50 rounded">
            <p className="text-xs text-muted">Reviewing</p>
            <p className="text-sm font-semibold text-navy">{submissionTitle}</p>
            <p className="text-xs text-muted mt-0.5">{subtest}</p>
          </div>
        )}

        <RadioGroup
          name="turnaround"
          label="Turnaround Time"
          options={turnaroundOptions}
          value={turnaround}
          onChange={setTurnaround}
        />

        <Select
          label="Focus Area"
          options={focusAreaOptions}
          value={focusArea}
          onChange={(e) => setFocusArea(e.target.value)}
        />

        <Textarea
          label="Notes for Reviewer (optional)"
          placeholder="Any specific areas or questions you'd like the reviewer to focus on..."
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          hint="Max 500 characters"
        />

        <div className="p-3 bg-gray-50 rounded">
          <div className="flex justify-between text-sm mb-1">
            <span className="font-semibold text-navy">Cost</span>
            <span className="font-bold text-navy">{creditCost} credit{creditCost > 1 ? 's' : ''}</span>
          </div>
          <div className="flex justify-between text-xs text-muted">
            <span>Available credits</span>
            <span>{availableCredits}</span>
          </div>
        </div>

        {!hasCredits && (
          <InlineAlert variant="warning">
            You don&apos;t have enough credits. Purchase more credits or choose a different turnaround time.
          </InlineAlert>
        )}

        <Button
          fullWidth
          disabled={!hasCredits}
          onClick={() => onSubmit?.({ turnaround, focusArea, notes, paymentMethod })}
        >
          Submit Review Request ({creditCost} credit{creditCost > 1 ? 's' : ''})
        </Button>
      </div>
    </Drawer>
  );
}
