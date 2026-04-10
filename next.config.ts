import type {NextConfig} from 'next';

function getOrigin(value: string | undefined, fallback: string): string {
  try {
    return new URL(value ?? fallback).origin;
  } catch {
    return new URL(fallback).origin;
  }
}

function expandLoopbackOrigins(origin: string): string[] {
  try {
    const url = new URL(origin);
    if (url.hostname !== '127.0.0.1' && url.hostname !== 'localhost') {
      return [url.origin];
    }

    const normalizedPort = url.port ? `:${url.port}` : '';
    return [
      `${url.protocol}//127.0.0.1${normalizedPort}`,
      `${url.protocol}//localhost${normalizedPort}`,
    ];
  } catch {
    return [origin];
  }
}

const defaultApiOrigin = process.env.API_PROXY_TARGET_URL ?? 'http://127.0.0.1:5198';
const apiOrigin = getOrigin(process.env.NEXT_PUBLIC_API_BASE_URL, defaultApiOrigin);
const apiOrigins = Array.from(new Set(expandLoopbackOrigins(apiOrigin)));
const apiWebSocketOrigins = apiOrigins.map((origin) =>
  origin.startsWith('https://')
    ? `wss://${origin.slice('https://'.length)}`
    : origin.startsWith('http://')
      ? `ws://${origin.slice('http://'.length)}`
      : origin
);

const isProductionBuild = process.env.NODE_ENV === 'production';
const scriptSrcDirective = isProductionBuild
  ? "script-src 'self' 'unsafe-inline'"
  : "script-src 'self' 'unsafe-inline' 'unsafe-eval'";

const nextConfig: NextConfig = {
  reactStrictMode: true,
  eslint: {
    ignoreDuringBuilds: true,
  },
  typescript: {
    ignoreBuildErrors: true,
  },
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
          {
            key: 'Content-Security-Policy',
            value: [
              "default-src 'self'",
              scriptSrcDirective,
              "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
              "font-src 'self' https://fonts.gstatic.com",
              "img-src 'self' data: blob:",
              `connect-src 'self' blob: ${apiOrigins.join(' ')} ${apiWebSocketOrigins.join(' ')} https://*.googleapis.com`,
              `media-src 'self' blob: ${apiOrigins.join(' ')}`,
              "worker-src 'self' blob:",
              "frame-src 'self'",
              "object-src 'none'",
              "base-uri 'self'",
              "form-action 'self'",
            ].join('; '),
          },
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
