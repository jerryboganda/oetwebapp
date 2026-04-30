'use client';

import { cn } from '@/lib/utils';
import { useRef } from 'react';
import { AnimatePresence, MotionConfig, motion, useReducedMotion } from 'motion/react';
import type { Transition } from 'motion/react';
import { motionTokens } from '@/lib/motion';
import { Badge, type BadgeProps } from '@/components/ui/badge';

type SaveStatus = 'idle' | 'saving' | 'saved' | 'offline-saved' | 'failed';

interface WritingEditorProps {
  value: string;
  onChange: (value: string) => void;
  saveStatus?: SaveStatus;
  wordCount?: number;
  wordCountStatus?: {
    label: string;
    message: string;
    variant: NonNullable<BadgeProps['variant']>;
  };
  fontSize?: number;
  onFontSizeChange?: (size: number) => void;
  showFontSizeControls?: boolean;
  placeholder?: string;
  disabled?: boolean;
  spellCheck?: boolean;
  className?: string;
}

const saveStatusLabels: Record<SaveStatus, { label: string; color: string }> = {
  idle: { label: '', color: '' },
  saving: { label: 'Saving...', color: 'text-amber-600' },
  saved: { label: 'Saved', color: 'text-emerald-600' },
  'offline-saved': { label: 'Saved locally', color: 'text-blue-600' },
  failed: { label: 'Save failed', color: 'text-red-600' },
};

export function WritingEditor({
  value, onChange, saveStatus = 'idle', wordCount, wordCountStatus,
  fontSize = 16, onFontSizeChange, placeholder = 'Begin writing your response...', disabled, spellCheck = true, className,
  showFontSizeControls = true,
}: WritingEditorProps) {
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const prefersReducedMotion = useReducedMotion();
  const count = wordCount ?? value.trim().split(/\s+/).filter(Boolean).length;
  const statusTransition: Transition = prefersReducedMotion
    ? { duration: 0.01 }
    : motionTokens.spring.item;

  return (
    <MotionConfig reducedMotion="user">
      <div className={cn('flex flex-col h-full', className)}>
        {/* Toolbar */}
        <div className="flex items-center justify-between shrink-0 border-b border-border bg-background-light px-3 py-2 sm:px-4">
          <div className="flex flex-wrap items-center gap-3 text-xs text-muted">
            <span className="font-semibold">{count} words</span>
            {wordCountStatus ? (
              <Badge variant={wordCountStatus.variant} size="sm" title={wordCountStatus.message}>
                {wordCountStatus.label}
              </Badge>
            ) : null}
            <AnimatePresence initial={false} mode="wait">
              {saveStatus !== 'idle' ? (
                <motion.span
                  key={saveStatus}
                  aria-live="polite"
                  aria-atomic="true"
                  className={cn('font-semibold', saveStatusLabels[saveStatus].color)}
                  initial={{ opacity: 0, y: 4 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -4 }}
                  transition={statusTransition}
                >
                  {saveStatusLabels[saveStatus].label}
                </motion.span>
              ) : null}
            </AnimatePresence>
          </div>
          {onFontSizeChange && showFontSizeControls ? (
            <div className="flex items-center gap-1">
              <button
                className="flex items-center justify-center rounded border border-border min-w-[2.75rem] min-h-[2.75rem] px-3 py-2 text-xs transition-[background-color,border-color,color,transform] duration-150 hover:bg-background-light active:scale-[0.98] disabled:opacity-50"
                onClick={() => onFontSizeChange(Math.max(12, fontSize - 2))}
                disabled={fontSize <= 12}
                aria-label="Decrease font size"
              >
                A-
              </button>
              <span className="w-8 text-center text-xs text-muted">{fontSize}</span>
              <button
                className="flex items-center justify-center rounded border border-border min-w-[2.75rem] min-h-[2.75rem] px-3 py-2 text-xs transition-[background-color,border-color,color,transform] duration-150 hover:bg-background-light active:scale-[0.98] disabled:opacity-50"
                onClick={() => onFontSizeChange(Math.min(24, fontSize + 2))}
                disabled={fontSize >= 24}
                aria-label="Increase font size"
              >
                A+
              </button>
            </div>
          ) : null}
        </div>

        {/* Editor */}
        <textarea
          ref={textareaRef}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          disabled={disabled}
          spellCheck={spellCheck}
          autoCorrect={spellCheck ? 'on' : 'off'}
          autoCapitalize={spellCheck ? 'sentences' : 'off'}
          style={{ fontSize: `${fontSize}px` }}
          className={cn(
            'w-full flex-1 resize-none border-0 bg-white p-4 text-navy leading-relaxed sm:p-6',
            'focus:outline-none placeholder:text-muted/40',
            'disabled:cursor-not-allowed disabled:opacity-50',
          )}
          aria-label="Writing editor"
        />
      </div>
    </MotionConfig>
  );
}
