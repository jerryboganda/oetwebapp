import { NextRequest, NextResponse } from 'next/server';

const RELEASES_API = 'https://api.github.com/repos/jerryboganda/oetwebapp/releases?per_page=30';
const VERSION_PATTERN = /^v(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)-mobile-(android|ios)$/;

interface GithubAsset {
  name: string;
  browser_download_url: string;
  digest?: string | null;
}

interface GithubRelease {
  tag_name: string;
  draft: boolean;
  prerelease: boolean;
  published_at?: string | null;
  assets?: GithubAsset[];
}

/**
 * Same-origin native release discovery for automatic mobile update checks.
 * Android GitHub-distributed builds use the signed APK as a safe fallback when
 * Google Play in-app updates are unavailable. iOS is returned only after an IPA
 * release exists; production iOS installs still hand off to the App Store.
 */
export async function GET(request: NextRequest) {
  const platform = request.nextUrl.searchParams.get('platform');
  if (platform !== 'android' && platform !== 'ios') {
    return NextResponse.json({ error: 'platform must be android or ios' }, { status: 400 });
  }

  try {
    const response = await fetch(RELEASES_API, {
      headers: {
        Accept: 'application/vnd.github+json',
        'User-Agent': 'oet-native-release-discovery',
      },
      next: { revalidate: 300 },
    });
    if (!response.ok) throw new Error(`GitHub releases returned ${response.status}`);

    const releases = await response.json() as GithubRelease[];
    for (const release of releases) {
      if (release.draft || release.prerelease) continue;
      const match = VERSION_PATTERN.exec(release.tag_name);
      if (!match || match[2] !== platform) continue;
      const expectedSuffix = platform === 'android' ? '.apk' : '.ipa';
      const asset = release.assets?.find((candidate) => candidate.name.toLowerCase().endsWith(expectedSuffix));
      if (!asset || !isTrustedGithubDownload(asset.browser_download_url)) continue;

      return NextResponse.json({
        platform,
        version: match[1],
        downloadUrl: asset.browser_download_url,
        digest: asset.digest ?? null,
        publishedAt: release.published_at ?? null,
      }, {
        headers: { 'Cache-Control': 'public, max-age=60, s-maxage=300, stale-while-revalidate=600' },
      });
    }

    return NextResponse.json({ error: `No ${platform} release is published.` }, { status: 404 });
  } catch {
    return NextResponse.json({ error: 'Native release discovery is temporarily unavailable.' }, {
      status: 503,
      headers: { 'Cache-Control': 'no-store' },
    });
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
