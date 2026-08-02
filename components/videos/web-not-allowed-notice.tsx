'use client';

import Link from 'next/link';
import { Laptop, MonitorSmartphone, ShieldAlert, Smartphone } from 'lucide-react';

export function WebNotAllowedNotice() {
  return (
    <div className="flex h-full min-h-[360px] w-full flex-col items-center justify-center gap-5 bg-gradient-to-b from-[#0F172A] via-[#0B1120] to-[#070A12] px-6 py-12 text-center text-white">
      <div className="relative flex h-16 w-16 items-center justify-center rounded-2xl bg-amber-500/10 ring-1 ring-amber-500/20 backdrop-blur-md">
        <MonitorSmartphone className="h-8 w-8 text-amber-400" aria-hidden="true" />
        <span className="absolute -right-1 -top-1 flex h-6 w-6 items-center justify-center rounded-full bg-amber-500 text-black">
          <ShieldAlert className="h-3.5 w-3.5" />
        </span>
      </div>

      <div className="max-w-lg space-y-2">
        <h2 className="text-xl font-bold tracking-tight text-white sm:text-2xl">
          App Required for Video Playback
        </h2>
        <p className="text-sm leading-6 text-slate-300">
          Course videos are protected with copyright &amp; anti-piracy security and can{' '}
          <strong className="font-semibold text-amber-400">ONLY</strong> be played strictly inside the official Desktop or Mobile Apps. Video playback is disabled on web browsers.
        </p>
      </div>

      <div className="mt-2 flex w-full max-w-md flex-col items-center justify-center gap-3 sm:flex-row">
        <Link
          href="/get-app"
          className="inline-flex w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-r from-violet-600 to-indigo-600 px-5 py-3 text-sm font-semibold text-white shadow-lg shadow-indigo-500/25 transition hover:from-violet-500 hover:to-indigo-500 hover:scale-[1.02] active:scale-[0.98] sm:w-auto"
        >
          <Laptop className="h-4 w-4" aria-hidden="true" />
          Download Desktop App (Windows / Mac)
        </Link>
        <Link
          href="/get-app"
          className="inline-flex w-full items-center justify-center gap-2 rounded-xl border border-slate-700 bg-slate-800/80 px-5 py-3 text-sm font-semibold text-slate-200 transition hover:bg-slate-700 hover:text-white hover:scale-[1.02] active:scale-[0.98] sm:w-auto"
        >
          <Smartphone className="h-4 w-4" aria-hidden="true" />
          Download Mobile App (Android / iOS)
        </Link>
      </div>
    </div>
  );
}
