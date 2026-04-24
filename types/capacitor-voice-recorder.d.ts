/**
 * Ambient module declaration for the optional peer dependency
 * `@capacitor-community/voice-recorder`.
 *
 * The package is intentionally not installed in this repo — it only ships
 * with native mobile builds and is pulled in dynamically at runtime from
 * `lib/mobile/pronunciation-recorder.ts`. On web/desktop builds the
 * dynamic import is guarded by `Capacitor.isNativePlatform()` and wrapped
 * in try/catch, so a resolution failure is the expected happy path.
 *
 * Before this declaration the dynamic `import(...)` call required a
 * `@ts-expect-error` suppression because TypeScript could not resolve
 * the specifier. With this ambient module the specifier resolves to a
 * minimal shape that matches the local `VoiceRecorderPlugin` interface
 * in `pronunciation-recorder.ts`, and the suppression can be removed.
 *
 * Why a minimal shape instead of the real `.d.ts`: the real package
 * bundles a full plugin surface (RecordingData, GenericResponse, etc.)
 * that we do not consume. Keeping the declaration narrow means the
 * compile-time contract matches the runtime contract that our wrapper
 * actually enforces — no accidental coupling to plugin internals.
 *
 * See: lib/mobile/pronunciation-recorder.ts
 */
declare module '@capacitor-community/voice-recorder' {
	export interface VoiceRecorderPlugin {
		requestAudioRecordingPermission(): Promise<{ value: boolean }>;
		hasAudioRecordingPermission(): Promise<{ value: boolean }>;
		startRecording(): Promise<{ value: boolean }>;
		stopRecording(): Promise<{
			value: {
				recordDataBase64: string;
				msDuration: number;
				mimeType: string;
			};
		}>;
		pauseRecording(): Promise<{ value: boolean }>;
		resumeRecording(): Promise<{ value: boolean }>;
		getCurrentStatus(): Promise<{ status: string }>;
	}

	export const VoiceRecorder: VoiceRecorderPlugin;
}
