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
  experimental: {
    // Tree-shake barrel imports on-demand to avoid loading the entire icon/chart/motion
    // library when only a few exports are used. Documented Next.js optimization:
    // https://nextjs.org/docs/app/api-reference/next-config-js/optimizePackageImports
    optimizePackageImports: [
      'lucide-react',
      '@tabler/icons-react',
      'recharts',
      'motion',
      'motion/react',
      'country-flag-icons',
    ],
  },
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
