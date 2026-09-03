'use client';

import { useEffect, useMemo, useState } from 'react';
import Image from 'next/image';
import Link from 'next/link';
import {
  ArrowLeft,
  BellRing,
  PlayCircle,
  ShieldCheck,
  Smartphone,
  Video,
} from 'lucide-react';
import {
  ANDROID_INSTALL_URL,
  detectVisitorOs,
  IOS_DOWNLOAD_URL,
  IOS_STORE_URL,
  MAC_DOWNLOAD_URL,
  WINDOWS_DOWNLOAD_URL,
  type DesktopOsKind,
} from '@/lib/app-downloads';
import {
  PlatformDownloadBadge,
  PlatformGlyph,
  type PlatformKey,
} from '@/components/marketing/store-badges';

const GET_APP_URL = 'https://app.oetwithdrhesham.co.uk/get-app';

const OS_CTA: Partial<Record<DesktopOsKind, { platform: PlatformKey; href: string }>> = {
  windows: { platform: 'windows', href: WINDOWS_DOWNLOAD_URL },
  mac: { platform: 'mac', href: MAC_DOWNLOAD_URL },
  android: { platform: 'android', href: ANDROID_INSTALL_URL },
  ios: { platform: 'ios', href: IOS_DOWNLOAD_URL },
};

