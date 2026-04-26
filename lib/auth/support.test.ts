import { describe, it, expect } from 'vitest';
import { SUPPORT_EMAIL, buildSupportMailto, buildSupportWhatsAppLink } from './support';

describe('SUPPORT_EMAIL', () => {
  it('points at the documented support inbox', () => {
    expect(SUPPORT_EMAIL).toBe('support@edu80.app');
  });
});

describe('buildSupportMailto', () => {
  it('uses the support email and a fixed subject', () => {
    const link = buildSupportMailto();
    expect(link.startsWith(`mailto:${SUPPORT_EMAIL}?subject=`)).toBe(true);
    const subject = decodeURIComponent(link.split('subject=')[1].split('&')[0]);
    expect(subject).toBe('Need help with my OET account');
  });

  it('omits the email line when no address is provided', () => {
    const body = decodeURIComponent(buildSupportMailto().split('body=')[1]);
    expect(body).toContain('Hello Support Team,');
    expect(body).toContain('I have just completed the account registration flow');
    expect(body).not.toContain('Registered email:');
  });

  it('includes the email line when an address is provided', () => {
    const body = decodeURIComponent(
      buildSupportMailto('learner@example.com').split('body=')[1],
    );
    expect(body).toContain('Registered email: learner@example.com');
  });

  it('percent-encodes special characters in the body', () => {
    const link = buildSupportMailto('a+b@example.com');
    // raw '+' inside an email must be percent-encoded inside the body parameter
    expect(link).toContain('Registered%20email%3A%20a%2Bb%40example.com');
  });
});

describe('buildSupportWhatsAppLink', () => {
  it('returns a wa.me link with a percent-encoded message', () => {
    const link = buildSupportWhatsAppLink();
    expect(link.startsWith('https://wa.me/?text=')).toBe(true);
    const text = decodeURIComponent(link.split('text=')[1]);
    expect(text).toBe('Hello, I need help with my OET account setup.');
  });

  it('appends the registered email when provided', () => {
    const text = decodeURIComponent(
      buildSupportWhatsAppLink('learner@example.com').split('text=')[1],
    );
    expect(text).toContain('Hello, I need help with my OET account setup.');
    expect(text).toContain('Registered email: learner@example.com');
  });

  it('skips the email fragment when omitted', () => {
    const text = decodeURIComponent(
      buildSupportWhatsAppLink().split('text=')[1],
    );
    expect(text).not.toContain('Registered email:');
  });

  it('skips the email fragment when blank', () => {
    const text = decodeURIComponent(
      buildSupportWhatsAppLink('').split('text=')[1],
    );
    expect(text).not.toContain('Registered email:');
  });
});
