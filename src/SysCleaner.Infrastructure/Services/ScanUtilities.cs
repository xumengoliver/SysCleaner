using Microsoft.Win32;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace SysCleaner.Infrastructure.Services;

internal static class ScanUtilities
{
    private static readonly Regex ExecutableRegex = new("""(?<path>"[^"]+"|(?:[A-Za-z]:|%[^%]+%|\\SystemRoot)\\[^,]+?)(\s|$)""", RegexOptions.Compiled);

    public static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    public static string ExtractExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var match = ExecutableRegex.Match(command);
        if (!match.Success)
        {
            return string.Empty;
        }

        return ExpandPath(match.Groups["path"].Value.Trim('"').TrimEnd(','));
    }

    public static bool PathExists(string? path)
    {
        var expandedPath = ExpandPath(path);
        if (string.IsNullOrWhiteSpace(expandedPath))
        {
            return false;
        }

        return File.Exists(expandedPath) || Directory.Exists(expandedPath);
    }

    public static string ExpandPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = Normalize(path).Trim('"');
        if (normalized.StartsWith("\\SystemRoot\\", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = normalized["\\SystemRoot\\".Length..];
            return Normalize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), suffix));
        }

        return Normalize(Environment.ExpandEnvironmentVariables(normalized));
    }

    public static bool IsPathUnderWindowsDirectory(string? path)
    {
        var expandedPath = ExpandPath(path);
        if (string.IsNullOrWhiteSpace(expandedPath))
        {
            return false;
        }

        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return expandedPath.StartsWith(windowsDirectory, StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<(RegistryKey Root, string Path)> GetUninstallRoots()
    {
        yield return (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        yield return (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
        yield return (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
    }

    public static string SafeGetString(RegistryKey key, string name) => Normalize(key.GetValue(name)?.ToString());

    public static bool IsProtectedPublisher(string publisher, string displayName)
    {
        return publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Windows", StringComparison.OrdinalIgnoreCase);
    }

    public static RiskLevel ToRisk(ItemHealth health) => health switch
    {
        ItemHealth.Healthy => RiskLevel.Safe,
        ItemHealth.Review => RiskLevel.Review,
        ItemHealth.Broken => RiskLevel.Review,
        ItemHealth.Protected => RiskLevel.Protected,
        _ => RiskLevel.Review
    };

    public static string TryReadShortcutTarget(string path)
    {
        if (!File.Exists(path) || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        try
        {
            var shell = Type.GetTypeFromProgID("WScript.Shell");
            if (shell is null)
            {
                return string.Empty;
            }

            dynamic instance = Activator.CreateInstance(shell)!;
            dynamic shortcut = instance.CreateShortcut(path);
            string target = shortcut.TargetPath;
            return Normalize(target);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static IEnumerable<string> EnumerateSafeDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch
        {
            return [];
        }
    }

    public static IEnumerable<string> EnumerateSafeFiles(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root);
        }
        catch
        {
            return [];
        }
    }

    public static string DescribeException(Exception ex) => ex is Win32Exception win32
        ? $"{ex.Message} (0x{win32.NativeErrorCode:X})"
        : ex.Message;

    public static string ToAppId(string registryPath) => Convert.ToHexString(Encoding.UTF8.GetBytes(registryPath)).ToLowerInvariant();
}