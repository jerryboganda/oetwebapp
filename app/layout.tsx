import type { Metadata, Viewport } from 'next';
import { Fraunces, Manrope } from 'next/font/google';
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
    <html lang="en" className={`${bodyFont.variable} ${displayFont.variable}`}>
      <body className="font-sans antialiased min-h-[var(--app-viewport-height,100dvh)] bg-background-light text-navy overflow-x-hidden selection:bg-primary/15 selection:text-navy" suppressHydrationWarning>
        <AppProviders>{children}</AppProviders>
      </body>
    </html>
  );
}
