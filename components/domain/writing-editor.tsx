'use client';

import { cn } from '@/lib/utils';
import { useRef } from 'react';
import { AnimatePresence, MotionConfig, motion, useReducedMotion } from 'motion/react';
import type { Transition } from 'motion/react';
import { motionTokens } from '@/lib/motion';

type SaveStatus = 'idle' | 'saving' | 'saved' | 'offline-saved' | 'failed';

interface WritingEditorProps {
  value: string;
  onChange: (value: string) => void;
  saveStatus?: SaveStatus;
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
  value, onChange, saveStatus = 'idle',
  fontSize = 16, onFontSizeChange, placeholder = 'Begin writing your response...', disabled, spellCheck = true, className,
  showFontSizeControls = true,
}: WritingEditorProps) {
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const prefersReducedMotion = useReducedMotion();
  const statusTransition: Transition = prefersReducedMotion
    ? { duration: 0.01 }
    : motionTokens.spring.item;

  return (
    <MotionConfig reducedMotion="user">
      <div className={cn('flex flex-col h-full bg-white/40 backdrop-blur-3xl relative', className)}>
        <div className="absolute inset-0 bg-gradient-to-br from-primary/5 via-transparent to-transparent opacity-50 pointer-events-none -z-10" />
        {/* Toolbar */}
        <div className="flex items-center justify-between shrink-0 border-b border-border/40 bg-white/60 backdrop-blur-md px-4 py-3 sm:px-6 z-10 transition-all shadow-[0_4px_24px_-12px_rgba(0,0,0,0.05)]">
          <div className="flex flex-wrap items-center gap-3 text-xs text-navy/70">
            <AnimatePresence initial={false} mode="wait">
              {saveStatus !== 'idle' ? (
                <motion.span
                  key={saveStatus}
                  aria-live="polite"
                  aria-atomic="true"
                  className={cn('font-bold px-2 py-1 rounded bg-white/50 backdrop-blur-sm', saveStatusLabels[saveStatus].color)}
                  initial={{ opacity: 0, scale: 0.95, y: 2 }}
                  animate={{ opacity: 1, scale: 1, y: 0 }}
                  exit={{ opacity: 0, scale: 0.95, y: -2 }}
                  transition={statusTransition}
                >
                  {saveStatusLabels[saveStatus].label}
                </motion.span>
              ) : null}
            </AnimatePresence>
          </div>
          {onFontSizeChange && showFontSizeControls ? (
            <div className="flex items-center bg-white/80 backdrop-blur rounded-full p-1 shadow-sm ring-1 ring-black/5 gap-1">
              <button
                className="flex items-center justify-center rounded-full w-7 h-7 text-xs font-bold transition-all duration-200 hover:bg-navy/5 hover:text-navy active:scale-95 disabled:opacity-30 text-navy/60"
                onClick={() => onFontSizeChange(Math.max(12, fontSize - 2))}
                disabled={fontSize <= 12}
                aria-label="Decrease font size"
              >
                A-
              </button>
              <span className="w-8 text-center text-xs font-bold text-navy">{fontSize}</span>
              <button
                className="flex items-center justify-center rounded-full w-7 h-7 text-[13px] font-black transition-all duration-200 hover:bg-navy/5 hover:text-navy active:scale-95 disabled:opacity-30 text-navy/60"
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
            'w-full flex-1 resize-none border-0 bg-transparent p-5 text-navy leading-relaxed sm:p-8 z-10',
            'focus:outline-none focus:ring-0 placeholder:text-muted/30 transition-all',
            'disabled:cursor-not-allowed disabled:opacity-50',
          )}
          aria-label="Writing editor"
        />
      </div>
    </MotionConfig>
  );
}
