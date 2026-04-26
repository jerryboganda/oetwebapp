import { describe, it, expect } from 'vitest';
import { SUPPORT_EMAIL, buildSupportMailto, buildSupportWhatsAppLink } from '../auth/support';

describe('buildSupportMailto', () => {
  it('uses the canonical support email', () => {
    expect(SUPPORT_EMAIL).toBe('support@edu80.app');
    expect(buildSupportMailto()).toMatch(/^mailto:support@edu80\.app\?/);
  });

  it('encodes the subject', () => {
    const link = buildSupportMailto();
    expect(link).toContain('subject=' + encodeURIComponent('Need help with my OET account'));
  });

  it('includes the registered email in the body when provided', () => {
    const link = buildSupportMailto('jane@example.com');
    const body = decodeURIComponent(link.split('body=')[1] ?? '');
    expect(body).toContain('Registered email: jane@example.com');
  });

  it('omits the email line when no email is provided', () => {
    const link = buildSupportMailto();
    const body = decodeURIComponent(link.split('body=')[1] ?? '');
    expect(body).not.toContain('Registered email:');
  });

  it('escapes special characters via encodeURIComponent', () => {
    const link = buildSupportMailto('a+b@example.com');
    expect(link).toContain(encodeURIComponent('Registered email: a+b@example.com'));
  });
});

describe('buildSupportWhatsAppLink', () => {
  it('produces a wa.me link', () => {
    expect(buildSupportWhatsAppLink()).toMatch(/^https:\/\/wa\.me\/\?text=/);
  });

  it('includes the email in the message when provided', () => {
    const link = buildSupportWhatsAppLink('jane@example.com');
    expect(decodeURIComponent(link)).toContain('Registered email: jane@example.com');
  });

  it('omits the email portion when not provided', () => {
    const link = buildSupportWhatsAppLink();
    expect(decodeURIComponent(link)).not.toContain('Registered email:');
  });
});
