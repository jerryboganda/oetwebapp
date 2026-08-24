import type { MetadataRoute } from 'next';

const PUBLIC_ORIGIN = 'https://app.oetwithdrhesham.co.uk';

const PUBLIC_PATHS = [
  '/sign-in',
  '/register',
  '/forgot-password',
  '/reset-password',
  '/privacy',
  '/terms',
  '/support',
  '/get-app',
  '/exam-guide',
] as const;

export default function sitemap(): MetadataRoute.Sitemap {
  return PUBLIC_PATHS.map((path) => ({
    url: `${PUBLIC_ORIGIN}${path}`,
    changeFrequency: path === '/exam-guide' ? 'monthly' : 'weekly',
    priority: path === '/sign-in' || path === '/register' ? 0.8 : 0.5,
  }));
}
