'use client';

import { Capacitor } from '@capacitor/core';

// ── Types ───────────────────────────────────────────────────────

export interface ShareOptions {
  title?: string;
  text?: string;
  url?: string;
  dialogTitle?: string;
}

export interface ShareResult {
  shared: boolean;
  platform?: string;
}

// ── Lazy Module Loading ─────────────────────────────────────────

type ShareModule = typeof import('@capacitor/share');
let shareModulePromise: Promise<ShareModule> | null = null;

function loadShareModule(): Promise<ShareModule> {
  shareModulePromise ??= import('@capacitor/share');
  return shareModulePromise;
}

// ── Share Functions ─────────────────────────────────────────────

export async function canShare(): Promise<boolean> {
  if (!Capacitor.isNativePlatform()) {
    return typeof navigator !== 'undefined' && typeof navigator.share === 'function';
  }

  try {
    const { Share } = await loadShareModule();
    const result = await Share.canShare();
    return result.value;
  } catch {
    return false;
  }
}

export async function triggerShare(options: ShareOptions): Promise<ShareResult> {
  if (!Capacitor.isNativePlatform()) {
    // Fallback to Web Share API
    if (typeof navigator !== 'undefined' && typeof navigator.share === 'function') {
      try {
        await navigator.share({
          title: options.title,
          text: options.text,
          url: options.url,
        });
        return { shared: true, platform: 'web' };
      } catch {
        return { shared: false, platform: 'web' };
      }
    }
    return { shared: false };
  }

  try {
    const { Share } = await loadShareModule();
    await Share.share({
      title: options.title,
      text: options.text,
      url: options.url,
      dialogTitle: options.dialogTitle,
    });
    return { shared: true, platform: Capacitor.getPlatform() };
  } catch {
    return { shared: false, platform: Capacitor.getPlatform() };
  }
}

// ── Pre-built Share Actions ─────────────────────────────────────

export async function shareAppLink(): Promise<ShareResult> {
  return triggerShare({
    title: 'OET Prep Learner',
    text: 'Prepare for your OET exam with OET Prep Learner!',
    url: 'https://app.oetwithdrhesham.co.uk',
    dialogTitle: 'Share OET Prep',
  });
}

export async function shareAchievement(achievementTitle: string): Promise<ShareResult> {
  return triggerShare({
    title: 'OET Prep Achievement',
    text: `I just earned "${achievementTitle}" on OET Prep Learner!`,
    url: 'https://app.oetwithdrhesham.co.uk',
    dialogTitle: 'Share Achievement',
  });
}

export async function shareScore(subtest: string, score: number): Promise<ShareResult> {
  return triggerShare({
    title: 'OET Prep Score',
    text: `I scored ${score}% on ${subtest} practice with OET Prep Learner!`,
    url: 'https://app.oetwithdrhesham.co.uk',
    dialogTitle: 'Share Score',
  });
}
