using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace Wynzio.Utilities
{
    internal class AutoStartManager
    {
        private const string RunRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryName = "Wynzio";

        /// <summary>
        /// Check if the application is configured to start automatically with Windows
        /// </summary>
        /// <returns>True if auto-start is enabled, false otherwise</returns>
        public bool IsAutoStartEnabled()
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryPath))
            {
                return key?.GetValue(AppRegistryName) != null;
            }
        }

        /// <summary>
        /// Enable automatic startup with Windows
        /// </summary>
        /// <returns>True if operation succeeded, false otherwise</returns>
        public bool EnableAutoStart()
        {
            try
            {
                // Get the path to the executable - fixed to handle .NET published applications
                string executablePath = GetExecutablePath();

                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, true))
                {
                    if (key != null)
                    {
                        key.SetValue(AppRegistryName, executablePath);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enable auto-start: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disable automatic startup with Windows
        /// </summary>
        /// <returns>True if operation succeeded, false otherwise</returns>
        public bool DisableAutoStart()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, true))
                {
                    if (key != null)
                    {
                        if (key.GetValue(AppRegistryName) != null)
                        {
                            key.DeleteValue(AppRegistryName);
                        }
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to disable auto-start: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Toggles the auto-start state
        /// </summary>
        /// <returns>The new state: true if enabled, false if disabled</returns>
        public bool ToggleAutoStart()
        {
            bool currentState = IsAutoStartEnabled();

            if (currentState)
            {
                DisableAutoStart();
                return false;
            }
            else
            {
                EnableAutoStart();
                return true;
            }
        }

        /// <summary>
        /// Get the correct path to the executable
        /// </summary>
        /// <returns>Full path to the executable</returns>
        private string GetExecutablePath()
        {
            try
            {
                // Try to get the entry assembly first (this works in normal execution)
                var entryAssembly = Assembly.GetEntryAssembly();
                if (entryAssembly != null)
                {
                    var entryPath = entryAssembly.Location;

                    // Check if this is a published .NET app (.dll vs .exe check)
                    if (entryPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        // For published .NET apps, use Process.GetCurrentProcess().MainModule instead
                        var mainModulePath = Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(mainModulePath))
                        {
                            return mainModulePath;
                        }
                    }
                    else if (!string.IsNullOrEmpty(entryPath))
                    {
                        return entryPath;
                    }
                }

                // Fallback to executing assembly if entry assembly is not available
                var executingAssembly = Assembly.GetExecutingAssembly();
                var assemblyPath = executingAssembly.Location;

                // If the path ends with .dll, try to find the corresponding .exe
                if (assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    var directoryPath = Path.GetDirectoryName(assemblyPath);
                    var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
                    var exePath = Path.Combine(directoryPath ?? "", $"{assemblyName}.exe");

                    if (File.Exists(exePath))
                    {
                        return exePath;
                    }

                    // Try to find any .exe file in the directory
                    var exeFiles = Directory.GetFiles(directoryPath ?? "", "*.exe");
                    if (exeFiles.Length > 0)
                    {
                        return exeFiles[0]; // Use the first .exe file found
                    }
                }

                // Last resort: use Process.GetCurrentProcess().MainModule
                var processPath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(processPath))
                {
                    return processPath;
                }

                // If all else fails, return the original path
                return assemblyPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting executable path: {ex.Message}");

                // Fallback to process executable path
                try
                {
                    return Process.GetCurrentProcess().MainModule?.FileName ??
                           Assembly.GetExecutingAssembly().Location;
                }
                catch
                {
                    // If everything fails, return a default path that points to the application folder
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "Wynzio\\Wynzio.exe");
                }
            }
        }
    }
}