const FEATURES = [
  { icon: Video, title: 'Video Library', text: 'Expert-led OET video lessons stream exclusively inside the apps — with resume, chapters, captions, and handouts.' },
  { icon: PlayCircle, title: 'Full practice suite', text: 'Listening, Reading, Writing, Speaking and full mock exams — the complete platform, everywhere.' },
  { icon: ShieldCheck, title: 'Secure & up to date', text: 'The desktop app updates itself automatically; the app tells you in-app when an Android or iOS update is ready.' },
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
          <h1 className="text-3xl font-bold">Get the Candidates App</h1>
          <p className="mx-auto mt-3 max-w-xl text-sm leading-6 text-white/75">
            The Video Library and the smoothest OET practice experience live in our desktop and
            mobile apps. One account, everything in sync.
          </p>
          {heroCta && (
            <PlatformDownloadBadge
              platform={heroCta.platform}
              href={heroCta.href}
              className="mx-auto mt-6 max-w-[240px]"
            />
          )}
        </section>

        <section className="mt-8 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4 sm:gap-5">
          <div className="group flex h-full flex-col items-center rounded-2xl border border-border bg-surface p-5 sm:p-6 text-center shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-primary hover:shadow-md hoverable:-translate-y-0.5">
            <PlatformGlyph platform="windows" className="h-8 w-8 text-primary shrink-0 transition-transform duration-200 group-hover:scale-105" />
            <div className="my-3 flex flex-1 flex-col items-center justify-start w-full min-h-[64px] sm:min-h-[76px]">
              <h2 className="text-sm sm:text-base font-bold text-navy">Windows</h2>
              <p className="mt-1 text-xs text-muted leading-relaxed">Installer (.exe) — auto-updates</p>
            </div>
            <PlatformDownloadBadge platform="windows" href={WINDOWS_DOWNLOAD_URL} className="w-full max-w-[220px] mt-auto justify-center" />
          </div>

          <div className="group flex h-full flex-col items-center rounded-2xl border border-border bg-surface p-5 sm:p-6 text-center shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-primary hover:shadow-md hoverable:-translate-y-0.5">
            <PlatformGlyph platform="mac" className="h-8 w-8 text-primary shrink-0 transition-transform duration-200 group-hover:scale-105" />
            <div className="my-3 flex flex-1 flex-col items-center justify-start w-full min-h-[64px] sm:min-h-[76px]">
              <h2 className="text-sm sm:text-base font-bold text-navy">macOS</h2>
              <p className="mt-1 text-xs text-muted leading-relaxed">Universal .dmg — Intel & Apple Silicon (M1/M2/M3/M4) — auto-updates</p>
            </div>
            <PlatformDownloadBadge platform="mac" href={MAC_DOWNLOAD_URL} className="w-full max-w-[220px] mt-auto justify-center" />
          </div>

          <div className="group flex h-full flex-col items-center rounded-2xl border border-border bg-surface p-5 sm:p-6 text-center shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-primary hover:shadow-md hoverable:-translate-y-0.5">
            <PlatformGlyph platform="android" className="h-8 w-8 text-primary shrink-0 transition-transform duration-200 group-hover:scale-105" />
            <div className="my-3 flex flex-1 flex-col items-center justify-start w-full min-h-[64px] sm:min-h-[76px]">
              <h2 className="text-sm sm:text-base font-bold text-navy">Android</h2>
              <p className="mt-1 text-xs text-muted leading-relaxed">Signed APK (.apk) — latest release</p>
            </div>
            <PlatformDownloadBadge platform="android" href={ANDROID_INSTALL_URL} className="w-full max-w-[220px] mt-auto justify-center" />
          </div>

          <div className="group flex h-full flex-col items-center rounded-2xl border border-border bg-surface p-5 sm:p-6 text-center shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-primary hover:shadow-md hoverable:-translate-y-0.5">
            <PlatformGlyph platform="ios" className="h-8 w-8 text-primary shrink-0 transition-transform duration-200 group-hover:scale-105" />
            <div className="my-3 flex flex-1 flex-col items-center justify-start w-full min-h-[64px] sm:min-h-[76px]">
              <h2 className="text-sm sm:text-base font-bold text-navy">iPhone & iPad</h2>
              <p className="mt-1 text-xs text-muted leading-relaxed">
                {IOS_STORE_URL ? 'Official App Store download' : 'Temporary direct IPA download'}
              </p>
            </div>
            <PlatformDownloadBadge platform="ios" href={IOS_DOWNLOAD_URL} className="w-full max-w-[220px] mt-auto justify-center" />
          </div>
        </section>

        <section className="mt-8 sm:mt-10">
          <div className="mb-4 text-center sm:text-left">
            <h2 className="text-xs font-bold uppercase tracking-wider text-muted">
              Included across all devices & platforms
            </h2>
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4 sm:gap-5">
            {FEATURES.map((feature) => (
              <div
                key={feature.title}
                className="group flex h-full flex-col rounded-2xl border border-border bg-surface p-5 sm:p-6 shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-primary hover:shadow-md hoverable:-translate-y-0.5"
              >
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary transition-colors group-hover:bg-primary group-hover:text-white">
                  <feature.icon className="h-5 w-5" aria-hidden="true" />
                </div>
                <h3 className="mt-3.5 text-sm font-bold text-navy sm:text-base">{feature.title}</h3>
                <p className="mt-1.5 flex-1 text-xs leading-relaxed text-muted">{feature.text}</p>
              </div>
            ))}
          </div>
        </section>

        <section className="mt-8 sm:mt-10 rounded-2xl border border-border bg-surface p-6 sm:p-8 shadow-sm">
          <div className="flex flex-col sm:flex-row items-center gap-5 sm:gap-6 text-center sm:text-left">
            {qrDataUrl ? (
              <div className="shrink-0 rounded-xl bg-white p-2.5 shadow-xs border border-border">
                <Image src={qrDataUrl} alt="QR code linking to this download page" width={130} height={130} unoptimized className="rounded-lg" />
              </div>
            ) : (
              <div className="flex h-[130px] w-[130px] shrink-0 items-center justify-center rounded-xl border border-border bg-background-light text-xs text-muted">
                QR code
              </div>
            )}
            <div className="max-w-xl flex-1">
              <div className="inline-flex items-center gap-1.5 rounded-full bg-primary/10 px-2.5 py-0.5 text-xs font-semibold text-primary">
                <Smartphone className="h-3.5 w-3.5" aria-hidden="true" />
                Mobile & Tablet Companion
              </div>
              <h3 className="mt-2 text-base sm:text-lg font-bold text-navy">
                Practise on your smartphone or tablet
              </h3>
              <p className="mt-1 text-xs sm:text-sm text-muted leading-relaxed">
                On your desktop or laptop? Scan this QR code with your mobile camera to open this page and get the app directly on your phone or tablet.
              </p>
            </div>
          </div>
        </section>

        <p className="mt-8 text-center text-xs leading-5 text-muted">
          Why app-only videos? Our video lessons are original teaching material. Streaming them
          exclusively inside the apps keeps the content secure for paying candidates and keeps
          prices fair for everyone.
        </p>
      </div>
    </main>
  );
}
