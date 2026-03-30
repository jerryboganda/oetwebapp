'use client';

import { useRef } from 'react';
import styles from './auth-screen-shell.module.scss';

interface OtpCodeInputProps {
  value: string;
  onChange: (value: string) => void;
  length?: number;
  disabled?: boolean;
}

function updateCodeAtIndex(code: string, index: number, character: string, length: number): string {
  const next = Array.from({ length }, (_, slot) => code[slot] ?? '');
  next[index] = character;
  return next.join('');
}

export function OtpCodeInput({ value, onChange, length = 6, disabled = false }: OtpCodeInputProps) {
  const inputRefs = useRef<Array<HTMLInputElement | null>>([]);

  const focusIndex = (index: number) => {
    inputRefs.current[index]?.focus();
    inputRefs.current[index]?.select();
  };

  const distributeDigits = (digits: string, startIndex: number) => {
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
          inputMode="numeric"
          autoComplete={index === 0 ? 'one-time-code' : 'off'}
          maxLength={1}
          value={value[index] ?? ''}
          disabled={disabled}
          aria-label={`OTP digit ${index + 1}`}
          onChange={(event) => {
            const digits = event.target.value.replace(/\D/g, '');

            if (!digits) {
              onChange(updateCodeAtIndex(value, index, '', length));
              return;
            }

            if (digits.length > 1) {
              distributeDigits(digits.slice(0, length - index), index);
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
            const digits = event.clipboardData.getData('text').replace(/\D/g, '');
            if (!digits) {
              return;
            }

            event.preventDefault();
            distributeDigits(digits.slice(0, length - index), index);
          }}
        />
      ))}
    </div>
  );
}
