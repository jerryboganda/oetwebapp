'use client';

import { useEffect, useMemo, useState } from 'react';
import Image from 'next/image';
import Link from 'next/link';
import {
  Apple,
  ArrowLeft,
  BellRing,
  Laptop,
  MonitorDown,
  PlayCircle,
  ShieldCheck,
  Smartphone,
  Video,
} from 'lucide-react';
import {
  ANDROID_DOWNLOAD_URL,
  detectVisitorOs,
  IOS_STORE_URL,
  MAC_DOWNLOAD_URL,
  WINDOWS_DOWNLOAD_URL,
  type DesktopOsKind,
} from '@/lib/app-downloads';

const GET_APP_URL = 'https://app.oetwithdrhesham.co.uk/get-app';

const OS_CTA: Partial<Record<DesktopOsKind, { label: string; href: string }>> = {
  windows: { label: 'Download for Windows', href: WINDOWS_DOWNLOAD_URL },
  mac: { label: 'Download for Mac', href: MAC_DOWNLOAD_URL },
  android: { label: 'Download the Android app', href: ANDROID_DOWNLOAD_URL },
};

const FEATURES = [
  { icon: Video, title: 'Video Library', text: 'Expert-led OET video lessons stream exclusively inside the apps — with resume, chapters, captions, and handouts.' },
  { icon: PlayCircle, title: 'Full practice suite', text: 'Listening, Reading, Writing, Speaking and full mock exams — the complete platform, everywhere.' },
  { icon: ShieldCheck, title: 'Secure & up to date', text: 'The desktop app updates itself automatically; mobile updates arrive through the store.' },
  { icon: BellRing, title: 'Notifications', text: 'Get notified the moment new videos, recalls, and mock results land.' },
];

