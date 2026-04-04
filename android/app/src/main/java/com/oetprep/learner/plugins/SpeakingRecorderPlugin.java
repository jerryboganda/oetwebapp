package com.oetprep.learner.plugins;

import android.Manifest;
import android.media.MediaRecorder;
import android.os.Build;
import android.os.SystemClock;
import android.util.Base64;

import androidx.annotation.NonNull;

import com.getcapacitor.JSObject;
import com.getcapacitor.PermissionState;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.annotation.CapacitorPlugin;
import com.getcapacitor.annotation.Permission;
import com.getcapacitor.annotation.PermissionCallback;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;

@CapacitorPlugin(name = "SpeakingRecorder", permissions = {@Permission(alias = "microphone", strings = {Manifest.permission.RECORD_AUDIO})})
public class SpeakingRecorderPlugin extends Plugin {
    private static final String MIME_TYPE = "audio/mp4";

    private MediaRecorder recorder;
    private File recordingFile;
    private long startedAtMs;
    private long pausedAtMs;
    private long pausedDurationMs;
    private boolean recording;
    private boolean paused;

    @com.getcapacitor.PluginMethod
    public void start(PluginCall call) {
        if (getPermissionState("microphone") != PermissionState.GRANTED) {
            saveCall(call);
            requestPermissionForAlias("microphone", call, "microphonePermissionCallback");
            return;
        }

        startRecording(call);
    }

    @PermissionCallback
    private void microphonePermissionCallback(PluginCall call) {
        if (getPermissionState("microphone") == PermissionState.GRANTED) {
            startRecording(call);
            return;
        }

        call.reject("Microphone permission was denied.");
    }

    @com.getcapacitor.PluginMethod
    public void pause(PluginCall call) {
        if (!recording || recorder == null) {
            call.reject("No active recording.");
            return;
        }

        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.N) {
            call.reject("Pausing recordings requires Android 7.0 or newer.");
            return;
        }

        if (paused) {
            call.resolve();
            return;
        }

        try {
            recorder.pause();
            paused = true;
            pausedAtMs = SystemClock.elapsedRealtime();
            call.resolve();
        } catch (Exception error) {
            call.reject("Unable to pause the native recording.", error);
        }
    }

    @com.getcapacitor.PluginMethod
    public void resume(PluginCall call) {
        if (!recording || recorder == null) {
            call.reject("No active recording.");
            return;
        }

        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.N) {
            call.reject("Resuming recordings requires Android 7.0 or newer.");
            return;
        }

        if (!paused) {
            call.resolve();
            return;
        }

        try {
            recorder.resume();
            paused = false;
            pausedDurationMs += SystemClock.elapsedRealtime() - pausedAtMs;
            pausedAtMs = 0L;
            call.resolve();
        } catch (Exception error) {
            call.reject("Unable to resume the native recording.", error);
        }
    }

    @com.getcapacitor.PluginMethod
    public void stop(PluginCall call) {
        if (!recording || recorder == null || recordingFile == null) {
            call.reject("No active recording.");
            return;
        }

        final File fileToRead = recordingFile;
        final MediaRecorder recorderToStop = recorder;
        final boolean wasPaused = paused;

        try {
            if (wasPaused && Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
                recorderToStop.resume();
            }

            recorderToStop.stop();
        } catch (RuntimeException error) {
            cleanupRecorder(true);
            call.reject("Failed to stop the native recording.", error);
            return;
        }

        long durationMs = SystemClock.elapsedRealtime() - startedAtMs - pausedDurationMs;
        if (wasPaused && pausedAtMs > 0L) {
            durationMs -= SystemClock.elapsedRealtime() - pausedAtMs;
        }

        String base64;
        try {
            base64 = readFileAsBase64(fileToRead);
        } catch (IOException error) {
            cleanupRecorder(true);
            call.reject("Failed to read the native recording.", error);
            return;
        }

        cleanupRecorder(true);

        JSObject result = new JSObject();
        result.put("base64", base64);
        result.put("mimeType", MIME_TYPE);
        result.put("fileName", fileToRead.getName());
        result.put("durationMs", Math.max(durationMs, 0));
        call.resolve(result);
    }

    @com.getcapacitor.PluginMethod
    public void cancel(PluginCall call) {
        cleanupRecorder(true);
        call.resolve();
    }

    private void startRecording(PluginCall call) {
        if (recording) {
            call.reject("A native recording is already in progress.");
            return;
        }

        File recordingDirectory = new File(getContext().getCacheDir(), "speaking-recordings");
        if (!recordingDirectory.exists() && !recordingDirectory.mkdirs()) {
            call.reject("Unable to create the native recording directory.");
            return;
        }

        try {
            recordingFile = File.createTempFile("speaking-", ".m4a", recordingDirectory);
            recorder = new MediaRecorder();
            recorder.setAudioSource(MediaRecorder.AudioSource.MIC);
            recorder.setOutputFormat(MediaRecorder.OutputFormat.MPEG_4);
            recorder.setAudioEncoder(MediaRecorder.AudioEncoder.AAC);
            recorder.setAudioEncodingBitRate(128_000);
            recorder.setAudioSamplingRate(44_100);
            recorder.setOutputFile(recordingFile.getAbsolutePath());
            recorder.prepare();
            recorder.start();

            recording = true;
            paused = false;
            startedAtMs = SystemClock.elapsedRealtime();
            pausedAtMs = 0L;
            pausedDurationMs = 0L;

            JSObject result = new JSObject();
            result.put("mimeType", MIME_TYPE);
            result.put("fileName", recordingFile.getName());
            result.put("startedAt", System.currentTimeMillis());
            call.resolve(result);
        } catch (IOException | RuntimeException error) {
            cleanupRecorder(true);
            call.reject("Failed to start the native recording.", error);
        }
    }

    private void cleanupRecorder(boolean deleteFile) {
        recording = false;
        paused = false;
        pausedAtMs = 0L;
        pausedDurationMs = 0L;

        if (recorder != null) {
            try {
                recorder.reset();
            } catch (Exception ignored) {
            }

            try {
                recorder.release();
            } catch (Exception ignored) {
            }

            recorder = null;
        }

        if (deleteFile && recordingFile != null && recordingFile.exists()) {
            // Best-effort cleanup only.
            //noinspection ResultOfMethodCallIgnored
            recordingFile.delete();
        }

        recordingFile = null;
    }

    @NonNull
    private String readFileAsBase64(File file) throws IOException {
        try (FileInputStream inputStream = new FileInputStream(file);
             ByteArrayOutputStream buffer = new ByteArrayOutputStream()) {
            byte[] bytes = new byte[8_192];
            int read;
            while ((read = inputStream.read(bytes)) != -1) {
                buffer.write(bytes, 0, read);
            }
            return Base64.encodeToString(buffer.toByteArray(), Base64.NO_WRAP);
        }
    }
}
