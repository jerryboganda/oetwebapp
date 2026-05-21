# Speaking Module — Desktop (Electron) Audio Recording

## Architecture

```
[Renderer] --(window.desktopBridge.speakingAudio)--> [Preload] --(ipcRenderer.invoke)--> [Main]
```

The preload script exposes a thin surface via `contextBridge`. Main owns the actual recording (or delegates back to the renderer's MediaRecorder — current state).

## Permissions

Main process registers a permission handler:

```ts
session.defaultSession.setPermissionRequestHandler((webContents, permission, callback) => {
  if (permission === 'media') return callback(true);
  callback(false);
});
```

## IPC channels (reserved)

- `speaking:audio:start` — begin a recording session.
- `speaking:audio:stop` — return base64 audio + MIME type.
- `speaking:audio:get-platform` — return `process.platform`.

## macOS entitlements

Add to the production `*.entitlements`:

```xml
<key>com.apple.security.device.microphone</key>
<true/>
```

`electron-builder.yml` must reference the entitlements file under `mac.entitlements`.

## Dev troubleshooting

- **Windows UAC**: ensure the microphone is allowed in Windows Settings → Privacy → Microphone.
- **macOS TCC**: `tccutil reset Microphone com.your.bundle.id` clears stale permission grants.
- **Linux**: ALSA / PulseAudio capture device must be the default; check `pavucontrol`.

## Bridge usage from renderer

```ts
import { createSpeakingAudioRecorder } from '@/lib/desktop/speaking-audio-bridge';
const recorder = createSpeakingAudioRecorder();
await recorder.start();
// later
const blob = await recorder.stop();
```