export default function GetAppPage() {
  const [visitorOs, setVisitorOs] = useState<DesktopOsKind>('unknown');
  const [qrDataUrl, setQrDataUrl] = useState<string | null>(null);

  useEffect(() => {
    setVisitorOs(detectVisitorOs());
  }, []);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const { toDataURL } = await import('qrcode');
        const dataUrl = await toDataURL(GET_APP_URL, { margin: 1, width: 200 });
        if (!cancelled) setQrDataUrl(dataUrl);
      } catch {
        if (!cancelled) setQrDataUrl(null);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const heroCta = useMemo(() => OS_CTA[visitorOs] ?? null, [visitorOs]);

  return (
    <main className="min-h-screen bg-background-light">
      <div className="mx-auto max-w-5xl px-6 py-10">
        <Link
          href="/"
          className="inline-flex items-center gap-2 text-sm font-semibold text-muted hover:text-primary"
        >
          <ArrowLeft className="h-4 w-4" />
          Back to OET Prep
        </Link>

        <section className="mt-8 rounded-3xl bg-navy px-8 py-12 text-center text-white shadow-clinical">
          <h1 className="text-3xl font-bold">Get the OET Prep app</h1>
          <p className="mx-auto mt-3 max-w-xl text-sm leading-6 text-white/75">
            The Video Library and the smoothest OET practice experience live in our desktop and
            mobile apps. One account, everything in sync.
          </p>
          {heroCta && (
            <a
              href={heroCta.href}
              className="mt-6 inline-flex items-center gap-2 rounded-xl bg-primary px-6 py-3 text-sm font-bold text-white hover:bg-primary-dark"
            >
              <MonitorDown className="h-5 w-5" />
              {heroCta.label}
            </a>
          )}
        </section>

        <section className="mt-8 grid gap-4 md:grid-cols-2 lg:grid-cols-4">
          <a
            href={WINDOWS_DOWNLOAD_URL}
            className="flex flex-col items-center gap-3 rounded-2xl border border-border bg-surface p-6 text-center shadow-sm transition-colors hover:border-primary"
          >
            <Laptop className="h-8 w-8 text-primary" />
            <div>
              <h2 className="text-sm font-bold text-navy">Windows</h2>
              <p className="mt-1 text-xs text-muted">Installer (.exe) — auto-updates</p>
            </div>
            <span className="rounded-lg bg-primary px-4 py-2 text-xs font-bold text-white">Download</span>
          </a>

          <a
            href={MAC_DOWNLOAD_URL}
            className="flex flex-col items-center gap-3 rounded-2xl border border-border bg-surface p-6 text-center shadow-sm transition-colors hover:border-primary"
          >
            <Apple className="h-8 w-8 text-primary" />
            <div>
              <h2 className="text-sm font-bold text-navy">macOS</h2>
              <p className="mt-1 text-xs text-muted">Disk image (.dmg) — auto-updates</p>
            </div>
            <span className="rounded-lg bg-primary px-4 py-2 text-xs font-bold text-white">Download</span>
          </a>

          <a
            href={ANDROID_DOWNLOAD_URL}
            className="flex flex-col items-center gap-3 rounded-2xl border border-border bg-surface p-6 text-center shadow-sm transition-colors hover:border-primary"
          >
            <Smartphone className="h-8 w-8 text-primary" />
            <div>
              <h2 className="text-sm font-bold text-navy">Android</h2>
              <p className="mt-1 text-xs text-muted">Signed APK (.apk) — latest release</p>
            </div>
            <span className="rounded-lg bg-primary px-4 py-2 text-xs font-bold text-white">Download</span>
          </a>

          {IOS_STORE_URL ? (
            <a
              href={IOS_STORE_URL}
              target="_blank"
              rel="noreferrer"
              className="flex flex-col items-center gap-3 rounded-2xl border border-border bg-surface p-6 text-center shadow-sm transition-colors hover:border-primary"
            >
              <Apple className="h-8 w-8 text-primary" />
              <div>
                <h2 className="text-sm font-bold text-navy">iPhone & iPad</h2>
                <p className="mt-1 text-xs text-muted">App Store</p>
              </div>
              <span className="rounded-lg bg-primary px-4 py-2 text-xs font-bold text-white">Get the app</span>
            </a>
          ) : (
            <div className="flex flex-col items-center gap-3 rounded-2xl border border-dashed border-border bg-surface p-6 text-center shadow-sm">
              <Apple className="h-8 w-8 text-muted" />
              <div>
                <h2 className="text-sm font-bold text-navy">iPhone & iPad</h2>
                <p className="mt-1 text-xs text-muted">Coming soon to the App Store</p>
              </div>
            </div>
          )}
        </section>

        <section className="mt-8 grid gap-6 md:grid-cols-[280px_minmax(0,1fr)]">
          <div className="flex flex-col items-center gap-3 rounded-2xl border border-border bg-surface p-6 text-center shadow-sm">
            {qrDataUrl ? (
              <Image src={qrDataUrl} alt="QR code linking to this download page" width={200} height={200} unoptimized className="rounded-lg" />
            ) : (
              <div className="flex h-[200px] w-[200px] items-center justify-center rounded-lg bg-background-light text-xs text-muted">
                QR code
              </div>
            )}
            <p className="text-xs leading-5 text-muted">
              On your phone? Scan to open this page and grab the mobile app.
            </p>
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            {FEATURES.map((feature) => (
              <div key={feature.title} className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
                <feature.icon className="h-6 w-6 text-primary" aria-hidden="true" />
                <h3 className="mt-3 text-sm font-bold text-navy">{feature.title}</h3>
                <p className="mt-1.5 text-xs leading-5 text-muted">{feature.text}</p>
              </div>
            ))}
          </div>
        </section>

        <p className="mt-8 text-center text-xs leading-5 text-muted">
          Why app-only videos? Our video lessons are original teaching material. Streaming them
          exclusively inside the apps keeps the content secure for paying students and keeps
          prices fair for everyone.
        </p>
      </div>
    </main>
  );
}
