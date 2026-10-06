using System;
using System.Collections.Generic;
using System.IO;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// Joins raw 16 kHz mono 16-bit PCM clips in memory: a silence between clips and a hard maximum length.
///
/// <para>
/// Pure static code with no dependency on options, configuration, logging or DI, in its own file on purpose: the remote
/// helper agent link-compiles THIS file verbatim (OET-RWP/1 section 6.4), so a helper and the primary join clips with the
/// very same code. Do not add a dependency to it, and treat any behavioural change as an engine change (it changes what
/// <c>media.speaking-join</c> produces, so bump the pinned engine version when you do).
/// </para>
/// </summary>
public static class PcmJoiner
{
    public const int SampleRate = 16_000;
    private const int BytesPerSample = 2;

    public sealed record Joined(byte[] Pcm, int DurationMs, bool Truncated);

    public static Joined Join(IReadOnlyList<byte[]> clips, int gapMilliseconds, int maxSeconds)
    {
        var gapBytes = BytesFor(Math.Max(0, gapMilliseconds));
        var limit = Math.Max(1, maxSeconds) * SampleRate * BytesPerSample;
        using var joined = new MemoryStream();
        var truncated = false;

        for (var i = 0; i < clips.Count && !truncated; i++)
        {
            var clip = clips[i];
            // Whole samples only: a stray odd byte would shift every later sample.
            var clipBytes = clip.Length - (clip.Length % BytesPerSample);
            if (clipBytes <= 0) continue;

            if (joined.Length > 0)
            {
                var gap = (int)Math.Min(gapBytes, Math.Max(0, limit - joined.Length));
                joined.Write(new byte[gap - (gap % BytesPerSample)]);
            }

            var room = limit - joined.Length;
            var take = (int)Math.Min(clipBytes, room - (room % BytesPerSample));
            if (take > 0) joined.Write(clip, 0, take);
            if (take < clipBytes) truncated = true;
        }

        var pcm = joined.ToArray();
        return new Joined(pcm, DurationMs(pcm.Length), truncated);
    }

    public static int DurationMs(int pcmBytes) => (int)(pcmBytes / (double)(SampleRate * BytesPerSample) * 1000);

    private static int BytesFor(int milliseconds)
    {
        var bytes = (int)(milliseconds / 1000.0 * SampleRate * BytesPerSample);
        return bytes - (bytes % BytesPerSample);
    }
}
