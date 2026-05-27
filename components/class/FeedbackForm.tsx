'use client';

import { Star } from 'lucide-react';
import { useState, type FormEvent } from 'react';

import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import type { ClassFeedbackSubmitPayload } from '@/lib/api';

export interface FeedbackFormProps {
  onSubmit: (payload: ClassFeedbackSubmitPayload) => Promise<void>;
  submitting?: boolean;
  apiError?: string | null;
  apiSuccess?: string | null;
}

export function FeedbackForm({ onSubmit, submitting = false, apiError, apiSuccess }: FeedbackFormProps) {
  const [rating, setRating] = useState<number>(0);
  const [hoverRating, setHoverRating] = useState<number>(0);
  const [comment, setComment] = useState('');
  const [recommend, setRecommend] = useState<boolean | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setLocalError(null);
    if (rating < 1 || rating > 5) {
      setLocalError('Please choose a rating from 1 to 5 stars.');
      return;
    }
    await onSubmit({
      rating,
      comment: comment.trim() || null,
      recommendToFriend: recommend,
    });
  }

  const stars = [1, 2, 3, 4, 5];
  const displayedRating = hoverRating || rating;

  return (
    <form onSubmit={(e) => void handleSubmit(e)} className="space-y-6">
      {apiError ? <InlineAlert variant="warning">{apiError}</InlineAlert> : null}
      {apiSuccess ? <InlineAlert variant="success">{apiSuccess}</InlineAlert> : null}
      {localError ? <InlineAlert variant="warning">{localError}</InlineAlert> : null}

      <fieldset className="space-y-2">
        <legend className="text-sm font-semibold tracking-tight text-navy">How would you rate this class?</legend>
        <div className="flex items-center gap-1" role="radiogroup" aria-label="Star rating">
          {stars.map((s) => {
            const filled = s <= displayedRating;
            return (
              <button
                key={s}
                type="button"
                onMouseEnter={() => setHoverRating(s)}
                onMouseLeave={() => setHoverRating(0)}
                onFocus={() => setHoverRating(s)}
                onBlur={() => setHoverRating(0)}
                onClick={() => setRating(s)}
                aria-label={`${s} star${s === 1 ? '' : 's'}`}
                aria-pressed={rating === s}
                className="rounded-full p-1 transition-colors focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2"
              >
                <Star
                  className={
                    filled
                      ? 'h-8 w-8 fill-amber-400 text-amber-400'
                      : 'h-8 w-8 text-muted/40'
                  }
                />
              </button>
            );
          })}
          <span className="ml-3 text-sm text-muted">
            {rating === 0 ? 'No rating yet' : `${rating} / 5`}
          </span>
        </div>
      </fieldset>

      <Textarea
        label="Comments (optional)"
        value={comment}
        onChange={(e) => setComment(e.target.value)}
        maxLength={2000}
        hint="Anything specific you’d like the tutor or our team to know."
      />

      <fieldset className="space-y-2 rounded-2xl border border-border bg-surface p-4 shadow-sm">
        <legend className="text-sm font-semibold tracking-tight text-navy">Would you recommend this class to a friend?</legend>
        <div className="flex gap-3">
          <label className="flex items-center gap-2 text-sm text-navy">
            <input
              type="radio"
              name="recommend"
              checked={recommend === true}
              onChange={() => setRecommend(true)}
              className="h-4 w-4 text-primary focus:ring-primary"
            />
            Yes
          </label>
          <label className="flex items-center gap-2 text-sm text-navy">
            <input
              type="radio"
              name="recommend"
              checked={recommend === false}
              onChange={() => setRecommend(false)}
              className="h-4 w-4 text-primary focus:ring-primary"
            />
            No
          </label>
          <label className="flex items-center gap-2 text-sm text-muted">
            <input
              type="radio"
              name="recommend"
              checked={recommend === null}
              onChange={() => setRecommend(null)}
              className="h-4 w-4 text-primary focus:ring-primary"
            />
            Skip
          </label>
        </div>
      </fieldset>

      <div className="flex justify-end">
        <Button type="submit" variant="primary" loading={submitting} disabled={rating < 1}>
          Submit feedback
        </Button>
      </div>
    </form>
  );
}
