'use client';

import Image from 'next/image';
import Link from 'next/link';
import { LockKeyhole, MonitorDown, Smartphone } from 'lucide-react';
import { ANDROID_STORE_URL, GET_APP_PATH, GITHUB_RELEASES_URL } from '@/lib/app-downloads';

/**
 * Shown in place of the player on WEB (browser) visits — videos play only
 * inside the desktop and mobile apps. The catalog stays fully browsable;
 * this screen converts the lock into an app-install prompt.
 */
export function PlayerLockScreen({
  title,
  thumbnailUrl,
}: {
  title: string;
  thumbnailUrl: string | null;
}) {
  return (
    <div className="relative flex h-full min-h-[280px] w-full flex-col items-center justify-center overflow-hidden bg-navy px-6 py-10 text-center">
      {thumbnailUrl && (
        <Image
          src={thumbnailUrl}
          alt=""
          fill
          unoptimized
          aria-hidden="true"
          sizes="100vw"
          className="object-cover opacity-25 blur-md"
        />
      )}
      <div className="relative z-10 flex flex-col items-center gap-4">
        <span className="flex h-16 w-16 items-center justify-center rounded-full bg-white/10">
          <LockKeyhole className="h-8 w-8 text-white" aria-hidden="true" />
        </span>
        <div>
          <h2 className="text-lg font-bold text-white">Videos play only in the OET app</h2>
          <p className="mx-auto mt-2 max-w-md text-sm leading-6 text-white/70">
            To protect our teaching content, “{title}” streams exclusively inside the OET Prep
            desktop and mobile apps. Install the app, sign in with this account, and continue
            right where you left off.
          </p>
        </div>
        <div className="flex flex-wrap items-center justify-center gap-3">
          <a
            href={GITHUB_RELEASES_URL}
            target="_blank"
            rel="noreferrer"
            className="inline-flex items-center gap-2 rounded-lg bg-primary px-4 py-2.5 text-sm font-semibold text-white hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
          >
            <MonitorDown className="h-4 w-4" aria-hidden="true" />
            Desktop app (Windows / Mac)
          </a>
          <a
            href={ANDROID_STORE_URL}
            target="_blank"
            rel="noreferrer"
            className="inline-flex items-center gap-2 rounded-lg border border-white/30 px-4 py-2.5 text-sm font-semibold text-white hover:border-white hover:bg-white/10"
          >
            <Smartphone className="h-4 w-4" aria-hidden="true" />
            Android app
          </a>
        </div>
        <Link href={GET_APP_PATH} className="text-xs font-semibold text-white/70 underline-offset-4 hover:text-white hover:underline">
          See all download options & QR code
        </Link>
      </div>
    </div>
  );
}
