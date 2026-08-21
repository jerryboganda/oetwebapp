import { createReadStream, existsSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';

export const DEFAULT_RELEASES_ROOT = '/var/opt/oet-learner/releases';
export const DEFAULT_PUBLIC_BASE_URL = 'https://app.oetwithdrhesham.co.uk';
export const DOWNLOAD_FALLBACK_PATH = '/get-app';

export function getDownloadFallbackUrl(): string {
  return `${getPublicBaseUrl()}${DOWNLOAD_FALLBACK_PATH}`;
}

const ALLOWED_EXTENSIONS = new Set([
  '.exe',
  '.sig',
  '.dmg',
  '.apk',
  '.aab',
  '.ipa',
  '.json',
  '.txt',
  '.gz',
]);

const DESKTOP_TARGETS = new Set(['windows-x86_64', 'darwin-aarch64', 'darwin-x86_64']);

export type DownloadPlatform = 'windows' | 'mac' | 'android' | 'ios';

export interface TauriPlatformArtifact {
  signature: string;
  url: string;
}

export interface DesktopDownload {
  url: string;
  sha256?: string;
}

export interface DesktopFeed {
  version: string;
  notes?: string;
  pub_date?: string;
  platforms: Record<string, TauriPlatformArtifact>;
  downloads?: Partial<Record<'windows' | 'mac', DesktopDownload>>;
}

export interface MobileRelease {
  platform: 'android' | 'ios';
  version: string;
  downloadUrl: string;
  digest?: string | null;
  publishedAt?: string | null;
  sha256?: string;
}

export interface ReleaseAsset {
  absPath: string;
  contentType: string;
  cacheControl: string;
  size: number;
}

export function getReleasesRoot(): string {
  return process.env.RELEASES_ROOT?.trim() || DEFAULT_RELEASES_ROOT;
}

export function getPublicBaseUrl(): string {
  const raw = process.env.RELEASES_PUBLIC_BASE_URL?.trim()
    || process.env.APP_URL?.trim()
    || DEFAULT_PUBLIC_BASE_URL;
  return raw.replace(/\/+$/, '');
}

export function isTrustedReleaseUrl(value: string): boolean {
  try {
    const url = new URL(value);
    const allowedHosts = new Set([
      new URL(DEFAULT_PUBLIC_BASE_URL).hostname,
      new URL(getPublicBaseUrl()).hostname,
    ]);
    return url.protocol === 'https:'
      && allowedHosts.has(url.hostname)
      && url.pathname.startsWith('/releases/');
  } catch {
    return false;
  }
}

export function publicReleaseUrl(...segments: string[]): string {
  const encoded = segments
    .map((segment) => segment.split('/').map((part) => encodeURIComponent(part)).join('/'))
    .join('/');
  return `${getPublicBaseUrl()}/releases/${encoded}`;
}

export function readDesktopFeed(): DesktopFeed | null {
  return readJsonFile(path.join(getReleasesRoot(), 'desktop', 'current.json'), validateDesktopFeed);
}

export function readMobileRelease(platform: 'android' | 'ios'): MobileRelease | null {
  return readJsonFile(
    path.join(getReleasesRoot(), 'mobile', platform, 'current.json'),
    (value) => validateMobileRelease(value, platform),
  );
}

export function resolveDownloadUrl(platform: DownloadPlatform): string | null {
  if (platform === 'android' || platform === 'ios') {
    const release = readMobileRelease(platform);
    return release && isTrustedReleaseUrl(release.downloadUrl) ? release.downloadUrl : null;
  }

  const feed = readDesktopFeed();
  const download = feed?.downloads?.[platform];
  if (download && isTrustedReleaseUrl(download.url)) return download.url;

  const fallbackTarget = platform === 'windows' ? 'windows-x86_64' : 'darwin-aarch64';
  const artifact = feed?.platforms?.[fallbackTarget];
  if (artifact && isTrustedReleaseUrl(artifact.url) && !artifact.url.endsWith('.tar.gz')) {
    return artifact.url;
  }
  return null;
}

export function resolveReleaseAsset(relativePath: string): ReleaseAsset | null {
  const root = path.resolve(getReleasesRoot());
  const normalized = relativePath.replace(/\\/g, '/').replace(/^\/+/, '');
  if (!normalized || normalized.includes('\0') || normalized.split('/').some((part) => part === '..')) {
    return null;
  }

  const absPath = path.resolve(root, normalized);
  if (absPath !== root && !absPath.startsWith(`${root}${path.sep}`)) return null;
  if (!existsSync(absPath) || !statSync(absPath).isFile()) return null;

  const ext = extensionOf(absPath);
  if (!ALLOWED_EXTENSIONS.has(ext)) return null;

  const stat = statSync(absPath);
  const isManifest = ext === '.json' || /(^|[\\/])current\.json$/.test(absPath);
  return {
    absPath,
    contentType: contentTypeFor(ext),
    cacheControl: isManifest
      ? 'public, max-age=30, must-revalidate'
      : 'public, max-age=31536000, immutable',
    size: stat.size,
  };
}

export function openReleaseAsset(asset: ReleaseAsset) {
  return createReadStream(asset.absPath);
}

export function buildDesktopFeed(input: DesktopFeed): DesktopFeed {
  return validateDesktopFeed(input);
}

export function buildMobileRelease(input: MobileRelease): MobileRelease {
  return validateMobileRelease(input, input.platform);
}

function readJsonFile<T>(filePath: string, validate: (value: unknown) => T): T | null {
  try {
    if (!existsSync(filePath)) return null;
    return validate(JSON.parse(readFileSync(filePath, 'utf8')));
  } catch {
    return null;
  }
}

function validateDesktopFeed(value: unknown): DesktopFeed {
  if (!value || typeof value !== 'object') throw new Error('Desktop feed is missing.');
  const feed = value as DesktopFeed;
  if (!isSemver(feed.version)) throw new Error('Desktop feed version is invalid.');
  if (!feed.platforms || typeof feed.platforms !== 'object') throw new Error('Desktop platforms are missing.');
  if (!feed.platforms['windows-x86_64']) throw new Error('Windows updater artifact is required.');

  for (const [target, artifact] of Object.entries(feed.platforms)) {
    if (!DESKTOP_TARGETS.has(target)) throw new Error(`Unexpected updater target: ${target}`);
    if (typeof artifact?.signature !== 'string' || artifact.signature.trim().length < 64) {
      throw new Error(`Updater signature is invalid for ${target}.`);
    }
    if (!isTrustedReleaseUrl(artifact.url)) throw new Error(`Updater URL is not trusted for ${target}.`);
  }

  if (feed.downloads) {
    for (const [platform, download] of Object.entries(feed.downloads)) {
      if ((platform !== 'windows' && platform !== 'mac') || !download) continue;
      if (!isTrustedReleaseUrl(download.url)) throw new Error(`Download URL is not trusted for ${platform}.`);
    }
  }

  return feed;
}

function validateMobileRelease(value: unknown, platform: 'android' | 'ios'): MobileRelease {
  if (!value || typeof value !== 'object') throw new Error('Mobile release is missing.');
  const release = value as MobileRelease;
  if (release.platform !== platform) throw new Error('Mobile release platform mismatch.');
  if (!isSemver(release.version)) throw new Error('Mobile release version is invalid.');
  if (!isTrustedReleaseUrl(release.downloadUrl)) throw new Error('Mobile download URL is not trusted.');
  return release;
}

function isSemver(value: unknown): value is string {
  return typeof value === 'string' && /^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$/.test(value);
}

function extensionOf(filePath: string): string {
  if (filePath.endsWith('.tar.gz')) return '.gz';
  return path.extname(filePath).toLowerCase();
}

function contentTypeFor(ext: string): string {
  switch (ext) {
    case '.json':
      return 'application/json; charset=utf-8';
    case '.txt':
      return 'text/plain; charset=utf-8';
    default:
      return 'application/octet-stream';
  }
}
