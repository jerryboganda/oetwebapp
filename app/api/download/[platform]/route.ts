import { NextRequest, NextResponse } from 'next/server';
import { GITHUB_RELEASES_URL } from '@/lib/app-downloads';

/**
 * Direct-download resolver for the native installers.
 *
 * The Windows (.exe), macOS (.dmg), Android (.apk), and iOS (.ipa) installers
 * are published as GitHub Release assets with version-stamped filenames (e.g.
 * `OET.with.Dr.Hesham_0.6.3_x64-setup.exe`), so a static link can't point at
 * them. This route queries the GitHub Releases API, finds the newest release
 * that carries the requested asset, and 302-redirects the browser straight to
 * the file — so a click starts the download instead of dumping the user on the
 * GitHub releases page.
 */

const GITHUB_RELEASES_API =
  'https://api.github.com/repos/jerryboganda/oetwebapp/releases?per_page=20';

type PlatformKey = 'windows' | 'mac' | 'android' | 'ios';

const ASSET_MATCHERS: Record<PlatformKey, (name: string) => boolean> = {
  windows: (name) => name.toLowerCase().endsWith('-setup.exe') || name.toLowerCase().endsWith('.exe'),
  mac: (name) => name.toLowerCase().endsWith('.dmg'),
  android: (name) => name.toLowerCase().endsWith('.apk'),
  ios: (name) => name.toLowerCase().endsWith('.ipa'),
};

const IOS_RELEASE_TAG = /^v\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?-mobile-ios$/;

interface GithubAsset {
  name: string;
  browser_download_url: string;
}

interface GithubRelease {
  tag_name: string;
  draft: boolean;
  prerelease: boolean;
  assets: GithubAsset[];
}

export async function GET(
  _req: NextRequest,
  { params }: { params: Promise<{ platform: string }> },
) {
  const { platform } = await params;
  const key = platform as PlatformKey;
  const matcher = ASSET_MATCHERS[key];

  if (!matcher) {
    return NextResponse.redirect(GITHUB_RELEASES_URL, 302);
  }

  try {
    const res = await fetch(GITHUB_RELEASES_API, {
      headers: {
        Accept: 'application/vnd.github+json',
        'User-Agent': 'oet-prep-web',
      },
      // Cache at the edge for an hour — releases change rarely.
      next: { revalidate: 3600 },
    });

    if (!res.ok) {
      return NextResponse.redirect(GITHUB_RELEASES_URL, 302);
    }

    const releases = (await res.json()) as GithubRelease[];

    for (const release of releases) {
      if (release.draft || release.prerelease) continue;
      if (key === 'ios' && !IOS_RELEASE_TAG.test(release.tag_name)) continue;
      const asset = release.assets?.find((a) => matcher(a.name));
      if (asset?.browser_download_url && isTrustedGithubDownload(asset.browser_download_url)) {
        return NextResponse.redirect(asset.browser_download_url, 302);
      }
    }
  } catch {
    // fall through to the releases page
  }

  return NextResponse.redirect(GITHUB_RELEASES_URL, 302);
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
