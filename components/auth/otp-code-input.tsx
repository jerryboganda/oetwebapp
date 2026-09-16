'use client';

import { useRef } from 'react';
import { toAsciiDigits } from '@/lib/normalize-digits';
import styles from './auth-screen-shell.module.scss';

interface OtpCodeInputProps {
  value: string;
  onChange: (value: string) => void;
  length?: number;
  disabled?: boolean;
  /** Accessible name for the group; digits get `${id}-digit-N` aria-labels. */
  id?: string;
  autoFocus?: boolean;
}

function updateCodeAtIndex(code: string, index: number, character: string, length: number): string {
  const next = Array.from({ length }, (_, slot) => code[slot] ?? '');
  next[index] = character;
  return next.join('');
}

export function OtpCodeInput({ value, onChange, length = 6, disabled = false, id, autoFocus = false }: OtpCodeInputProps) {
  const inputRefs = useRef<Array<HTMLInputElement | null>>([]);

  const focusIndex = (index: number) => {
    inputRefs.current[index]?.focus();
    inputRefs.current[index]?.select();
  };

  const distributeDigits = (digits: string, fromIndex: number) => {
    // A complete code always fills from the first box, wherever it entered —
    // OS one-time-code AutoFill and a paste both land on whichever box happens
    // to have focus, and the learner means "this is the whole code".
    const startIndex = digits.length >= length ? 0 : fromIndex;
    const next = Array.from({ length }, (_, slot) => value[slot] ?? '');

    digits.split('').forEach((digit, offset) => {
      const targetIndex = startIndex + offset;
      if (targetIndex < length) {
        next[targetIndex] = digit;
      }
    });

    onChange(next.join(''));

    const focusTarget = Math.min(startIndex + digits.length, length - 1);
    focusIndex(focusTarget);
  };

  return (
    <div className={styles.otpGrid}>
      {Array.from({ length }, (_, index) => (
        <input
          key={index}
          ref={(element) => {
            inputRefs.current[index] = element;
          }}
          className={styles.otpInput}
          type="text"
          inputMode="numeric"
          pattern="[0-9]*"
          enterKeyHint="done"
          // Every box advertises one-time-code so OS AutoFill works whichever
          // box has focus; a full code delivered to any box fills them all.
          autoComplete="one-time-code"
          // NOT maxLength={1}: that cap blocks typing into an already-filled box
          // (tap-to-correct on iPad) and truncates a six-digit AutoFill to one
          // character. Selecting on focus makes typing replace instead.
          maxLength={length}
          value={value[index] ?? ''}
          disabled={disabled}
          aria-label={id ? `${id}-digit-${index + 1}` : `OTP digit ${index + 1}`}
          autoFocus={autoFocus && index === 0}
          onFocus={(event) => event.target.select()}
          onChange={(event) => {
            // Normalize BEFORE filtering: `\D` is ASCII-only, so an Arabic
            // keyboard's digits would otherwise be deleted as "not a digit"
            // and the box would stay empty while the keyboard is open.
            let digits = toAsciiDigits(event.target.value);

            if (!digits) {
              onChange(updateCodeAtIndex(value, index, '', length));
              return;
            }

            // Typing into a box that already holds a digit (tap-to-correct)
            // yields two characters — the kept one plus the new one, in caret
            // order. Keep only the new one instead of spilling into the next
            // box. A longer run is a real paste and falls through.
            const existing = value[index] ?? '';
            if (existing && digits.length === 2) {
              if (digits[0] === existing) digits = digits.slice(1);
              else if (digits[1] === existing) digits = digits.slice(0, 1);
            }

            if (digits.length > 1) {
              distributeDigits(digits, index);
              return;
            }

            onChange(updateCodeAtIndex(value, index, digits, length));
            if (index < length - 1) {
              focusIndex(index + 1);
            }
          }}
          onKeyDown={(event) => {
            if (event.key === 'Backspace' && !(value[index] ?? '') && index > 0) {
              event.preventDefault();
              onChange(updateCodeAtIndex(value, index - 1, '', length));
              focusIndex(index - 1);
            }

            if (event.key === 'ArrowLeft' && index > 0) {
              event.preventDefault();
              focusIndex(index - 1);
            }

            if (event.key === 'ArrowRight' && index < length - 1) {
              event.preventDefault();
              focusIndex(index + 1);
            }
          }}
          onPaste={(event) => {
            const digits = toAsciiDigits(event.clipboardData.getData('text'));
            if (!digits) {
              return;
            }

            event.preventDefault();
            distributeDigits(digits.slice(0, length), index);
          }}
        />
      ))}
    </div>
  );
}
