'use client';

import React, { useState, useEffect } from 'react';
import Link from 'next/link';
import { Monitor, Smartphone, Download, ShieldCheck, X } from 'lucide-react';

interface AppDownloadPromoProps {
  variant?: 'banner' | 'card' | 'modal';
  onClose?: () => void;
}

export function AppDownloadPromo({ variant = 'card', onClose }: AppDownloadPromoProps) {
  if (variant === 'banner') {
    return (
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-r from-teal-900 via-indigo-900 to-slate-900 p-4 text-white shadow-xl border border-teal-500/20">
        <div className="flex flex-col items-start justify-between gap-4 md:flex-row md:items-center">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-teal-500/20 text-teal-400 border border-teal-500/30">
              <Download className="h-5 w-5" />
            </div>
            <div>
              <p className="text-sm font-bold text-white">Download Official Apps for Video Access &amp; Offline Study</p>
              <p className="text-xs text-slate-300">
                Course video playback is enabled exclusively on our official Windows, macOS, Android, and iOS applications.
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2 shrink-0">
            <Link
              href="/get-app"
              className="inline-flex items-center gap-1.5 rounded-lg bg-teal-500 px-3.5 py-1.5 text-xs font-semibold text-slate-950 hover:bg-teal-400 transition"
            >
              <Monitor className="h-3.5 w-3.5" />
              Windows &amp; Mac
            </Link>
            <Link
              href="/get-app"
              className="inline-flex items-center gap-1.5 rounded-lg bg-indigo-600 px-3.5 py-1.5 text-xs font-semibold text-white hover:bg-indigo-500 transition"
            >
              <Smartphone className="h-3.5 w-3.5" />
              Android &amp; iOS
            </Link>
          </div>
        </div>
      </div>
    );
  }

  if (variant === 'modal') {
    return (
      <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm animate-in fade-in duration-200">
        <div className="relative w-full max-w-lg overflow-hidden rounded-3xl bg-slate-900 p-6 text-white shadow-2xl border border-teal-500/30">
          {onClose && (
            <button
              onClick={onClose}
              className="absolute right-4 top-4 rounded-full bg-slate-800 p-1.5 text-slate-400 hover:bg-slate-700 hover:text-white transition"
              aria-label="Close"
            >
              <X className="h-4 w-4" />
            </button>
          )}

          <div className="flex flex-col items-center text-center">
            <div className="mb-4 flex h-14 w-14 items-center justify-center rounded-2xl bg-teal-500/10 text-teal-400 ring-1 ring-teal-500/30">
              <ShieldCheck className="h-8 w-8" />
            </div>

            <h3 className="text-xl font-bold text-white">Get the OET with Dr Hesham App</h3>
            <p className="mt-2 text-sm leading-6 text-slate-300">
              Course videos are available exclusively through our official applications. Download now on your PC, Mac, phone, or tablet for full HD video playback and security.
            </p>

            <div className="mt-6 grid w-full grid-cols-2 gap-3">
              <Link
                href="/get-app"
                className="flex flex-col items-center justify-center rounded-2xl border border-slate-700 bg-slate-800/80 p-4 hover:border-teal-500/50 hover:bg-slate-800 transition group"
              >
                <Monitor className="h-6 w-6 text-teal-400 group-hover:scale-110 transition-transform" />
                <span className="mt-2 text-xs font-semibold text-white">Windows &amp; macOS</span>
                <span className="text-[10px] text-slate-400">Desktop Edition</span>
              </Link>
              <Link
                href="/get-app"
                className="flex flex-col items-center justify-center rounded-2xl border border-slate-700 bg-slate-800/80 p-4 hover:border-indigo-500/50 hover:bg-slate-800 transition group"
              >
                <Smartphone className="h-6 w-6 text-indigo-400 group-hover:scale-110 transition-transform" />
                <span className="mt-2 text-xs font-semibold text-white">Google Play &amp; App Store</span>
                <span className="text-[10px] text-slate-400">Android &amp; iOS</span>
              </Link>
            </div>

            <button
              onClick={onClose}
              className="mt-5 text-xs text-slate-400 hover:text-white transition underline underline-offset-4"
            >
              Continue on Web (Browsing &amp; Practice)
            </button>
          </div>
        </div>
      </div>
    );
  }

  // Default 'card' variant
  return (
    <div className="rounded-3xl border border-border/80 bg-card p-6 shadow-sm transition hover:shadow-md">
      <div className="flex items-center gap-3">
        <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-teal-500/10 text-teal-600 dark:text-teal-400">
          <Download className="h-6 w-6" />
        </div>
        <div>
          <h3 className="font-bold text-foreground">Official OET Applications</h3>
          <p className="text-xs text-muted-foreground">Available for Windows, macOS, Android &amp; iOS</p>
        </div>
      </div>

      <p className="mt-3 text-sm text-muted-foreground leading-relaxed">
        Course videos are available exclusively through our official applications. Download the app to enjoy uninterrupted video streaming, offline practice, and instant updates.
      </p>

      <div className="mt-4 flex flex-wrap items-center gap-2">
        <Link
          href="/get-app"
          className="inline-flex items-center gap-2 rounded-xl bg-teal-600 px-4 py-2 text-xs font-semibold text-white shadow-sm hover:bg-teal-500 transition"
        >
          <Monitor className="h-4 w-4" />
          Download Desktop App
        </Link>
        <Link
          href="/get-app"
          className="inline-flex items-center gap-2 rounded-xl bg-slate-900 dark:bg-slate-800 px-4 py-2 text-xs font-semibold text-white shadow-sm hover:bg-slate-800 dark:hover:bg-slate-700 transition"
        >
          <Smartphone className="h-4 w-4" />
          Download Mobile App
        </Link>
      </div>
    </div>
  );
}

export function PostLoginAppModal() {
  const [isOpen, setIsOpen] = useState(false);

  useEffect(() => {
    // Show promo modal once per session for web users
    const hasSeenPromo = sessionStorage.getItem('oet_app_promo_dismissed');
    if (!hasSeenPromo) {
      setIsOpen(true);
    }
  }, []);

  const handleClose = () => {
    sessionStorage.setItem('oet_app_promo_dismissed', 'true');
    setIsOpen(false);
  };

  if (!isOpen) return null;
  return <AppDownloadPromo variant="modal" onClose={handleClose} />;
}
