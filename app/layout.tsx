import type { Metadata, Viewport } from 'next';
import Script from 'next/script';
import { Fraunces, Manrope } from 'next/font/google';
import { getRuntimeBootstrapScript } from '@/lib/runtime-signals';
import { AppProviders } from './providers';
import './globals.css';

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

export const metadata: Metadata = {
  title: 'OET Prep — Learner Platform',
  description: 'Prepare for the Occupational English Test with personalised practice, AI feedback, and expert review.',
  manifest: '/manifest.json',
  icons: { icon: '/icon.svg' },
  appleWebApp: {
    capable: true,
    title: 'OET Prep',
  },
  openGraph: {
    title: 'OET Prep — Learner Platform',
    description: 'Prepare for the OET with personalised practice, AI feedback, and expert review.',
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

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={`${bodyFont.variable} ${displayFont.variable}`} suppressHydrationWarning>
      <head>
        {/*
          CSP for Capacitor WebView and web — restrict script/style/connect sources.
          NOTE: `frame-ancestors 'none'` is critical here. The meta CSP is the
          only frame-ancestors defense inside the Capacitor WebView and the
          Electron renderer, where the response header from next.config.ts is
          not authoritative. Keep it in sync with `X-Frame-Options: DENY`.
        */}
        <meta
          httpEquiv="Content-Security-Policy"
          content="default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: blob: https:; connect-src 'self' https://*.oetwithdrhesham.co.uk wss://*.oetwithdrhesham.co.uk https://generativelanguage.googleapis.com; media-src 'self' blob:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none';"
        />
      </head>
      <body className="font-sans antialiased min-h-[var(--app-viewport-height,100dvh)] bg-background-light text-navy overflow-x-hidden selection:bg-primary/15 selection:text-navy" suppressHydrationWarning>
        <Script id="runtime-signals" strategy="beforeInteractive" dangerouslySetInnerHTML={{ __html: getRuntimeBootstrapScript() }} />
        <AppProviders>{children}</AppProviders>
      </body>
    </html>
  );
}
