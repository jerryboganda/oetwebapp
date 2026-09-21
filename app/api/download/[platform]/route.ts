import { NextRequest, NextResponse } from 'next/server';
import {
  getDownloadFallbackUrl,
  isMacDownloadDisabled,
  type DownloadPlatform,
  resolveDownloadUrl,
} from '@/lib/native-releases';

const PLATFORMS = new Set<DownloadPlatform>(['windows', 'mac', 'android', 'ios']);

/**
 * Direct-download resolver for native installers hosted on production.
 * Unknown platforms and missing catalogs fall back to /get-app — never GitHub.
 * The mac platform answers 503 while the handover kill-switch is armed
 * (17 Sep 2026: no protected-playback QA on a real Mac yet).
 */
export async function GET(
  _req: NextRequest,
  { params }: { params: Promise<{ platform: string }> },
) {
  const { platform } = await params;
  if (!PLATFORMS.has(platform as DownloadPlatform)) {
    return NextResponse.redirect(getDownloadFallbackUrl(), 302);
  }

  if (platform === 'mac' && isMacDownloadDisabled()) {
    return NextResponse.json(
      {
        error: 'mac-download-disabled',
        message:
          'The Mac download is temporarily unavailable while the macOS app completes its release QA. Please use the Web App at https://app.oetwithdrhesham.co.uk in the meantime.',
      },
      { status: 503, headers: { 'Cache-Control': 'no-store' } },
    );
  }

  const downloadUrl = resolveDownloadUrl(platform as DownloadPlatform);
  if (!downloadUrl) {
    return NextResponse.redirect(getDownloadFallbackUrl(), 302);
  }

  return NextResponse.redirect(downloadUrl, 302);
}
