/* VBWR B
 * Project: VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Modified with AI: OpenAI Codex; Cassette 1984 audio visualizers, 2026-10-10.
 * Human guidance: Stereo analog VU meters and a real per-app spectrum.
 * Copyright (c) 2026 Umberto Giacobbi
 * SPDX-License-Identifier: MIT
 * VBWR E */

using System.Numerics;

namespace VoiceDucker.Audio;

internal sealed record AudioVisualizationFrame(double LeftRms, double RightRms,
    float[] Bands, long Timestamp, bool Ready = true, string? Error = null)
{
    public static AudioVisualizationFrame Waiting { get; } =
        new(0, 0, new float[AudioSpectrum.BandCount], 0, Ready: false);
}

// Stereo PCM is kept only in a short ring buffer; no audio is written to disk.
internal sealed class AudioSpectrum
{
    public const int SampleRate = 44100;
    public const int BandCount = 24;
    private const int FftSize = 2048;
    private readonly Complex[] _left = new Complex[FftSize];
    private readonly Complex[] _right = new Complex[FftSize];
    private readonly double[] _window = new double[FftSize];
    private readonly float[] _leftRing = new float[FftSize];
    private readonly float[] _rightRing = new float[FftSize];
    private readonly float[] _bands = new float[BandCount];
    private readonly double _windowSum;
    private int _index;
    private int _filled;
    private int _sinceFft;
    private double _leftEnergy;
    private double _rightEnergy;

    public AudioSpectrum()
    {
        for (var index = 0; index < FftSize; index++)
        {
            _window[index] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (FftSize - 1));
            _windowSum += _window[index];
        }
    }

    public AudioVisualizationFrame Process(ReadOnlySpan<short> stereoSamples)
    {
        if (stereoSamples.Length == 0 || stereoSamples.Length % 2 != 0)
        {
            throw new ArgumentException("Expected complete interleaved stereo PCM16 frames.", nameof(stereoSamples));
        }

        double leftSum = 0, rightSum = 0;
        for (var index = 0; index < stereoSamples.Length; index += 2)
        {
            var left = stereoSamples[index] / 32768f;
            var right = stereoSamples[index + 1] / 32768f;
            leftSum += left * left;
            rightSum += right * right;
            _leftRing[_index] = left;
            _rightRing[_index] = right;
            _index = (_index + 1) % FftSize;
            _filled = Math.Min(FftSize, _filled + 1);
            if (++_sinceFft >= FftSize && _filled == FftSize)
            {
                ComputeSpectrum();
                _sinceFft = 0;
            }
        }

        var frames = stereoSamples.Length / 2;
        // About 300 ms to settle; RMS responds independently in each channel.
        var blend = 1 - Math.Exp(-frames / (SampleRate * 0.1));
        _leftEnergy += blend * (leftSum / frames - _leftEnergy);
        _rightEnergy += blend * (rightSum / frames - _rightEnergy);
        return new AudioVisualizationFrame(Math.Sqrt(_leftEnergy), Math.Sqrt(_rightEnergy),
            (float[])_bands.Clone(), Environment.TickCount64);
    }

    public void Reset()
    {
        Array.Clear(_leftRing);
        Array.Clear(_rightRing);
        Array.Clear(_bands);
        _index = _filled = _sinceFft = 0;
        _leftEnergy = _rightEnergy = 0;
    }

    private void ComputeSpectrum()
    {
        for (var index = 0; index < FftSize; index++)
        {
            var ringIndex = (_index + index) % FftSize;
            _left[index] = new Complex(_leftRing[ringIndex] * _window[index], 0);
            _right[index] = new Complex(_rightRing[ringIndex] * _window[index], 0);
        }
        Transform(_left);
        Transform(_right);
        var scale = 2 / _windowSum;
        for (var band = 0; band < BandCount; band++)
        {
            var lower = 40 * Math.Pow(16000.0 / 40, (double)band / BandCount);
            var upper = 40 * Math.Pow(16000.0 / 40, (double)(band + 1) / BandCount);
            var first = Math.Max(1, (int)Math.Ceiling(lower * FftSize / SampleRate));
            var last = Math.Min(FftSize / 2 - 1, (int)Math.Ceiling(upper * FftSize / SampleRate) - 1);
            if (last < first)
            {
                first = last = Math.Max(1, (int)Math.Round(Math.Sqrt(lower * upper) * FftSize / SampleRate));
            }
            double power = 0;
            for (var bin = first; bin <= last; bin++)
            {
                // Combine channel powers, never waveforms: antiphase stereo stays visible.
                var left = _left[bin].Magnitude;
                var right = _right[bin].Magnitude;
                power = Math.Max(power, (left * left + right * right) / 2);
            }
            var decibels = 20 * Math.Log10(Math.Max(1e-9, Math.Sqrt(power) * scale));
            _bands[band] = (float)Math.Clamp((decibels + 60) / 60, 0, 1);
        }
    }

    private static void Transform(Complex[] samples)
    {
        for (int index = 1, reversed = 0; index < samples.Length; index++)
        {
            var bit = samples.Length >> 1;
            for (; (reversed & bit) != 0; bit >>= 1) reversed ^= bit;
            reversed ^= bit;
            if (index < reversed) (samples[index], samples[reversed]) = (samples[reversed], samples[index]);
        }
        for (var length = 2; length <= samples.Length; length <<= 1)
        {
            var step = Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
            for (var start = 0; start < samples.Length; start += length)
            {
                var factor = Complex.One;
                for (var offset = 0; offset < length / 2; offset++)
                {
                    var even = samples[start + offset];
                    var odd = samples[start + offset + length / 2] * factor;
                    samples[start + offset] = even + odd;
                    samples[start + offset + length / 2] = even - odd;
                    factor *= step;
                }
            }
        }
    }
}
