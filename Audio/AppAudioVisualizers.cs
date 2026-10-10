/* VBWR B
 * Project: VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * Modified with AI: OpenAI Codex; per-process visualizer lifecycle, 2026-10-10.
 * Human guidance: Show or hide real app audio visualizers.
 * Copyright (c) 2026 Umberto Giacobbi
 * SPDX-License-Identifier: MIT
 * VBWR E */

namespace VoiceDucker.Audio;

internal sealed class AppAudioVisualizers : IDisposable
{
    private readonly Dictionary<int, ProcessAudioVisualizer> _captures = new();

    public void Synchronize(IEnumerable<int> processIds)
    {
        var wanted = processIds.ToHashSet();
        foreach (var processId in _captures.Keys.Where(id => !wanted.Contains(id)).ToArray())
        {
            _captures[processId].Dispose();
            _captures.Remove(processId);
        }
        foreach (var processId in wanted)
        {
            if (!_captures.ContainsKey(processId)) _captures.Add(processId, new ProcessAudioVisualizer(processId));
        }
    }

    public AudioVisualizationFrame GetFrame(int processId) =>
        _captures.TryGetValue(processId, out var capture) ? capture.Frame : AudioVisualizationFrame.Waiting;

    public void Dispose()
    {
        foreach (var capture in _captures.Values) capture.Dispose();
        _captures.Clear();
    }
}
