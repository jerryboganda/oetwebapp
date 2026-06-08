import type {NextConfig} from 'next';
import createNextIntlPlugin from 'next-intl/plugin';

// Wraps the Next config so server components can call `useTranslations` / `getTranslations`.
// `i18n.ts` at the repo root resolves the active locale from the `lang` cookie or
// `Accept-Language` header per request. The wrapper does not introduce locale-prefixed
// URLs — i18n here is content-only so existing routes stay untouched.
const withNextIntl = createNextIntlPlugin('./i18n.ts');

// NOTE: Content-Security-Policy is emitted by middleware.ts on a per-request basis
// so each response carries a unique nonce. Do NOT add a CSP here — a static CSP
// would either override the nonced one (bad) or be overridden by it (dead code).
// API-origin resolution likewise moved to middleware.ts to co-locate with connect-src.

const readNextBuildWorkers = () => {
  const rawValue = process.env.NEXT_BUILD_WORKERS;
  if (!rawValue) return undefined;

  const parsedValue = Number.parseInt(rawValue, 10);
  return Number.isFinite(parsedValue) && parsedValue > 0 ? parsedValue : undefined;
};

const nextBuildWorkers = readNextBuildWorkers();

const nextConfig: NextConfig = {
  experimental: {
    // FE-038: rewrite barrel imports (lucide-react is imported by ~250 files,
    // plus tabler/recharts/motion) to deep imports so unused members tree-shake
    // out of each route bundle.
    optimizePackageImports: ['lucide-react', '@tabler/icons-react', 'recharts', 'motion'],
    ...(nextBuildWorkers ? { cpus: nextBuildWorkers } : {}),
  },
  reactStrictMode: true,
  typescript: { ignoreBuildErrors: true },
  images: {
    remotePatterns: [],
  },
  output: 'standalone',
  // next-intl bundles live under messages/ and are dynamically imported in
  // i18n.ts. Next.js' file-tracing misses them in standalone builds (the
  // bundler resolves the dynamic specifier statically but the JSON
  // assets are not copied into .next/standalone). Force-include them so
  // every server-rendered page in the container can resolve translations.
  outputFileTracingIncludes: {
    '/**': ['./messages/**/*.json', './i18n.ts'],
  },
  async redirects() {
    // Phase 2 of the Content Hub consolidation: every legacy /admin/<area>
    // route now lives under /admin/content/<area>. Permanent redirects keep
    // bookmarks, audit-trail URLs, emails, and tests working.
    const moves: Array<[string, string]> = [
      ['/admin/content-papers', '/admin/content/papers'],
      ['/admin/content-hierarchy', '/admin/content/hierarchy'],
      ['/admin/content-import', '/admin/content/import'],
      ['/admin/content-generation', '/admin/content/generation'],
      ['/admin/content-analytics', '/admin/content/analytics'],
      ['/admin/content-quality', '/admin/content/quality'],
      ['/admin/grammar', '/admin/content/grammar'],
      ['/admin/pronunciation', '/admin/content/pronunciation'],
      ['/admin/strategies', '/admin/content/strategies'],
      ['/admin/media', '/admin/content/media'],
      ['/admin/dedup', '/admin/content/dedup'],
      ['/admin/publish-requests', '/admin/content/publish-requests'],
    ];
    return moves.flatMap(([source, destination]) => [
      { source, destination, permanent: true },
      { source: `${source}/:path*`, destination: `${destination}/:path*`, permanent: true },
    ]).concat([
      // Recalls consolidation — see docs/RECALLS-MODULE-PLAN.md.
      // Only the bare landing routes redirect; deeper /vocabulary/* tooling
      // pages remain accessible until their feature parity ships under /recalls.
      { source: '/vocabulary', destination: '/recalls/words', permanent: true },
      { source: '/vocabulary/browse', destination: '/recalls/words', permanent: true },
      { source: '/vocabulary/flashcards', destination: '/recalls/words', permanent: true },
      { source: '/vocabulary/quiz', destination: '/recalls/words', permanent: true },
      { source: '/review', destination: '/recalls/words', permanent: true },
    ]);
  },
  async headers() {
    return [
      {
        source: '/(.*)',
        headers: [
          { key: 'X-Content-Type-Options', value: 'nosniff' },
          { key: 'X-Frame-Options', value: 'DENY' },
          { key: 'Referrer-Policy', value: 'strict-origin-when-cross-origin' },
          { key: 'Permissions-Policy', value: 'camera=(self), microphone=(self), geolocation=()' },
          // Content-Security-Policy is set dynamically by middleware.ts (nonce-based).
        ],
      },
    ];
  },
  serverExternalPackages: ['wavesurfer.js'],
};

export default withNextIntl(nextConfig);
