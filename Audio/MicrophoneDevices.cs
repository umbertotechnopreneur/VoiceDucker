/*
 * VoiceDucker - VibeWare
 * Copyright (c) 2026 Umberto Giacobbi
 * SPDX-License-Identifier: MIT
 */

using NAudio.CoreAudioApi;

namespace VoiceDucker.Audio;

/// <summary>A microphone selection value; an empty ID follows the Windows communications default.</summary>
internal sealed record MicrophoneChoice(string Id, string Name);

/// <summary>Lists and resolves capture endpoints without starting microphone capture.</summary>
internal static class MicrophoneDevices
{
    #region Public methods

    /// <summary>Lists active capture endpoints, preceded by the Windows default choice.</summary>
    public static IReadOnlyList<MicrophoneChoice> GetChoices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var choices = new List<MicrophoneChoice>();
        string defaultName;

        try
        {
            using var microphone = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            defaultName = $"Windows default — {microphone.FriendlyName}";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            defaultName = "Windows default — no microphone available";
        }

        choices.Add(new MicrophoneChoice(string.Empty, defaultName));

        foreach (var microphone in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (microphone)
            {
                choices.Add(new MicrophoneChoice(microphone.ID, microphone.FriendlyName));
            }
        }

        return choices;
    }

    /// <summary>
    /// Opens the selected capture endpoint or communications default. The caller owns
    /// the returned device; unavailable explicit selections fail without changing preferences.
    /// </summary>
    public static MMDevice OpenSelected(MMDeviceEnumerator enumerator, string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }

        MMDevice microphone;
        try
        {
            microphone = enumerator.GetDevice(deviceId);
        }
        catch (System.Runtime.InteropServices.COMException exception)
        {
            throw new InvalidOperationException(
                "The selected microphone is unavailable. Choose another microphone or Windows default.", exception);
        }

        if (microphone.State != DeviceState.Active || microphone.DataFlow != DataFlow.Capture)
        {
            microphone.Dispose();
            throw new InvalidOperationException(
                "The selected microphone is disconnected or disabled. Choose another microphone or Windows default.");
        }

        return microphone;
    }

    #endregion
}
