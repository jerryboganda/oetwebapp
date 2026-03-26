'use client';

import { cn } from '@/lib/utils';
import { useRef, useEffect, useState, useCallback } from 'react';

type SaveStatus = 'idle' | 'saving' | 'saved' | 'offline-saved' | 'failed';

interface WritingEditorProps {
  value: string;
  onChange: (value: string) => void;
  saveStatus?: SaveStatus;
  wordCount?: number;
  fontSize?: number;
  onFontSizeChange?: (size: number) => void;
  placeholder?: string;
  disabled?: boolean;
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
  value, onChange, saveStatus = 'idle', wordCount,
  fontSize = 16, onFontSizeChange, placeholder = 'Begin writing your response...', disabled, className,
}: WritingEditorProps) {
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const count = wordCount ?? value.trim().split(/\s+/).filter(Boolean).length;

  return (
    <div className={cn('flex flex-col h-full', className)}>
      {/* Toolbar */}
      <div className="flex items-center justify-between px-4 py-2 bg-gray-50 border-b border-gray-200 shrink-0">
        <div className="flex items-center gap-3 text-xs text-muted">
          <span className="font-semibold">{count} words</span>
          {saveStatus !== 'idle' && (
            <span className={cn('font-semibold', saveStatusLabels[saveStatus].color)}>
              {saveStatusLabels[saveStatus].label}
            </span>
          )}
        </div>
        {onFontSizeChange && (
          <div className="flex items-center gap-1">
            <button
              className="px-2 py-0.5 text-xs border border-gray-200 rounded hover:bg-gray-100 disabled:opacity-50"
              onClick={() => onFontSizeChange(Math.max(12, fontSize - 2))}
              disabled={fontSize <= 12}
              aria-label="Decrease font size"
            >
              A-
            </button>
            <span className="text-xs text-muted w-8 text-center">{fontSize}</span>
            <button
              className="px-2 py-0.5 text-xs border border-gray-200 rounded hover:bg-gray-100 disabled:opacity-50"
              onClick={() => onFontSizeChange(Math.min(24, fontSize + 2))}
              disabled={fontSize >= 24}
              aria-label="Increase font size"
            >
              A+
            </button>
          </div>
        )}
      </div>

      {/* Editor */}
      <textarea
        ref={textareaRef}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        disabled={disabled}
        style={{ fontSize: `${fontSize}px` }}
        className={cn(
          'flex-1 w-full p-6 resize-none border-0 bg-white text-navy leading-relaxed',
          'focus:outline-none placeholder:text-muted/40',
          'disabled:opacity-50 disabled:cursor-not-allowed',
        )}
        aria-label="Writing editor"
      />
    </div>
  );
}
