import type {NextConfig} from 'next';

// NOTE: Content-Security-Policy is emitted by middleware.ts on a per-request basis
// so each response carries a unique nonce. Do NOT add a CSP here — a static CSP
// would either override the nonced one (bad) or be overridden by it (dead code).
// API-origin resolution likewise moved to middleware.ts to co-locate with connect-src.

const nextConfig: NextConfig = {
  reactStrictMode: true,
  images: {
    remotePatterns: [],
  },
  output: 'standalone',
  async redirects() {
    // Phase 2 of the Content Hub consolidation: every legacy /admin/<area>
    // route now lives under /admin/content/<area>. Permanent redirects keep
    // bookmarks, audit-trail URLs, emails, and tests working.
    const moves: Array<[string, string]> = [
      ['/admin/content-papers', '/admin/content/papers'],
      ['/admin/content-hierarchy', '/admin/content/hierarchy'],
      ['/admin/content-import', '/admin/content/import'],
      ['/admin/content-generation', '/admin/content/generation'],
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
          { key: 'Permissions-Policy', value: 'camera=(), microphone=(self), geolocation=()' },
          // Content-Security-Policy is set dynamically by middleware.ts (nonce-based).
        ],
      },
    ];
  },
  serverExternalPackages: ['wavesurfer.js'],
  webpack: (config, {dev}) => {
    // HMR is disabled in AI Studio via DISABLE_HMR env var.
    // Do not modify — file watching is disabled to prevent flickering during agent edits.
    if (dev && process.env.DISABLE_HMR === 'true') {
      config.watchOptions = {
        ignored: /.*/,
      };
    }
    return config;
  },
};

export default nextConfig;
