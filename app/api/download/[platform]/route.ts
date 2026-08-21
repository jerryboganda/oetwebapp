import { NextRequest, NextResponse } from 'next/server';
import {
  DOWNLOAD_FALLBACK_PATH,
  type DownloadPlatform,
  resolveDownloadUrl,
} from '@/lib/native-releases';

const PLATFORMS = new Set<DownloadPlatform>(['windows', 'mac', 'android', 'ios']);

/**
 * Direct-download resolver for native installers hosted on production.
 * Unknown platforms and missing catalogs fall back to /get-app — never GitHub.
 */
export async function GET(
  req: NextRequest,
  { params }: { params: Promise<{ platform: string }> },
) {
  const { platform } = await params;
  if (!PLATFORMS.has(platform as DownloadPlatform)) {
    return NextResponse.redirect(new URL(DOWNLOAD_FALLBACK_PATH, req.url), 302);
  }

  const downloadUrl = resolveDownloadUrl(platform as DownloadPlatform);
  if (!downloadUrl) {
    return NextResponse.redirect(new URL(DOWNLOAD_FALLBACK_PATH, req.url), 302);
  }

  return NextResponse.redirect(downloadUrl, 302);
}
