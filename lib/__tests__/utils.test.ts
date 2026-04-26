import { describe, it, expect } from 'vitest';
import { cn } from '../utils';

describe('cn', () => {
  it('joins simple class names', () => {
    expect(cn('a', 'b')).toBe('a b');
  });

  it('drops falsy values', () => {
    expect(cn('a', false, null, undefined, 0, '', 'b')).toBe('a b');
  });

  it('handles conditional object syntax', () => {
    expect(cn('a', { b: true, c: false, d: true })).toBe('a b d');
  });

  it('flattens arrays', () => {
    expect(cn(['a', ['b', { c: true }]])).toBe('a b c');
  });

  it('merges conflicting tailwind utilities (last one wins)', () => {
    expect(cn('p-2', 'p-4')).toBe('p-4');
    expect(cn('text-red-500', 'text-blue-500')).toBe('text-blue-500');
  });

  it('preserves non-conflicting tailwind utilities', () => {
    expect(cn('px-2', 'py-4')).toBe('px-2 py-4');
  });

  it('returns an empty string when given no arguments', () => {
    expect(cn()).toBe('');
  });
});
