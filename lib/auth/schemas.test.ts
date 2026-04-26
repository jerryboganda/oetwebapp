import { describe, it, expect } from 'vitest';
import { signupPayloadSchema } from './schemas';

const baseValid = {
  agreeToPrivacy: true,
  agreeToTerms: true,
  password: 'password1',
  confirmPassword: 'password1',
  countryTarget: 'GB',
  email: 'jane.doe@example.com',
  examTypeId: 'oet',
  firstName: 'Jane',
  lastName: 'Doe',
  marketingOptIn: false,
  mobileNumber: '+447123456789',
  professionId: 'nursing',
  sessionId: 'evening',
};

describe('signupPayloadSchema', () => {
  it('accepts a fully valid payload', () => {
    const result = signupPayloadSchema.safeParse(baseValid);
    expect(result.success).toBe(true);
  });

  it.each([
    ['firstName', '', 'First name is required'],
    ['lastName', 'D', 'Last name is required'],
    ['countryTarget', 'X', 'Select your target country'],
    ['examTypeId', '', 'Select an exam'],
    ['professionId', '', 'Select a profession'],
    ['sessionId', '', 'Select a session'],
  ])('rejects invalid %s', (field, value, expectedMsg) => {
    const result = signupPayloadSchema.safeParse({ ...baseValid, [field]: value });
    expect(result.success).toBe(false);
    if (!result.success) {
      const issue = result.error.issues.find((i) => i.path[0] === field);
      expect(issue?.message).toBe(expectedMsg);
    }
  });

  it('rejects an invalid email', () => {
    const result = signupPayloadSchema.safeParse({ ...baseValid, email: 'not-an-email' });
    expect(result.success).toBe(false);
    if (!result.success) {
      const issue = result.error.issues.find((i) => i.path[0] === 'email');
      expect(issue?.message).toBe('Enter a valid email address');
    }
  });

  it('rejects passwords shorter than 8 characters', () => {
    const result = signupPayloadSchema.safeParse({
      ...baseValid,
      password: 'short',
      confirmPassword: 'short',
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      const passIssue = result.error.issues.find((i) => i.path[0] === 'password');
      expect(passIssue?.message).toBe('Password must be at least 8 characters');
    }
  });

  it('rejects when passwords do not match', () => {
    const result = signupPayloadSchema.safeParse({
      ...baseValid,
      password: 'password1',
      confirmPassword: 'password2',
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      const issue = result.error.issues.find((i) => i.path[0] === 'confirmPassword');
      expect(issue?.message).toBe('Passwords must match');
    }
  });

  it('rejects when terms are not accepted', () => {
    const result = signupPayloadSchema.safeParse({ ...baseValid, agreeToTerms: false });
    expect(result.success).toBe(false);
    if (!result.success) {
      const issue = result.error.issues.find((i) => i.path[0] === 'agreeToTerms');
      expect(issue?.message).toBe('Accept the terms to continue');
    }
  });

  it('rejects when privacy is not accepted', () => {
    const result = signupPayloadSchema.safeParse({ ...baseValid, agreeToPrivacy: false });
    expect(result.success).toBe(false);
    if (!result.success) {
      const issue = result.error.issues.find((i) => i.path[0] === 'agreeToPrivacy');
      expect(issue?.message).toBe('Accept the privacy policy to continue');
    }
  });

  it('rejects when mobile number is too short', () => {
    const result = signupPayloadSchema.safeParse({ ...baseValid, mobileNumber: '123' });
    expect(result.success).toBe(false);
    if (!result.success) {
      const issue = result.error.issues.find((i) => i.path[0] === 'mobileNumber');
      expect(issue?.message).toBe('Mobile number is required');
    }
  });

  it('rejects when mobile number is too long', () => {
    const result = signupPayloadSchema.safeParse({
      ...baseValid,
      mobileNumber: '+'.padEnd(25, '1'),
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      const issue = result.error.issues.find((i) => i.path[0] === 'mobileNumber');
      expect(issue?.message).toBe('Enter a valid mobile number');
    }
  });

  it('accepts marketingOptIn=true', () => {
    const result = signupPayloadSchema.safeParse({ ...baseValid, marketingOptIn: true });
    expect(result.success).toBe(true);
  });

  it('rejects when required boolean fields are not booleans', () => {
    const result = signupPayloadSchema.safeParse({
      ...baseValid,
      agreeToTerms: 'yes' as unknown as boolean,
    });
    expect(result.success).toBe(false);
  });

  it('reports multiple validation errors at once', () => {
    const result = signupPayloadSchema.safeParse({
      ...baseValid,
      firstName: '',
      email: 'bad',
      password: 'a',
      confirmPassword: 'b',
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues.length).toBeGreaterThanOrEqual(3);
    }
  });
});
