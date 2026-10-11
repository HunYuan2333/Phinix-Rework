using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace PhinixClient
{
    // Desktop integration stays outside theme parsing and persistence.
    internal static class LocalFileLocationAdapter
    {
        internal static bool TryOpenDirectory(string filePath)
        {
            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
                if (!Directory.Exists(directory)) return false;
                ProcessStartInfo start;
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer:
                    case RuntimePlatform.WindowsEditor:
                        start = new ProcessStartInfo(directory) { UseShellExecute = true };
                        break;
                    case RuntimePlatform.OSXPlayer:
                    case RuntimePlatform.OSXEditor:
                        start = DesktopCommand("/usr/bin/open", directory);
                        break;
                    case RuntimePlatform.LinuxPlayer:
                    case RuntimePlatform.LinuxEditor:
                        start = DesktopCommand("xdg-open", directory);
                        break;
                    default: return false;
                }
                // Starting the desktop handler is best effort; never wait on the game thread.
                using (var process = Process.Start(start)) return process != null;
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception
                || error is IOException || error is ArgumentException || error is InvalidOperationException
                || error is UnauthorizedAccessException || error is NotSupportedException
                || error is System.Security.SecurityException)
            { return false; }
        }

        private static ProcessStartInfo DesktopCommand(string command, string directory)
        {
            // Pass one quoted argument directly to the executable, without a shell.
            return new ProcessStartInfo(command, "\"" + directory.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
        }
    }
}
