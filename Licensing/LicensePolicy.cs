// VBWR B
// Project: VoiceDucker | Creator: Umberto Giacobbi
// Modified with AI: OpenAI Codex; Store trial handling, 2026-10-12.
// SPDX-License-Identifier: MIT
// VBWR E

namespace VoiceDucker.Licensing;

/// <summary>Describes the current Store entitlement or an unrestricted local distribution.</summary>
internal enum LicenseState
{
    Unrestricted,
    Checking,
    Full,
    Trial,
    Expired,
    Unavailable
}

/// <summary>Applies the shared access and display rules to Store license observations.</summary>
internal static class LicensePolicy
{
    #region Public methods

    /// <summary>Classifies a Store entitlement at the supplied point in time.</summary>
    internal static LicenseState Evaluate(bool active, bool trial, DateTimeOffset expires, DateTimeOffset now)
    {
        if (!active)
        {
            return LicenseState.Expired;
        }

        if (!trial)
        {
            return LicenseState.Full;
        }

        return expires > now ? LicenseState.Trial : LicenseState.Expired;
    }

    /// <summary>Allows unrestricted builds, purchased licenses, and trials that have not expired.</summary>
    internal static bool CanUse(LicenseState state, DateTimeOffset expires, DateTimeOffset now) =>
        state is LicenseState.Unrestricted or LicenseState.Full ||
        state == LicenseState.Trial && expires > now;

    /// <summary>Formats the application title for the current license state and remaining trial days.</summary>
    internal static string WindowTitle(string appName, LicenseState state, DateTimeOffset expires, DateTimeOffset now)
    {
        if (state == LicenseState.Expired)
        {
            return $"{appName} - Trial Expired";
        }

        if (state != LicenseState.Trial)
        {
            return appName;
        }

        var days = Math.Max(1, (int)Math.Ceiling((expires - now).TotalDays));

        return $"{appName} - Free Trial, {days} {(days == 1 ? "day" : "days")} remaining";
    }

    #endregion
}
