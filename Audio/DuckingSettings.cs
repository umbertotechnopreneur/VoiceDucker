namespace VoiceDucker.Audio;

internal sealed record DuckingSettings(
    int ReductionPercent = 90,
    int FadeDownMilliseconds = 300,
    int FadeUpMilliseconds = 30000,
    bool IncludeOtherSources = false)
{
    public bool IsValid => ReductionPercent is >= 0 and <= 100 &&
                           FadeDownMilliseconds is >= 0 and <= 5000 &&
                           FadeUpMilliseconds is >= 0 and <= 30000;
}
