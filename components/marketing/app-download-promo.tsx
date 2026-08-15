'use client';

import { createPortal } from 'react-dom';
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
        className="rounded-2xl border border-primary/20 bg-surface px-4 py-5 shadow-sm sm:px-6"
      >
        <div className="mx-auto flex max-w-6xl flex-col gap-4">
          <div className="min-w-0 text-center">
            <div className="flex items-center justify-center gap-2">
              <span className="flex h-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 px-2 text-primary">
                <PlatformIconCluster />
              </span>
              <h2 id="app-download-strip-title" className="text-base font-bold text-navy sm:text-lg">
                Study anywhere with the official Candidates App
              </h2>
            </div>
            <p className="mt-1.5 text-xs leading-relaxed text-muted sm:text-sm">
              Keep your candidate account in sync across desktop and mobile, with secure video access and offline study.
            </p>
            <p className="mt-1 text-xs font-medium text-primary">
              All prices are in GBP (£). Checkout happens securely inside the Candidates App.
            </p>
          </div>
          <AppDownloadGrid links={APP_DOWNLOAD_LINKS} compact />
        </div>
      </section>
    );
  }

  if (variant === 'modal') {
    return <PostLoginAppModalContent onClose={onClose} />;
  }

  return (
    <div className="rounded-3xl border border-border/80 bg-card p-6 shadow-sm transition hover:shadow-md">
      <div className="flex items-center gap-3">
        <div className="flex h-12 items-center justify-center rounded-2xl bg-teal-500/10 px-2 text-teal-600 dark:text-teal-400">
          <PlatformIconCluster />
        </div>
        <div>
          <h3 className="font-bold text-foreground">Official Candidates App</h3>
          <p className="text-xs text-muted-foreground">Available for Windows, macOS, Android &amp; iOS</p>
        </div>
      </div>

      <p className="mt-3 text-sm leading-relaxed text-muted-foreground">
        Course videos and interactive practice tools are available through our official Candidates App. All prices are in GBP (£). Checkout happens securely inside the Candidates App, with full access activated upon payment verification.
      </p>

      <AppDownloadGrid links={APP_DOWNLOAD_LINKS} className="mt-4" />
    </div>
  );
}

function PostLoginAppModalContent({ onClose }: { onClose?: () => void }) {
  const [portalTarget, setPortalTarget] = useState<HTMLElement | null>(null);

  useEffect(() => {
    setPortalTarget(document.body);
  }, []);

  if (!portalTarget) return null;

  return createPortal(
    <div
      className="fixed inset-0 z-[100] overflow-y-auto bg-black/60 px-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] pt-[max(0.75rem,env(safe-area-inset-top))] pl-[max(0.75rem,env(safe-area-inset-left))] pr-[max(0.75rem,env(safe-area-inset-right))] backdrop-blur-sm animate-in fade-in duration-200"
      role="dialog"
      aria-modal="true"
      aria-labelledby="app-download-modal-title"
    >
      <div className="relative mx-auto flex min-h-0 w-full max-w-lg flex-col overflow-hidden rounded-3xl border border-teal-500/30 bg-slate-900 text-white shadow-2xl">
        {onClose && (
          <button
            type="button"
            onClick={onClose}
            className="absolute right-2 top-2 z-10 inline-flex h-11 w-11 items-center justify-center rounded-full bg-slate-800 text-slate-300 shadow-md transition hover:bg-slate-700 hover:text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-teal-300"
            aria-label="Close"
          >
            <X className="h-5 w-5" aria-hidden="true" />
          </button>
        )}

        <div className="min-h-0 overflow-y-auto p-4 pt-16 sm:p-6 sm:pt-16">
          <div className="flex flex-col items-center text-center">
            <div className="mb-4 flex h-14 items-center justify-center rounded-2xl bg-teal-500/10 px-3 text-teal-400 ring-1 ring-teal-500/30">
              <PlatformIconCluster className="gap-2" />
            </div>

            <h3 id="app-download-modal-title" className="text-xl font-bold text-white">Get the Candidates App — OET with Dr Hesham</h3>
            <p className="mt-2 max-w-prose text-sm leading-6 text-slate-300">
              Course videos and interactive practice tools are available through our official Candidates App. Download the app on your preferred platform for secure video playback, live sync, and offline study.
            </p>

            <AppDownloadGrid links={APP_DOWNLOAD_LINKS} className="mt-6 grid-cols-1 min-[400px]:grid-cols-2" />

            <button
              type="button"
              onClick={onClose}
              className="mt-5 text-xs text-slate-400 underline underline-offset-4 transition hover:text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-teal-300"
            >
              Continue on Web (Browsing &amp; Practice)
            </button>
          </div>
        </div>
      </div>
    </div>,
    portalTarget,
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
