'use client';

import { MonitorSmartphone, ShieldAlert } from 'lucide-react';
import { AppDownloadGrid, type AppDownloadLinks } from '@/components/marketing/store-badges';
import {
  ANDROID_INSTALL_URL,
  IOS_DOWNLOAD_URL,
  MAC_DOWNLOAD_URL,
  WINDOWS_DOWNLOAD_URL,
} from '@/lib/app-downloads';

const APP_DOWNLOAD_LINKS: AppDownloadLinks = {
  windows: WINDOWS_DOWNLOAD_URL,
  mac: MAC_DOWNLOAD_URL,
  android: ANDROID_INSTALL_URL,
  ios: IOS_DOWNLOAD_URL,
};

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
        <p className="text-base font-medium leading-7 text-slate-200">
          Course videos are available exclusively through the OET with Dr Hesham applications. Please download the appropriate application to continue watching.
        </p>
      </div>

      <AppDownloadGrid links={APP_DOWNLOAD_LINKS} className="mt-2 max-w-2xl" />
    </div>
  );
}
