using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Speaking;

/// <summary>One candidate clip, already opened from storage. The transcoder reads it once and never keeps it.</summary>
public sealed record SpeakingAudioClipInput(Stream Content, string MimeType);

/// <summary>The joined audio sent to the model: one mp3, how long it is and how many clips went into it.</summary>
public sealed record SpeakingAudioJoin(byte[] Mp3, int DurationMs, int ClipCount, bool Truncated);

/// <summary>The transcoder cannot run (ffmpeg missing or failing): the audio stage reports "unavailable", grading goes on.</summary>
public sealed class SpeakingAudioTranscoderUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Turns the candidate's stored clips (webm/opus from a browser, mp4/aac from Safari, wav, mp3) into the one mp3 the
/// OpenAI audio chat API accepts: every clip decoded to 16 kHz mono, a short silence between clips, the whole cut
/// at a maximum length, encoded at 48 kbps.
/// </summary>
public interface ISpeakingAudioTranscoder
{
    Task<SpeakingAudioJoin> JoinToMp3Async(IReadOnlyList<SpeakingAudioClipInput> clips, CancellationToken ct);
}

/// <summary>
/// ffmpeg through standard input/output only: audio is never written to the container's disk (media and user data
/// stay behind <c>IFileStorage</c>). One short process per clip decodes it to raw 16 kHz mono PCM; the PCM is joined
/// in memory by <see cref="PcmJoiner"/>; one last process encodes the mp3.
/// </summary>
public sealed class FfmpegSpeakingAudioTranscoder(IOptions<SpeakingAudioAssessmentOptions> options) : ISpeakingAudioTranscoder
{
    private static readonly TimeSpan DecodeTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan EncodeTimeout = TimeSpan.FromSeconds(90);

    public async Task<SpeakingAudioJoin> JoinToMp3Async(IReadOnlyList<SpeakingAudioClipInput> clips, CancellationToken ct)
    {
        if (clips.Count == 0)
        {
            throw new ArgumentException("At least one clip is required.", nameof(clips));
        }

        var settings = options.Value;
        var decoded = new List<byte[]>(clips.Count);
        foreach (var clip in clips)
        {
            decoded.Add(await RunAsync(
                settings.FfmpegPath,
                ["-hide_banner", "-loglevel", "error", "-nostdin", "-i", "pipe:0", "-vn", "-ac", "1", "-ar", PcmJoiner.SampleRate.ToString(), "-f", "s16le", "pipe:1"],
                clip.Content,
                DecodeTimeout,
                ct));
        }

        var joined = PcmJoiner.Join(decoded, settings.GapMilliseconds, settings.MaxAudioSeconds);
        var mp3 = await RunAsync(
            settings.FfmpegPath,
            ["-hide_banner", "-loglevel", "error", "-nostdin", "-f", "s16le", "-ar", PcmJoiner.SampleRate.ToString(), "-ac", "1", "-i", "pipe:0", "-c:a", "libmp3lame", "-b:a", "48k", "-f", "mp3", "pipe:1"],
            new MemoryStream(joined.Pcm),
            EncodeTimeout,
            ct);
        if (mp3.Length == 0)
        {
            throw new SpeakingAudioTranscoderUnavailableException("ffmpeg produced no audio.");
        }

        return new SpeakingAudioJoin(mp3, joined.DurationMs, clips.Count, joined.Truncated);
    }

    /// <summary>Runs one ffmpeg process: <paramref name="input"/> into its stdin, its stdout returned. Killed on timeout.</summary>
    private static async Task<byte[]> RunAsync(string executable, string[] arguments, Stream input, TimeSpan timeout, CancellationToken ct)
    {
        var start = new ProcessStartInfo(string.IsNullOrWhiteSpace(executable) ? "ffmpeg" : executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new SpeakingAudioTranscoderUnavailableException("ffmpeg could not be started.");
        }
        catch (Win32Exception ex)
        {
            throw new SpeakingAudioTranscoderUnavailableException("ffmpeg is not installed.", ex);
        }

        using (process)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(timeout);
            var output = new MemoryStream();
            var errorText = new MemoryStream();
            try
            {
                var feed = Task.Run(async () =>
                {
                    try
                    {
                        await input.CopyToAsync(process.StandardInput.BaseStream, budget.Token);
                    }
                    catch (IOException)
                    {
                        // ffmpeg closed its input early (it had all it needed, or it failed): the exit code decides.
                    }
                    finally
                    {
                        process.StandardInput.Close();
                    }
                }, budget.Token);
                var read = process.StandardOutput.BaseStream.CopyToAsync(output, budget.Token);
                var errors = process.StandardError.BaseStream.CopyToAsync(errorText, budget.Token);
                await Task.WhenAll(feed, read, errors);
                await process.WaitForExitAsync(budget.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TryKill(process);
                throw new SpeakingAudioTranscoderUnavailableException("ffmpeg timed out.");
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            if (process.ExitCode != 0)
            {
                throw new SpeakingAudioTranscoderUnavailableException(
                    $"ffmpeg exited with code {process.ExitCode}: {Trim(System.Text.Encoding.UTF8.GetString(errorText.ToArray()))}");
            }

            return output.ToArray();
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }

    private static string Trim(string text) => text.Length <= 300 ? text.Trim() : text[..300].Trim();
}
