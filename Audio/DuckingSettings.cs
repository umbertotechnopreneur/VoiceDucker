namespace VoiceDucker.Audio;

internal sealed record DuckingSettings(
    int ReductionPercent = 50,
    int FadeDownMilliseconds = 1000,
    int FadeUpMilliseconds = 1000)
{
    public bool IsValid => ReductionPercent is >= 0 and <= 100 &&
                           FadeDownMilliseconds is >= 0 and <= 5000 &&
                           FadeUpMilliseconds is >= 0 and <= 5000;
}
