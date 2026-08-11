using System.Text;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Performs a conservative signal-level check on formats that can be decoded
/// without an external media process. The browser normally records Opus in a
/// WebM container, so those recordings remain valid and are explicitly marked
/// as decoder-limited; the ASR provider remains authoritative for their
/// intelligibility. PCM/WAV recordings receive deterministic clipping,
/// near-silence, and high-energy-noise checks. A detected defect is a
/// technical-review condition and is never converted into a language score.
/// </summary>
public static class SpeakingSimulationV11AudioSignalAnalyzer
{
    private const int MaxFramesToAnalyze = 1_000_000;
    private const double ClippingSampleThreshold = 0.999;
    private const double ClippingRatioThreshold = 0.01;
    private const double NearSilenceRmsThreshold = 0.003;
    private const double HighEnergyNoiseRmsThreshold = 0.65;
    private const double HighEnergyNoiseZeroCrossingThreshold = 0.20;

    public static SpeakingSimulationV11AudioSignalAnalysis Analyze(
        Stream stream,
        string? mimeType)
    {
        if (stream is null || !stream.CanRead)
        {
            return Limited("unknown", mimeType, "audio_signal_stream_unreadable");
        }

        if (!stream.CanSeek)
        {
            return Limited("unknown", mimeType, "audio_signal_stream_not_seekable");
        }

        try
        {
            stream.Position = 0;
            var header = new byte[12];
            if (stream.Read(header, 0, header.Length) != header.Length)
            {
                return Unparseable("unknown", mimeType, "audio_signal_header_missing");
            }

            if (!AsciiEquals(header, 0, "RIFF") || !AsciiEquals(header, 8, "WAVE"))
            {
                return Limited(DetectCodec(header, mimeType), mimeType, null);
            }

            var format = ReadWaveChunks(stream);
            if (format is null)
            {
                return Unparseable("pcm-wav", mimeType, "audio_signal_wav_unparseable");
            }

            if (format.AudioFormat != 1
                || format.Channels <= 0
                || format.SampleRateHz <= 0
                || format.BitsPerSample is not (8 or 16 or 24 or 32))
            {
                return Limited(
                    format.AudioFormat == 3 ? "ieee-float-wav" : "wav",
                    mimeType,
                    null,
                    format.SampleRateHz,
                    format.Channels);
            }

            return AnalyzePcm(stream, format, mimeType);
        }
        catch (EndOfStreamException)
        {
            return Unparseable("unknown", mimeType, "audio_signal_truncated");
        }
        catch (IOException)
        {
            return Unparseable("unknown", mimeType, "audio_signal_read_failed");
        }
        catch (ArgumentOutOfRangeException)
        {
            return Unparseable("unknown", mimeType, "audio_signal_bounds_invalid");
        }
    }

    private static SpeakingSimulationV11AudioSignalAnalysis AnalyzePcm(
        Stream stream,
        WaveFormat format,
        string? mimeType)
    {
        var bytesPerSample = format.BitsPerSample / 8;
        var frameBytes = checked(format.Channels * bytesPerSample);
        var completeFrames = format.DataSize / frameBytes;
        if (completeFrames <= 0)
        {
            return Unparseable("pcm-wav", mimeType, "audio_signal_samples_missing",
                format.SampleRateHz, format.Channels);
        }

        var stride = Math.Max(1L, (completeFrames + MaxFramesToAnalyze - 1) / MaxFramesToAnalyze);
        var analyzedFrames = 0L;
        var sampleCount = 0L;
        var clippedSamples = 0L;
        var maxConsecutiveClipped = 0L;
        var consecutiveClipped = 0L;
        var zeroCrossings = 0L;
        var hasPrevious = false;
        var previous = 0d;
        var sumSquares = 0d;
        var peak = 0d;

        stream.Position = format.DataOffset;
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        for (var frame = 0L; frame < completeFrames; frame++)
        {
            if (frame % stride != 0)
            {
                stream.Position += frameBytes;
                continue;
            }

            analyzedFrames++;
            var frameClipped = false;
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var value = ReadPcmSample(reader, format.BitsPerSample);
                var absolute = Math.Abs(value);
                sampleCount++;
                sumSquares += value * value;
                peak = Math.Max(peak, absolute);

                if (absolute >= ClippingSampleThreshold)
                {
                    clippedSamples++;
                    frameClipped = true;
                }

                if (hasPrevious && ((previous < 0 && value >= 0) || (previous >= 0 && value < 0)))
                {
                    zeroCrossings++;
                }

                previous = value;
                hasPrevious = true;
            }

            if (frameClipped)
            {
                consecutiveClipped++;
                maxConsecutiveClipped = Math.Max(maxConsecutiveClipped, consecutiveClipped);
            }
            else
            {
                consecutiveClipped = 0;
            }
        }

