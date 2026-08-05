'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, Download, ShieldAlert } from 'lucide-react';
import { ANDROID_DOWNLOAD_URL } from '@/lib/app-downloads';

interface NativeReleaseInfo {
  version: string;
  downloadUrl: string;
}

const STEPS = [
  'Tap "Download the update" below. Your phone starts downloading a file named OET-with-Dr-Hesham-*.apk.',
  'When the download finishes, swipe down to open your notifications (or open your Downloads app) and tap that file.',
  'If Android shows "Install blocked" or asks to allow installs from this app, tap Settings, turn on "Allow from this source", then go back and tap the file again.',
  'Tap Install, wait for it to finish, then open OET with Dr. Hesham again.',
];

export default function AndroidInstallPage() {
  const [release, setRelease] = useState<NativeReleaseInfo | null>(null);

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
          <p className="mx-auto mt-2 max-w-md text-sm leading-6 text-white/75">
            {release ? `Latest version: v${release.version}. ` : ''}
            This app is installed directly (not through Google Play), so the update has to be
            installed manually — it only takes a minute.
          </p>
          <a
            href={ANDROID_DOWNLOAD_URL}
            className="mt-6 inline-flex items-center gap-2 rounded-xl bg-primary px-6 py-3 text-sm font-bold text-white hover:bg-primary-dark"
          >
            <Download className="h-5 w-5" />
            Download the update
          </a>
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
