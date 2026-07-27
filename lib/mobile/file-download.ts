'use client';

import { Capacitor } from '@capacitor/core';

type FilesystemModule = typeof import('@capacitor/filesystem');
type ShareModule = typeof import('@capacitor/share');

let filesystemModulePromise: Promise<FilesystemModule> | null = null;
let shareModulePromise: Promise<ShareModule> | null = null;

function loadFilesystemModule(): Promise<FilesystemModule> {
  filesystemModulePromise ??= import('@capacitor/filesystem');
  return filesystemModulePromise;
}

function loadShareModule(): Promise<ShareModule> {
  shareModulePromise ??= import('@capacitor/share');
  return shareModulePromise;
}

function blobToBase64(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onloadend = () => {
      const result = reader.result as string;
      resolve(result.slice(result.indexOf(',') + 1));
    };
    reader.onerror = () => reject(reader.error ?? new Error('Failed to read file'));
    reader.readAsDataURL(blob);
  });
}

export function isNativeDownloadPlatform(): boolean {
  return Capacitor.isNativePlatform();
}

/**
 * Persist a fetched Blob on native platforms and hand it to the OS share
 * sheet so the learner can save it to Files or send it elsewhere.
 *
 * The browser `<a download>` convention (a blob: URL clicked via a synthetic
 * anchor) is a no-op inside Capacitor's native WebView — there is no download
 * manager wired up for blob: URLs, so the button silently does nothing. The
 * Filesystem plugin writes the real bytes to disk first; only then is there
 * anything for the Share plugin to hand off.
 */
export async function saveBlobNative(blob: Blob, filename: string): Promise<void> {
  const safeName = filename.replace(/[/\\]/g, '-').trim() || 'download';
  const { Filesystem, Directory } = await loadFilesystemModule();
  const data = await blobToBase64(blob);
  const written = await Filesystem.writeFile({
    path: safeName,
    data,
    directory: Directory.Cache,
  });

  const { Share } = await loadShareModule();
  await Share.share({ files: [written.uri], dialogTitle: `Save ${safeName}` });
}