        if (sampleCount == 0 || analyzedFrames == 0)
        {
            return Unparseable("pcm-wav", mimeType, "audio_signal_samples_missing",
                format.SampleRateHz, format.Channels);
        }

        var rms = Math.Sqrt(sumSquares / sampleCount);
        var clippingRatio = clippedSamples / (double)sampleCount;
        var zeroCrossingRate = zeroCrossings / (double)Math.Max(1, sampleCount - 1);
        var clippingDetected = clippingRatio >= ClippingRatioThreshold
            || maxConsecutiveClipped >= Math.Max(16, format.SampleRateHz / 100);
        var nearSilenceDetected = rms < NearSilenceRmsThreshold
            && analyzedFrames >= Math.Max(1, format.SampleRateHz / 4);
        var severeNoiseDetected = !clippingDetected
            && rms >= HighEnergyNoiseRmsThreshold
            && zeroCrossingRate >= HighEnergyNoiseZeroCrossingThreshold;

        var issueCode = clippingDetected
            ? "audio_clipping_detected"
            : severeNoiseDetected
                ? "audio_severe_noise_detected"
                : nearSilenceDetected
                    ? "audio_near_silent"
                    : null;

        return new SpeakingSimulationV11AudioSignalAnalysis(
            Analyzed: true,
            AnalysisLimited: false,
            Codec: "pcm-wav",
            SampleRateHz: format.SampleRateHz,
            Channels: format.Channels,
            SampleCount: sampleCount,
            Peak: Math.Round(peak, 6),
            Rms: Math.Round(rms, 6),
            ZeroCrossingRate: Math.Round(zeroCrossingRate, 6),
            ClippingDetected: clippingDetected,
            SevereNoiseDetected: severeNoiseDetected,
            NearSilenceDetected: nearSilenceDetected,
            IssueCode: issueCode,
            Details: new Dictionary<string, object?>
            {
                ["signalAnalysis"] = "pcm_wav",
                ["analysisLimited"] = false,
                ["sampleRateHz"] = format.SampleRateHz,
                ["channels"] = format.Channels,
                ["bitsPerSample"] = format.BitsPerSample,
                ["sampleCount"] = sampleCount,
                ["peak"] = Math.Round(peak, 6),
                ["rms"] = Math.Round(rms, 6),
                ["zeroCrossingRate"] = Math.Round(zeroCrossingRate, 6),
                ["clippingRatio"] = Math.Round(clippingRatio, 6),
                ["maxConsecutiveClippedFrames"] = maxConsecutiveClipped,
                ["clippingDetected"] = clippingDetected,
                ["severeNoiseDetected"] = severeNoiseDetected,
                ["nearSilenceDetected"] = nearSilenceDetected,
                ["issueCode"] = issueCode,
            });
    }

    private static WaveFormat? ReadWaveChunks(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var audioFormat = 0;
        var channels = 0;
        var sampleRate = 0;
        var bitsPerSample = 0;
        long? dataOffset = null;
        long dataSize = 0;

        while (stream.Position <= stream.Length - 8)
        {
            var chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var chunkSize = reader.ReadUInt32();
            var chunkStart = stream.Position;
            if (chunkSize > stream.Length - chunkStart)
            {
                return null;
            }

            var chunkEnd = chunkStart + chunkSize;
            if (string.Equals(chunkId, "fmt ", StringComparison.Ordinal))
            {
                if (chunkSize < 16)
                {
                    return null;
                }

                audioFormat = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = checked((int)reader.ReadUInt32());
                _ = reader.ReadUInt32(); // byte rate
                _ = reader.ReadUInt16(); // block alignment
                bitsPerSample = reader.ReadUInt16();
            }
            else if (string.Equals(chunkId, "data", StringComparison.Ordinal))
            {
                dataOffset = chunkStart;
                dataSize = chunkSize;
            }

            stream.Position = chunkEnd + (chunkSize % 2);
        }

        return audioFormat == 0 || dataOffset is null
            ? null
            : new WaveFormat(audioFormat, channels, sampleRate, bitsPerSample,
                dataOffset.Value, dataSize);
    }

    private static double ReadPcmSample(BinaryReader reader, int bitsPerSample)
        => bitsPerSample switch
        {
            8 => (reader.ReadByte() - 128) / 128d,
            16 => reader.ReadInt16() / 32768d,
            24 => Read24BitSample(reader),
            32 => reader.ReadInt32() / 2147483648d,
            _ => throw new InvalidDataException("Unsupported PCM sample width."),
        };

    private static double Read24BitSample(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(3);
        if (bytes.Length != 3)
        {
            throw new EndOfStreamException();
        }

        var value = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
        if ((value & 0x0080_0000) != 0)
        {
            value |= unchecked((int)0xff00_0000);
        }

        return value / 8388608d;
    }

    private static SpeakingSimulationV11AudioSignalAnalysis Limited(
        string codec,
        string? mimeType,
        string? issueCode,
        int? sampleRateHz = null,
        int? channels = null)
        => new(
            Analyzed: false,
            AnalysisLimited: true,
            Codec: codec,
            SampleRateHz: sampleRateHz,
            Channels: channels,
            SampleCount: null,
            Peak: null,
            Rms: null,
            ZeroCrossingRate: null,
            ClippingDetected: false,
            SevereNoiseDetected: false,
            NearSilenceDetected: false,
            IssueCode: issueCode,
            Details: new Dictionary<string, object?>
            {
                ["signalAnalysis"] = "decoder_limited",
                ["analysisLimited"] = true,
                ["codec"] = codec,
                ["mimeType"] = mimeType,
                ["issueCode"] = issueCode,
            });

    private static SpeakingSimulationV11AudioSignalAnalysis Unparseable(
        string codec,
        string? mimeType,
        string issueCode,
        int? sampleRateHz = null,
        int? channels = null)
        => new(
            Analyzed: false,
            AnalysisLimited: false,
            Codec: codec,
            SampleRateHz: sampleRateHz,
            Channels: channels,
            SampleCount: null,
            Peak: null,
            Rms: null,
            ZeroCrossingRate: null,
            ClippingDetected: false,
            SevereNoiseDetected: false,
            NearSilenceDetected: false,
            IssueCode: issueCode,
            Details: new Dictionary<string, object?>
            {
                ["signalAnalysis"] = "unparseable",
                ["analysisLimited"] = false,
                ["codec"] = codec,
                ["mimeType"] = mimeType,
                ["issueCode"] = issueCode,
            });

    private static string DetectCodec(byte[] header, string? mimeType)
    {
        if (header.Length >= 4 && AsciiEquals(header, 0, "OggS")) return "ogg-opus";
        if (header.Length >= 4 && header[0] == 0x1a && header[1] == 0x45
            && header[2] == 0xdf && header[3] == 0xa3) return "webm-opus";
        if (header.Length >= 3 && AsciiEquals(header, 0, "ID3")) return "mp3";
        if (header.Length >= 2 && header[0] == 0xff && (header[1] & 0xe0) == 0xe0) return "mp3";
        return string.IsNullOrWhiteSpace(mimeType) ? "unknown" : mimeType.Trim().ToLowerInvariant();
    }

    private static bool AsciiEquals(byte[] bytes, int offset, string value)
    {
        if (offset < 0 || offset + value.Length > bytes.Length) return false;
        for (var index = 0; index < value.Length; index++)
        {
            if (bytes[offset + index] != value[index]) return false;
        }

        return true;
    }

    private sealed record WaveFormat(
        int AudioFormat,
        int Channels,
        int SampleRateHz,
        int BitsPerSample,
        long DataOffset,
        long DataSize);
}

public sealed record SpeakingSimulationV11AudioSignalAnalysis(
    bool Analyzed,
    bool AnalysisLimited,
    string Codec,
    int? SampleRateHz,
    int? Channels,
    long? SampleCount,
    double? Peak,
    double? Rms,
    double? ZeroCrossingRate,
    bool ClippingDetected,
    bool SevereNoiseDetected,
    bool NearSilenceDetected,
    string? IssueCode,
    IReadOnlyDictionary<string, object?> Details);
