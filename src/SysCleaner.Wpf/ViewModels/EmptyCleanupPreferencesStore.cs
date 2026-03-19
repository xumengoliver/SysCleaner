using System.Text.Json;

namespace SysCleaner.Wpf.ViewModels;

internal sealed class EmptyCleanupPreferencesStore
{
    private readonly string _filePath;

    public EmptyCleanupPreferencesStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysCleaner", "ui-preferences.json"))
    {
    }

    internal EmptyCleanupPreferencesStore(string filePath)
    {
        _filePath = filePath;
    }

    public bool LoadIncludeSubfolders()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return true;
            }

            var json = File.ReadAllText(_filePath);
            var preferences = JsonSerializer.Deserialize<UiPreferences>(json);
            return preferences?.EmptyCleanupIncludeSubfolders ?? true;
        }
        catch
        {
            return true;
        }
    }

    public void SaveIncludeSubfolders(bool includeSubfolders)
    {
        try
        {
            var preferences = ReadPreferences() with { EmptyCleanupIncludeSubfolders = includeSubfolders };
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
        }
    }

    private UiPreferences ReadPreferences()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new UiPreferences();
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<UiPreferences>(json) ?? new UiPreferences();
        }
        catch
        {
            return new UiPreferences();
        }
    }

    private sealed record UiPreferences(bool EmptyCleanupIncludeSubfolders = true);
}