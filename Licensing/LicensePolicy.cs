// VBWR B
// Project: VoiceDucker | Creator: Umberto Giacobbi
// Modified with AI: OpenAI Codex; Store trial handling, 2026-10-12.
// SPDX-License-Identifier: MIT
// VBWR E
namespace VoiceDucker.Licensing;

internal enum LicenseState { Unrestricted, Checking, Full, Trial, Expired, Unavailable }

internal static class LicensePolicy
{
    internal static LicenseState Evaluate(bool active, bool trial, DateTimeOffset expires, DateTimeOffset now) =>
        !active ? LicenseState.Expired : !trial ? LicenseState.Full :
        expires > now ? LicenseState.Trial : LicenseState.Expired;

    internal static bool CanUse(LicenseState state, DateTimeOffset expires, DateTimeOffset now) =>
        state is LicenseState.Unrestricted or LicenseState.Full ||
        state == LicenseState.Trial && expires > now;
}
