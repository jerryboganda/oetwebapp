'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, ShieldAlert } from 'lucide-react';
import { Capacitor } from '@capacitor/core';
import { PlatformGlyph } from '@/components/marketing/store-badges';
import { ANDROID_DOWNLOAD_URL } from '@/lib/app-downloads';
import { getInstallSource, getPlayListingUrl, type InstallSourceKind } from '@/lib/mobile/install-source';

interface NativeReleaseInfo {
  version: string;
  downloadUrl: string;
}

const STEPS = [
  'Tap "Download the update" below. Your phone starts downloading a file named OET-with-Dr-Hesham-*.apk.',
  'When the download finishes, swipe down to open your notifications (or open your Downloads app) and tap that file.',
  'If Android shows "Install blocked" or asks to allow installs from this app, tap Settings, turn on "Allow from this source", then go back and tap the file again.',
  'Tap Install, wait for it to finish, then open OET with Dr Ahmed Hesham again.',
];

export default function AndroidInstallPage() {
  const [release, setRelease] = useState<NativeReleaseInfo | null>(null);
  // Installer channel of the copy running this page (resolves only inside
  // the native shell; stays `unknown` in a plain browser). A Play-installed
  // copy must NEVER be offered the direct APK: Play App Signing re-signs
  // uploads with a different key than the VPS APK, so Android rejects the
  // cross-channel install with "App not installed" and no app code can
  // override that. Play copies update via the Play listing instead.
  const [installSource, setInstallSource] = useState<InstallSourceKind>('unknown');

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const res = await fetch('/api/releases/native?platform=android', { cache: 'no-store' });
        if (!res.ok) return;
        const data = (await res.json()) as Partial<NativeReleaseInfo>;
        if (!cancelled && typeof data.version === 'string') {
          setRelease({ version: data.version, downloadUrl: data.downloadUrl ?? ANDROID_DOWNLOAD_URL });
        }
      } catch {
        // Ignore — the download button works without this.
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const source = await getInstallSource();
      if (!cancelled) {
        setInstallSource(source.kind);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const openPlayListing = async () => {
    const url = getPlayListingUrl();
    if (Capacitor.isNativePlatform()) {
      // Inside the WebView an https Play link would load in-app; hand it to
      // the OS browser/store instead.
      const { Browser } = await import('@capacitor/browser');
      await Browser.open({ url });
      return;
    }
    window.location.assign(url);
  };

  const isPlayCopy = installSource === 'play';

  return (
    <main className="min-h-screen bg-background-light">
      <div className="mx-auto max-w-2xl px-6 py-10">
        <Link
          href="/"
          className="inline-flex items-center gap-2 text-sm font-semibold text-muted hover:text-primary"
        >
          <ArrowLeft className="h-4 w-4" />
          Back to OET Prep
        </Link>

        <section className="mt-8 rounded-3xl bg-navy px-8 py-10 text-center text-white shadow-clinical">
          <h1 className="text-2xl font-bold">Install the Android update</h1>
          {isPlayCopy ? (
            <>
              <p className="mx-auto mt-2 max-w-md text-sm leading-6 text-white/75">
                {release ? `Latest version: v${release.version}. ` : ''}
                Your copy was installed through Google Play, so update it there — a manually
                downloaded file cannot install over a Play copy (Android rejects it with
                &ldquo;App not installed&rdquo;).
              </p>
              <button
                type="button"
                onClick={() => void openPlayListing()}
                className="mt-6 inline-flex items-center gap-2 rounded-xl bg-primary px-6 py-3 text-sm font-bold text-white hover:bg-primary-dark"
              >
                <PlatformGlyph platform="android" className="h-5 w-5" />
                Update in Google Play
              </button>
            </>
          ) : (
            <>
              <p className="mx-auto mt-2 max-w-md text-sm leading-6 text-white/75">
                {release ? `Latest version: v${release.version}. ` : ''}
                This app is installed directly (not through Google Play), so the update has to be
                installed manually — it only takes a minute.
              </p>
              <a
                href={ANDROID_DOWNLOAD_URL}
                className="mt-6 inline-flex items-center gap-2 rounded-xl bg-primary px-6 py-3 text-sm font-bold text-white hover:bg-primary-dark"
              >
                <PlatformGlyph platform="android" className="h-5 w-5" />
                Download the update
              </a>
              <p className="mx-auto mt-4 max-w-md text-xs leading-5 text-white/60">
                Installed the app from Google Play instead?{' '}
                <button
                  type="button"
                  onClick={() => void openPlayListing()}
                  className="font-semibold text-white underline underline-offset-2"
                >
                  Update it there
                </button>{' '}
                — a manual file cannot install over a Play copy.
              </p>
            </>
          )}
        </section>

        <section className="mt-8 rounded-2xl border border-border bg-surface p-6 shadow-sm">
          <h2 className="text-sm font-bold text-navy">After the download finishes</h2>
          <ol className="mt-4 space-y-4">
            {STEPS.map((step, i) => (
              <li key={step} className="flex gap-3 text-sm leading-6 text-muted">
                <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-primary/10 text-xs font-bold text-primary">
                  {i + 1}
                </span>
                {step}
              </li>
            ))}
          </ol>
        </section>

        <section className="mt-6 flex items-start gap-3 rounded-2xl border border-dashed border-border bg-surface p-5">
          <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0 text-muted" aria-hidden="true" />
          <p className="text-xs leading-5 text-muted">
            {/* eslint-disable-next-line react/no-unescaped-entities -- legacy instructional quote */}
            The download alone will not update the app — Android always requires the manual "tap
            the file, then Install&quot; step above. If your download appears to finish but nothing
            happens next, that step is what&apos;s missing; it never happens automatically.
          </p>
        </section>
      </div>
    </main>
  );
}
