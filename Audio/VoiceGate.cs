namespace VoiceDucker.Audio;

internal sealed class VoiceGate
{
    private const double Threshold = 0.018;
    private const long HoldMilliseconds = 650;
    private long _lastLoudTick = -1;

    public void Observe(double rms)
    {
        if (rms >= Threshold)
        {
            Volatile.Write(ref _lastLoudTick, Environment.TickCount64);
        }
    }

    public bool IsSpeaking
    {
        get
        {
            var last = Volatile.Read(ref _lastLoudTick);
            return last >= 0 && Environment.TickCount64 - last <= HoldMilliseconds;
        }
    }
}
