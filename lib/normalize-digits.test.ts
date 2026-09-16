import { describe, expect, it } from 'vitest';
import { toAsciiDigits } from '@/lib/normalize-digits';

describe('toAsciiDigits', () => {
  it('passes ASCII digits through unchanged', () => {
    expect(toAsciiDigits('123456')).toBe('123456');
  });

  it('normalizes Arabic-Indic digits (U+0660..U+0669)', () => {
    expect(toAsciiDigits('٠١٢٣٤٥٦٧٨٩')).toBe('0123456789');
  });

  it('normalizes Extended Arabic-Indic / Persian digits (U+06F0..U+06F9)', () => {
    expect(toAsciiDigits('۰۱۲۳۴۵۶۷۸۹')).toBe('0123456789');
  });

  // The reported iPad/Safari failure: an Arabic keyboard produced a full code
  // that the old ASCII-only /\D/g filter deleted entirely, leaving the boxes
  // empty while the field was focused and the keyboard was open.
  it('recovers a full six-digit code typed on an Arabic keyboard', () => {
    expect(toAsciiDigits('٤٥٦٧٨٩')).toBe('456789');
    expect(toAsciiDigits('٤٥٦٧٨٩')).toHaveLength(6);
  });

  it('accepts a mixed-script paste', () => {
    expect(toAsciiDigits('12٣٤۵۶')).toBe('123456');
  });

  it('drops separators, letters and bidi control characters', () => {
    expect(toAsciiDigits('12-34 56')).toBe('123456');
    expect(toAsciiDigits('code: 123456')).toBe('123456');
    // U+200F RIGHT-TO-LEFT MARK rides along when copying from an RTL client.
    expect(toAsciiDigits('\u200f١٢٣٤٥٦\u200f')).toBe('123456');
  });

  it('is null-safe', () => {
    expect(toAsciiDigits(null)).toBe('');
    expect(toAsciiDigits(undefined)).toBe('');
    expect(toAsciiDigits('')).toBe('');
  });

  it('does not treat other Unicode number-like characters as digits', () => {
    // Superscripts / circled numbers must not silently become part of a code.
    expect(toAsciiDigits('²³')).toBe('');
    expect(toAsciiDigits('①②')).toBe('');
  });
});
