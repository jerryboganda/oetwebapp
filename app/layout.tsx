import type { Metadata, Viewport } from 'next';
import { headers } from 'next/headers';
import { Fraunces, Manrope, Montserrat } from 'next/font/google';
import { getRuntimeBootstrapScript } from '@/lib/runtime-signals';
import { loadAllMessages, resolveLocale } from '@/i18n';
import { AppProviders } from './providers';
import './globals.css';

// Middleware injects a per-request CSP nonce into `x-nonce` + the
// `Content-Security-Policy` response header. Next.js only stamps that nonce
// onto <script> tags when a page is rendered *per-request*. If any page
// (including this layout) is prerendered/cached, the HTML ships without a
// nonce but the response CSP still requires one, so every script is blocked
// by the browser and the client never hydrates (blank page). See:
// https://nextjs.org/docs/app/building-your-application/configuring/content-security-policy#adding-a-nonce-with-middleware
export const dynamic = 'force-dynamic';

const bodyFont = Manrope({
  subsets: ['latin'],
  variable: '--font-sans',
  display: 'swap',
});

const displayFont = Fraunces({
  subsets: ['latin'],
  variable: '--font-display',
  display: 'swap',
});

// Admin UI typography (consumed by `app/admin/_design/admin-tokens.css`
// via the `--font-montserrat` CSS variable on <html>).
const adminFont = Montserrat({
  subsets: ['latin'],
  weight: ['400', '500', '600', '700'],
  variable: '--font-montserrat',
  display: 'swap',
});

const zoomHttpOrigins = ['https://zoom.us', 'https://*.zoom.us', 'https://zoom.com', 'https://*.zoom.com', 'https://source.zoom.us'];
const zoomWebSocketOrigins = ['wss://zoom.us', 'wss://*.zoom.us', 'wss://zoom.com', 'wss://*.zoom.com'];
// PayPal embedded checkout SDK + Smart Buttons + card-field iframes. Must match the
// middleware.ts response-header CSP — the browser enforces the INTERSECTION of this
// meta CSP and the header, so omitting PayPal here blocks the SDK even though the
// header allows it (the "script-src ... violates" console error on /checkout/review).
const paypalHttpOrigins = ['https://*.paypal.com', 'https://*.paypalobjects.com', 'https://*.venmo.com'];
// Bunny Stream CDN — Video Library HLS playback. hls.js fetches the playlist + segments
// from the pull-zone host (vz-*.b-cdn.net) via connect-src. Must match the middleware.ts
// response-header CSP: the browser enforces the INTERSECTION of this meta CSP and the
// header, so omitting Bunny here blocks EVERY native video (connect-src violation →
// hls.js manifestLoadError code=0 → black frame) even though the header allows it.
const bunnyOrigins = ['https://*.b-cdn.net', 'https://video.bunnycdn.com'];

const metaScriptSrc =
  process.env.NODE_ENV === 'production'
    ? `script-src 'self' 'unsafe-inline' ${zoomHttpOrigins.join(' ')} ${paypalHttpOrigins.join(' ')}`
    : `script-src 'self' 'unsafe-inline' 'unsafe-eval' ${zoomHttpOrigins.join(' ')} ${paypalHttpOrigins.join(' ')}`;

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

export const metadata: Metadata = {
  title: 'OET with Dr Hesham: Learner Platform',
  description: 'Prepare for the Occupational English Test with personalised practice, AI feedback, and tutor review.',
  manifest: '/manifest.json',
  icons: {
    icon: [
      { url: '/icon.png', sizes: '32x32', type: 'image/png' },
      { url: '/icon-192.png', sizes: '192x192', type: 'image/png' },
      { url: '/icon-512.png', sizes: '512x512', type: 'image/png' },
    ],
    apple: [{ url: '/apple-icon.png', sizes: '180x180', type: 'image/png' }],
  },
  appleWebApp: {
    capable: true,
    title: 'OET with Dr Hesham',
  },
  openGraph: {
    title: 'OET with Dr Hesham: Learner Platform',
    description: 'Prepare for the OET with personalised practice, AI feedback, and tutor review.',
    type: 'website',
  },
  robots: { index: true, follow: true },
};

export const viewport: Viewport = {
  width: 'device-width',
  initialScale: 1,
  maximumScale: 5,
  viewportFit: 'cover',
  themeColor: [
    { media: '(prefers-color-scheme: light)', color: '#f7f5ef' },
    { media: '(prefers-color-scheme: dark)', color: '#07111d' },
  ],
};

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  const nonce = (await headers()).get('x-nonce') ?? undefined;
  // Resolve the active locale + load message bundles on the server so the
  // `NextIntlClientProvider` rendered inside `AppProviders` can hand them to
  // every client component beneath. Pages that don't call `useTranslations`
  // are unaffected — existing English-inline pages keep rendering as-is.
  const locale = await resolveLocale();
  const messages = await loadAllMessages(locale);
  const direction = locale === 'ar' ? 'rtl' : 'ltr';
  return (
    <html lang={locale} dir={direction} className={`${bodyFont.variable} ${displayFont.variable} ${adminFont.variable}`} suppressHydrationWarning>
      <head>
        {/*
          CSP for Capacitor WebView and web — restrict script/style/connect sources.
          `frame-ancestors` is intentionally enforced only by response headers
          in next.config.ts; browsers ignore it when delivered through meta CSP.
        */}
        <meta
          httpEquiv="Content-Security-Policy"
          content={`default-src 'self'; ${metaScriptSrc}; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: blob: https:; connect-src 'self' blob: ${apiOrigins.join(' ')} ${apiWebSocketOrigins.join(' ')} ${zoomHttpOrigins.join(' ')} ${zoomWebSocketOrigins.join(' ')} ${paypalHttpOrigins.join(' ')} ${bunnyOrigins.join(' ')} https://*.oetwithdrhesham.co.uk wss://*.oetwithdrhesham.co.uk https://*.googleapis.com; media-src 'self' blob: ${apiOrigins.join(' ')} ${zoomHttpOrigins.join(' ')} ${bunnyOrigins.join(' ')}; worker-src 'self' blob: ${zoomHttpOrigins.join(' ')}; frame-src 'self' ${zoomHttpOrigins.join(' ')} ${paypalHttpOrigins.join(' ')}; object-src 'none'; base-uri 'self'; form-action 'self';`}
        />
      </head>
      <body className="font-sans antialiased min-h-[var(--app-viewport-height,100dvh)] bg-background-light text-navy overflow-x-hidden selection:bg-primary/15 selection:text-navy" suppressHydrationWarning>
        {/*
          Inline bootstrap script. Rendered as a plain <script> rather than
          next/script so the per-request CSP nonce is guaranteed to reach the
          DOM attribute — next/script's beforeInteractive strategy hoists and
          doesn't reliably propagate nonce in production.
        */}
        <script
          nonce={nonce}
          suppressHydrationWarning
          dangerouslySetInnerHTML={{ __html: getRuntimeBootstrapScript() }}
        />
        <AppProviders nonce={nonce} locale={locale} messages={messages}>{children}</AppProviders>
      </body>
    </html>
  );
}
