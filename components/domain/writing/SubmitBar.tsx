'use client';

import { cn } from '@/lib/utils';
import { Button } from '@/components/ui/button';

export interface SubmitBarSecondaryAction {
  label: string;
  onClick(): void;
  disabled?: boolean;
}

export interface SubmitBarProps {
  canSubmit: boolean;
  submitLabel?: string;
  onSubmit(): void;
  secondaryActions?: SubmitBarSecondaryAction[];
  helperText?: string;
  loading?: boolean;
  className?: string;
}

/**
 * Sticky bottom submit bar. Always reachable in the viewport, respects
 * iOS safe-area-inset-bottom, large thumb-zone target on mobile.
 *
 * Used by writing editor + diagnostic + mock + drill pages.
 */
export function SubmitBar({
  canSubmit,
  submitLabel = 'Submit',
  onSubmit,
  secondaryActions = [],
  helperText,
  loading = false,
  className,
}: SubmitBarProps) {
  return (
    <div
      className={cn(
        'fixed left-0 right-0 bottom-0 z-30 border-t border-border bg-surface/95 backdrop-blur-md shadow-[0_-6px_24px_-12px_rgba(0,0,0,0.15)]',
        'px-4 py-3 sm:px-6',
        // safe-area for iOS
        'pb-[max(env(safe-area-inset-bottom),0.75rem)]',
        className,
      )}
      role="region"
      aria-label="Submit actions"
    >
      <div className="mx-auto flex max-w-5xl items-center justify-between gap-3 flex-wrap">
        <div className="flex-1 min-w-0">
          {helperText ? (
            <p className="text-xs text-muted truncate" id="submit-bar-helper">
              {helperText}
            </p>
          ) : null}
        </div>
        <div className="flex items-center gap-2">
          {secondaryActions.map((action) => (
            <Button
              key={action.label}
              type="button"
              variant="outline"
              size="md"
              onClick={action.onClick}
              disabled={action.disabled || loading}
            >
              {action.label}
            </Button>
          ))}
          <Button
            type="button"
            variant="primary"
            size="md"
            disabled={!canSubmit || loading}
            loading={loading}
            onClick={onSubmit}
            aria-describedby={helperText ? 'submit-bar-helper' : undefined}
          >
            {submitLabel}
          </Button>
        </div>
      </div>
    </div>
  );
}
