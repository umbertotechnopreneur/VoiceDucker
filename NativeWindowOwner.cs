/* VBWR B
 * Project: VoiceDucker
 * Repository: https://github.com/umbertotechnopreneur/VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 * Created with AI: See file history for implementation provenance.
 * Modified with AI: OpenAI Codex; source attribution and release preparation, 2026-10-09.
 * Human guidance: Umberto Giacobbi requested branding and the public 0.1.0 release.
 * Evidence: Git history and GitHub Actions; this header does not certify human review.
 * Copyright (c) 2026 Umberto Giacobbi
 * SPDX-License-Identifier: MIT
 * License: MIT - see LICENSE
 * VBWR E */

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VoiceDucker;

/// <summary>
/// Attaches secondary native windows to their application owner without changing activation policy.
/// </summary>
internal static class NativeWindowOwner
{
    #region Private fields

    private const int OwnerWindowIndex = -8;

    #endregion

    #region Public methods

    /// <summary>
    /// Sets the native owner and reports a Win32 failure without treating a zero previous owner as an error.
    /// </summary>
    public static void SetOwner(IntPtr windowHandle, IntPtr ownerHandle)
    {
        if (windowHandle == IntPtr.Zero || ownerHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Both window handles are required.");
        }

        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(windowHandle, OwnerWindowIndex, ownerHandle);
        var error = Marshal.GetLastPInvokeError();
        if (previous == IntPtr.Zero && error != 0)
        {
            throw new Win32Exception(error, "The close dialog could not be attached to the main window.");
        }
    }

    #endregion

    #region Native interop

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    /// <summary>
    /// Calls the native pointer-sized window-property API used to attach an owner.
    /// </summary>
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr value);

    #endregion
}
