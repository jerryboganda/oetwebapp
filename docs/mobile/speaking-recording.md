# Speaking Module — Mobile Audio Recording

## Platform matrix

| Platform | Recorder | Format | Notes |
|----------|----------|--------|-------|
| Web (Chrome / Edge / Safari 17+) | `MediaRecorder` | `audio/webm;codecs=opus` | TLS-required |
| Capacitor iOS | `@capacitor-community/voice-recorder` | `audio/m4a` | needs `NSMicrophoneUsageDescription` |
| Capacitor Android | same plugin | `audio/webm` (where supported) or `audio/m4a` | needs `RECORD_AUDIO` permission |
| Electron Desktop | IPC bridge (see `docs/desktop/speaking-recording.md`) | platform-dependent | uses `setPermissionRequestHandler` |

## Permission flow

1. `requestMicrophonePermission()` from `lib/native/capacitor-permissions.ts` returns one of `granted | denied | prompt | unsupported`.
2. On `denied`, the UI must show a remediation path (settings deep-link on native; Chrome settings tip on web).
3. After grant, `createAudioRecorder()` from `lib/native/audio-recorder-bridge.ts` returns a platform-appropriate recorder.

## File-size + duration cap

- 8 MiB per chunk, 200 MiB per session (enforced server-side via `MockBookingRecordingService` shape).
- Hard duration cap matches `LiveKitOptions.DefaultMaxDurationSeconds` (default 1800s).

## Retry on permission denied

- Show a one-tap "Re-request microphone" CTA.
- After 3 denials, prompt to open OS settings.

## Capacitor config additions

```ts
// capacitor.config.ts
const config: CapacitorConfig = {
  // …existing keys preserved…
  plugins: {
    VoiceRecorder: {
      // plugin-specific options if needed
    },
  },
};
```

## iOS Info.plist

```xml
<key>NSMicrophoneUsageDescription</key>
<string>OET Speaking practice requires access to your microphone to record your responses.</string>
```

## Android manifest

```xml
<uses-permission android:name="android.permission.RECORD_AUDIO" />
```
