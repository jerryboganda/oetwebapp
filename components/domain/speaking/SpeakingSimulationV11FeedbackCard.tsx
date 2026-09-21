'use client';

import { useState, type FormEvent } from 'react';
import { Star } from 'lucide-react';

import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/form-controls';
import { ApiError } from '@/lib/api';
import { submitSpeakingSimulationV11Feedback } from '@/lib/api/speaking-simulation-v11';

export function SpeakingSimulationV11FeedbackCard({ sessionId }: { sessionId: string }) {
  const [rating, setRating] = useState(0);
  const [comment, setComment] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (rating < 1 || rating > 5 || submitting) return;

    setSubmitting(true);
    setError(null);
    try {
      await submitSpeakingSimulationV11Feedback(sessionId, rating, comment.trim() || null);
      setSuccess(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.userMessage : 'Could not submit feedback.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section className="mt-6 rounded-xl border border-border bg-surface p-5">
      <h2 className="text-base font-semibold text-foreground">How was this speaking practice?</h2>
      <p className="mt-1 text-sm text-muted">
        Rate the simulation experience. This feedback does not affect your practice score.
      </p>

      <form className="mt-4 space-y-4" onSubmit={(event) => void handleSubmit(event)}>
        <fieldset className="flex items-center gap-1" disabled={submitting}>
          <legend className="sr-only">Speaking practice rating</legend>
          {[1, 2, 3, 4, 5].map((value) => (
            <label
              key={value}
              className="cursor-pointer rounded-full p-1 focus-within:ring-2 focus-within:ring-primary focus-within:ring-offset-2"
            >
              <input
                className="sr-only"
                type="radio"
                name="speaking-practice-rating"
                value={value}
                checked={rating === value}
                onChange={() => {
                  setRating(value);
                  setSuccess(false);
                }}
                aria-label={`${value} star${value === 1 ? '' : 's'}`}
              />
              <Star
                aria-hidden="true"
                className={value <= rating
                  ? 'h-7 w-7 fill-amber-400 text-amber-400'
                  : 'h-7 w-7 text-muted/40'}
              />
            </label>
          ))}
          <span className="ml-2 text-sm text-muted">{rating ? `${rating} / 5` : 'Choose a rating'}</span>
        </fieldset>

        <Textarea
          label="Comments (optional)"
          value={comment}
          onChange={(event) => {
            setComment(event.target.value);
            setSuccess(false);
          }}
          maxLength={2000}
          disabled={submitting}
          hint="Tell us what felt realistic, confusing, or worth improving."
        />

        {error ? <InlineAlert variant="warning">{error}</InlineAlert> : null}
        {success ? <InlineAlert variant="success">Thanks. Your feedback was saved.</InlineAlert> : null}

        <div className="flex justify-end">
          <Button type="submit" loading={submitting} disabled={rating < 1}>
            {success ? 'Update feedback' : 'Submit feedback'}
          </Button>
        </div>
      </form>
    </section>
  );
}
