// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;

namespace FileTools
{
    public static class WindowsPaths
    {
        // Extended paths work on Windows 10 even when corporate policy disables LongPathsEnabled.
        // Preserve short paths for normal shell interoperability; keep any existing extended prefix.
        public static string ForIO(string path, bool force = false)
        {
            string full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || (!force && full.Length < 200)) return full;
            return full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full.Substring(2) : @"\\?\" + full;
        }
        public static string ForShell(string path)
        {
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path.Substring(8);
            return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path.Substring(4) : path;
        }
    }
}
