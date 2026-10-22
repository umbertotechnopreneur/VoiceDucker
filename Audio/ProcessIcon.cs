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

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace VoiceDucker.Audio;

/// <summary>Reads a process executable's shell icon as PNG bytes without retaining native handles.</summary>
internal static class ProcessIcon
{
    #region Private fields

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ShellGetIcon = 0x100;

    #endregion

    #region Public methods

    /// <summary>Returns the executable's shell icon, or null when the process or icon is unavailable.</summary>
    public static byte[]? ReadPng(int processId)
    {
        var path = GetProcessPath(processId);
        if (path is null)
        {
            return null;
        }

        var info = new ShellFileInfo();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(),
                ShellGetIcon) == IntPtr.Zero || info.Icon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            using var icon = Icon.FromHandle(info.Icon);
            using var bitmap = icon.ToBitmap();
            using var output = new MemoryStream();
            bitmap.Save(output, ImageFormat.Png);

            return output.ToArray();
        }
        finally
        {
            _ = DestroyIcon(info.Icon);
        }
    }

    #endregion

    #region Implementation

    /// <summary>Resolves the executable path and releases the temporary process handle.</summary>
    private static string? GetProcessPath(int processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var path = new StringBuilder(32768);
            uint length = (uint)path.Capacity;

            return QueryFullProcessImageName(process, 0, path, ref length)
                ? path.ToString() : null;
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    #endregion

    #region Native declarations

    /// <summary>Shell icon metadata matching the Windows SHFILEINFO layout.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    /// <summary>Opens a limited-query process handle for executable path lookup.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    /// <summary>Copies the full executable path associated with an open process handle.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags,
        StringBuilder path, ref uint length);

    /// <summary>Releases the process handle obtained by OpenProcess.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>Requests the executable's icon and associated shell metadata.</summary>
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes,
        ref ShellFileInfo info, uint fileInfoSize, uint flags);

    /// <summary>Releases the shell-owned icon handle after its pixels have been copied.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    #endregion
}
