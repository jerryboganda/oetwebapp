import { describe, it, expect } from 'vitest';
import { maskProviderId } from '@/components/domain/billing';

describe('maskProviderId', () => {
  it('returns empty string for nullish or empty input', () => {
    expect(maskProviderId(null)).toBe('');
    expect(maskProviderId(undefined)).toBe('');
    expect(maskProviderId('')).toBe('');
  });

  it('masks short tokens to *** without revealing them', () => {
    expect(maskProviderId('abc')).toBe('***');
    expect(maskProviderId('12345678')).toBe('***');
  });

  it('preserves the conventional Stripe prefix and last 4 chars', () => {
    expect(maskProviderId('cus_NfFq2HxLkTjuPo')).toBe('cus_***juPo');
    expect(maskProviderId('sub_1NXyHk2eZvKYlo2C')).toBe('sub_***lo2C');
    expect(maskProviderId('pi_3Abc12345Def')).toBe('pi_***5Def');
  });

  it('falls back to head/tail masking when there is no short prefix', () => {
    expect(maskProviderId('PAYID-ABC123XYZ987')).toBe('PAYI***Z987');
  });

  it('does not leak the full token in any branch', () => {
    const ids = [
      'cus_NfFq2HxLkTjuPo',
      'sub_1NXyHk2eZvKYlo2C',
      'in_1NXyHk2eZvKYlo2CABCDEFGH',
      'PAYID-ABC123XYZ987',
    ];
    for (const id of ids) {
      const masked = maskProviderId(id);
      expect(masked).not.toBe(id);
      expect(masked).toContain('***');
    }
  });
});
