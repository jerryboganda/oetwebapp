import { describe, it, expect } from 'vitest';
import { signupPayloadSchema } from '../auth/schemas';

const validPayload = {
  agreeToPrivacy: true,
  agreeToTerms: true,
  confirmPassword: 'SuperSecret1',
  countryTarget: 'AU',
  email: 'jane@example.com',
  examTypeId: 'oet',
  firstName: 'Jane',
  lastName: 'Doe',
  marketingOptIn: false,
  mobileNumber: '+61400000000',
  password: 'SuperSecret1',
  professionId: 'nursing',
  sessionId: 'session-1',
};

describe('signupPayloadSchema', () => {
  it('accepts a valid payload', () => {
    const result = signupPayloadSchema.safeParse(validPayload);
    expect(result.success).toBe(true);
  });

  it('rejects an invalid email', () => {
    const result = signupPayloadSchema.safeParse({ ...validPayload, email: 'not-an-email' });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues.some((i) => i.path.includes('email'))).toBe(true);
    }
  });

  it('rejects a too-short password', () => {
    const result = signupPayloadSchema.safeParse({
      ...validPayload,
      password: 'short',
      confirmPassword: 'short',
    });
    expect(result.success).toBe(false);
  });

  it('rejects when password and confirmPassword do not match', () => {
    const result = signupPayloadSchema.safeParse({
      ...validPayload,
      confirmPassword: 'DifferentPassword1',
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      const issue = result.error.issues.find((i) => i.path[0] === 'confirmPassword');
      expect(issue?.message).toBe('Passwords must match');
    }
  });

  it('rejects when terms are not accepted', () => {
    const result = signupPayloadSchema.safeParse({ ...validPayload, agreeToTerms: false });
    expect(result.success).toBe(false);
    if (!result.success) {
      const issue = result.error.issues.find((i) => i.path[0] === 'agreeToTerms');
      expect(issue?.message).toBe('Accept the terms to continue');
    }
  });

  it('rejects when privacy policy is not accepted', () => {
    const result = signupPayloadSchema.safeParse({ ...validPayload, agreeToPrivacy: false });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(
        result.error.issues.some(
          (i) => i.path[0] === 'agreeToPrivacy' && i.message === 'Accept the privacy policy to continue',
        ),
      ).toBe(true);
    }
  });

  it('rejects mobile number that is too short', () => {
    const result = signupPayloadSchema.safeParse({ ...validPayload, mobileNumber: '12345' });
    expect(result.success).toBe(false);
  });

  it('rejects mobile number that is too long', () => {
    const result = signupPayloadSchema.safeParse({
      ...validPayload,
      mobileNumber: '1'.repeat(21),
    });
    expect(result.success).toBe(false);
  });

  it('rejects empty required selections', () => {
    const result = signupPayloadSchema.safeParse({
      ...validPayload,
      examTypeId: '',
      professionId: '',
      sessionId: '',
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      const paths = result.error.issues.map((i) => i.path[0]);
      expect(paths).toContain('examTypeId');
      expect(paths).toContain('professionId');
      expect(paths).toContain('sessionId');
    }
  });

  it('rejects too-short first/last names', () => {
    const result = signupPayloadSchema.safeParse({
      ...validPayload,
      firstName: 'J',
      lastName: 'D',
    });
    expect(result.success).toBe(false);
  });

  it('rejects too-short countryTarget', () => {
    const result = signupPayloadSchema.safeParse({ ...validPayload, countryTarget: 'A' });
    expect(result.success).toBe(false);
  });
});
