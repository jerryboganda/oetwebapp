import { NextRequest } from 'next/server';
import { describe, expect, it } from 'vitest';

import { middleware } from './middleware';

describe('middleware mobile association files', () => {
  it.each([
    '/.well-known/apple-app-site-association',
    '/.well-known/assetlinks.json',
  ])('allows %s without authentication', (pathname) => {
    const response = middleware(new NextRequest(`https://app.oetwithdrhesham.co.uk${pathname}`));

    expect(response.status).not.toBe(307);
    expect(response.headers.get('location')).toBeNull();
  });
});

describe('middleware sponsor launch gate', () => {
  it('redirects sponsor routes to support while the sponsor portal is disabled', () => {
    const response = middleware(new NextRequest('https://app.oetwithdrhesham.co.uk/sponsor/billing'));

    expect(response.status).toBe(307);
    expect(response.headers.get('location')).toBe('https://app.oetwithdrhesham.co.uk/support');
  });
});
