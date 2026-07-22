import { NextResponse } from 'next/server';

const RELEASES_API = 'https://api.github.com/repos/jerryboganda/oetwebapp/releases?per_page=30';
const DESKTOP_TAG_PATTERN = /^v(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)-tauri-desktop$/;
const ALLOWED_TARGETS = new Set(['windows-x86_64', 'darwin-aarch64', 'darwin-x86_64']);

interface GithubRelease {
  tag_name: string;
  draft: boolean;
  prerelease: boolean;
  assets?: Array<{ name: string; browser_download_url: string }>;
}

interface TauriFeed {
  version: string;
  notes?: string;
  pub_date?: string;
  platforms: Record<string, { signature: string; url: string }>;
}

/**
 * Stable, desktop-only Tauri feed. GitHub's global "latest release" can point
 * at an Android release, so this resolver selects the newest desktop tag and
 * validates its signed manifest before proxying it to installed clients.
 */
export async function GET() {
  try {
    const releasesResponse = await fetch(RELEASES_API, {
      headers: {
        Accept: 'application/vnd.github+json',
        'User-Agent': 'oet-tauri-update-feed',
      },
      next: { revalidate: 300 },
    });
    if (!releasesResponse.ok) throw new Error(`GitHub releases returned ${releasesResponse.status}`);

    const releases = await releasesResponse.json() as GithubRelease[];
    const release = releases.find((candidate) =>
      !candidate.draft && !candidate.prerelease && DESKTOP_TAG_PATTERN.test(candidate.tag_name));
    const manifestAsset = release?.assets?.find((asset) => asset.name === 'latest.json');
    if (!release || !manifestAsset || !isTrustedGithubDownload(manifestAsset.browser_download_url)) {
      throw new Error('No trusted desktop updater manifest is published.');
    }

    const manifestResponse = await fetch(manifestAsset.browser_download_url, {
      headers: { Accept: 'application/json', 'User-Agent': 'oet-tauri-update-feed' },
      next: { revalidate: 300 },
    });
    if (!manifestResponse.ok) throw new Error(`Updater manifest returned ${manifestResponse.status}`);
    const manifest = await manifestResponse.json() as TauriFeed;
    validateManifest(manifest, release.tag_name);

    return NextResponse.json(manifest, {
      headers: { 'Cache-Control': 'public, max-age=30, s-maxage=300, stale-while-revalidate=600' },
    });
  } catch {
    // A non-2xx response makes Tauri try the secondary GitHub endpoint.
    return NextResponse.json({ error: 'Desktop updater feed is temporarily unavailable.' }, {
      status: 503,
      headers: { 'Cache-Control': 'no-store' },
    });
  }
}

function validateManifest(manifest: TauriFeed, tag: string): void {
  const tagMatch = DESKTOP_TAG_PATTERN.exec(tag);
  if (!tagMatch || manifest.version !== tagMatch[1]) throw new Error('Updater version does not match its release tag.');
  if (!manifest.platforms || typeof manifest.platforms !== 'object') throw new Error('Updater platforms are missing.');
  if (!manifest.platforms['windows-x86_64']) throw new Error('Windows updater artifact is required.');

  for (const [target, artifact] of Object.entries(manifest.platforms)) {
    if (!ALLOWED_TARGETS.has(target)) throw new Error(`Unexpected updater target: ${target}`);
    if (typeof artifact.signature !== 'string' || artifact.signature.trim().length < 64) {
      throw new Error(`Updater signature is invalid for ${target}.`);
    }
    if (!isTrustedGithubDownload(artifact.url)) throw new Error(`Updater URL is not trusted for ${target}.`);
  }
}

function isTrustedGithubDownload(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === 'https:' && url.hostname === 'github.com'
      && url.pathname.startsWith('/jerryboganda/oetwebapp/releases/download/');
  } catch {
    return false;
  }
}
