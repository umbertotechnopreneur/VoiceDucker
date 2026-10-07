/* VBWR B
 * Project: VoiceDucker
 * Repository: https://github.com/umbertotechnopreneur/VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare initiative: Human intent. AI implementation. Accountable human review.
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 * Created with AI: OpenAI Codex assisted the initial implementation; Git history
 * Human guidance: Umberto Giacobbi defined the purpose and audio behavior
 * Evidence: Git history
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT
 * VBWR E */

using NAudio.Wave;

namespace VoiceDucker.Audio;

internal static class MicrophoneLevel
{
    private static readonly Guid PcmSubtype = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    public static bool IsSupported(WaveFormat format)
    {
        var encoding = ResolveEncoding(format);
        return (encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            || (encoding == WaveFormatEncoding.Pcm &&
                (format.BitsPerSample == 16 || format.BitsPerSample == 24 || format.BitsPerSample == 32));
    }

    public static double Rms(ReadOnlySpan<byte> audio, WaveFormat format)
    {
        var sampleBytes = format.BitsPerSample / 8;
        if (sampleBytes == 0 || audio.Length < sampleBytes)
        {
            return 0;
        }

        var encoding = ResolveEncoding(format);
        double sum = 0;
        var count = audio.Length / sampleBytes;
        for (var sample = 0; sample < count; sample++)
        {
            var offset = sample * sampleBytes;
            double value;
            if (encoding == WaveFormatEncoding.IeeeFloat)
            {
                value = BitConverter.ToSingle(audio.Slice(offset, 4));
            }
            else if (sampleBytes == 2)
            {
                value = BitConverter.ToInt16(audio.Slice(offset, 2)) / 32768.0;
            }
            else if (sampleBytes == 3)
            {
                var raw = audio[offset] | (audio[offset + 1] << 8) | (audio[offset + 2] << 16);
                if ((raw & 0x800000) != 0)
                {
                    raw |= unchecked((int)0xff000000);
                }
                value = raw / 8388608.0;
            }
            else
            {
                value = BitConverter.ToInt32(audio.Slice(offset, 4)) / 2147483648.0;
            }

            sum += value * value;
        }

        return Math.Sqrt(sum / count);
    }

    private static WaveFormatEncoding ResolveEncoding(WaveFormat format)
    {
        if (format is not WaveFormatExtensible extensible)
        {
            return format.Encoding;
        }

        if (extensible.SubFormat == FloatSubtype)
        {
            return WaveFormatEncoding.IeeeFloat;
        }

        return extensible.SubFormat == PcmSubtype ? WaveFormatEncoding.Pcm : WaveFormatEncoding.Unknown;
    }
}
