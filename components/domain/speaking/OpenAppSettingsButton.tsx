'use client';

import { useState } from 'react';
import { Capacitor } from '@capacitor/core';
import { Button } from '@/components/ui/button';
import { tryOpenNativeAppSettings } from '@/lib/mobile/speaking-recorder';

/** Recovery path after a microphone denial inside the native apps; renders nothing on the web. */
export function OpenAppSettingsButton({ className }: { className?: string }) {
  const [unavailable, setUnavailable] = useState(false);

  if (!Capacitor.isNativePlatform()) {
    return null;
  }

  if (unavailable) {
    return (
      <p className="text-xs text-muted">
        Open your device Settings → Apps → OET with Dr Ahmed Hesham → Permissions → Microphone, choose Allow, then come back and press Start recording.
      </p>
    );
  }

  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      className={className}
      onClick={async () => {
        if (!(await tryOpenNativeAppSettings())) setUnavailable(true);
      }}
    >
      Open app settings
    </Button>
  );
}
