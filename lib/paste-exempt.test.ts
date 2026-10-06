import { describe, expect, it } from 'vitest';

import { isPasteExempt } from './paste-exempt';

const EXEMPT_EMAILS = [
  'drahmedheshamuk2025@gmail.com',
  'drahmedhesham9595@gmail.com',
  'ahmedibrahimabdrabuibrahim@gmail.com',
  'drahmedhesham.work@gmail.com',
  'tutorcommerceacademy2026@gmail.com',
];

describe('isPasteExempt', () => {
  it('matches each of the five exempt accounts, case-insensitively and trimmed', () => {
    for (const email of EXEMPT_EMAILS) {
      expect(isPasteExempt({ email })).toBe(true);
      expect(isPasteExempt({ email: `  ${email.toUpperCase()} ` })).toBe(true);
    }
  });

  it('trusts the server flag even when the email is not on the fallback list', () => {
    expect(isPasteExempt({ email: 'learner@oet-prep.dev', writingUnrestricted: true })).toBe(true);
  });

  it('rejects every other account, with no dot or plus folding', () => {
    expect(isPasteExempt({ email: 'learner@oet-prep.dev' })).toBe(false);
    expect(isPasteExempt({ email: 'learner@oet-prep.dev', writingUnrestricted: false })).toBe(false);
    expect(isPasteExempt({ email: 'xdrahmedheshamuk2025@gmail.com' })).toBe(false);
    expect(isPasteExempt({ email: 'drahmedhesham.9595@gmail.com' })).toBe(false);
    expect(isPasteExempt({ email: 'drahmedhesham9595+test@gmail.com' })).toBe(false);
    expect(isPasteExempt(null)).toBe(false);
    expect(isPasteExempt(undefined)).toBe(false);
    expect(isPasteExempt({ email: '' })).toBe(false);
  });
});
