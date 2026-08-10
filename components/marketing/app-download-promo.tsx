'use client';

import { useEffect, useState } from 'react';
import { X } from 'lucide-react';
import {
  AppDownloadGrid,
  PLATFORM_ORDER,
  PlatformGlyph,
  type AppDownloadLinks,
} from '@/components/marketing/store-badges';
import {
  ANDROID_INSTALL_URL,
  GET_APP_PATH,
  IOS_DOWNLOAD_URL,
} from '@/lib/app-downloads';

interface AppDownloadPromoProps {
  variant?: 'banner' | 'card' | 'modal';
  onClose?: () => void;
}

const APP_DOWNLOAD_LINKS: AppDownloadLinks = {
  windows: GET_APP_PATH,
  mac: GET_APP_PATH,
  android: ANDROID_INSTALL_URL,
  ios: IOS_DOWNLOAD_URL,
};

function PlatformIconCluster({ className = '' }: { className?: string }) {
  return (
    <span className={`flex items-center justify-center gap-1.5 ${className}`.trim()} aria-hidden="true">
      {PLATFORM_ORDER.map((platform) => (
        <PlatformGlyph key={platform} platform={platform} className="h-4 w-4" />
      ))}
    </span>
  );
}

export function AppDownloadPromo({ variant = 'card', onClose }: AppDownloadPromoProps) {
  if (variant === 'banner') {
    return (
      <section
        aria-labelledby="app-download-strip-title"
        className="rounded-2xl border border-primary/20 bg-surface px-4 py-4 shadow-sm sm:px-5"
      >
        <div className="mx-auto flex max-w-6xl flex-col gap-4">
          <div className="min-w-0 text-center">
            <div className="flex items-center justify-center gap-2">
              <span className="flex h-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 px-2 text-primary">
                <PlatformIconCluster />
              </span>
              <h2 id="app-download-strip-title" className="text-sm font-bold text-navy">
                Study anywhere with the official OET apps
              </h2>
            </div>
            <p className="mt-1 text-xs leading-5 text-muted">
              Keep your account in sync across desktop and mobile, with secure video access and offline study.
            </p>
          </div>
          <AppDownloadGrid links={APP_DOWNLOAD_LINKS} compact />
        </div>
      </section>
    );
  }

  if (variant === 'modal') {
    return (
      <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm animate-in fade-in duration-200">
        <div className="relative w-full max-w-lg overflow-hidden rounded-3xl border border-teal-500/30 bg-slate-900 p-6 text-white shadow-2xl">
          {onClose && (
            <button
              type="button"
              onClick={onClose}
              className="absolute right-4 top-4 rounded-full bg-slate-800 p-1.5 text-slate-400 transition hover:bg-slate-700 hover:text-white"
              aria-label="Close"
            >
              <X className="h-4 w-4" />
            </button>
          )}

          <div className="flex flex-col items-center text-center">
            <div className="mb-4 flex h-14 items-center justify-center rounded-2xl bg-teal-500/10 px-3 text-teal-400 ring-1 ring-teal-500/30">
              <PlatformIconCluster className="gap-2" />
            </div>

            <h3 className="text-xl font-bold text-white">Get the OET with Dr Hesham App</h3>
            <p className="mt-2 text-sm leading-6 text-slate-300">
              Course videos are available exclusively through our official applications. Download the app on your preferred platform for secure video playback and offline study.
            </p>

            <AppDownloadGrid links={APP_DOWNLOAD_LINKS} className="mt-6" />

            <button
              type="button"
              onClick={onClose}
              className="mt-5 text-xs text-slate-400 underline underline-offset-4 transition hover:text-white"
            >
              Continue on Web (Browsing &amp; Practice)
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="rounded-3xl border border-border/80 bg-card p-6 shadow-sm transition hover:shadow-md">
      <div className="flex items-center gap-3">
        <div className="flex h-12 items-center justify-center rounded-2xl bg-teal-500/10 px-2 text-teal-600 dark:text-teal-400">
          <PlatformIconCluster />
        </div>
        <div>
          <h3 className="font-bold text-foreground">Official OET Applications</h3>
          <p className="text-xs text-muted-foreground">Available for Windows, macOS, Android &amp; iOS</p>
        </div>
      </div>

      <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
        Course videos are available exclusively through our official applications. Download the app to enjoy uninterrupted video streaming, offline practice, and instant updates.
      </p>

      <AppDownloadGrid links={APP_DOWNLOAD_LINKS} className="mt-4" />
    </div>
  );
}

export function PostLoginAppModal() {
  const [isOpen, setIsOpen] = useState(false);

  useEffect(() => {
    const hasSeenPromo = sessionStorage.getItem('oet_app_promo_dismissed');
    if (!hasSeenPromo) setIsOpen(true);
  }, []);

  const handleClose = () => {
    sessionStorage.setItem('oet_app_promo_dismissed', 'true');
    setIsOpen(false);
  };

  if (!isOpen) return null;
  return <AppDownloadPromo variant="modal" onClose={handleClose} />;
}
