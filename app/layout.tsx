import type { Metadata, Viewport } from 'next';
import { Inter } from 'next/font/google';
import { AuthProvider } from '@/contexts/auth-context';
import './globals.css';

const inter = Inter({
  subsets: ['latin'],
  variable: '--font-sans',
});

export const metadata: Metadata = {
  title: 'OET Prep — Learner Platform',
  description: 'Prepare for the Occupational English Test with personalised practice, AI feedback, and expert review.',
  manifest: '/manifest.json',
  icons: { icon: '/icon.svg' },
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
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={inter.variable}>
      <body className="font-sans antialiased min-h-screen bg-background-light" suppressHydrationWarning>
        <AuthProvider>
          {children}
        </AuthProvider>
      </body>
    </html>
  );
}
