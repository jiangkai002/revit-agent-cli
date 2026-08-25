using System.IO;
using System.Text.Json;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace RevitAgent.Gui.Services;

public enum AppThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>Owns the visual theme independently from the CLI configuration.</summary>
public static class ThemeService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "revit-agent", "gui.json");

    public static AppThemePreference Preference { get; private set; } = Load();

    public static void Apply(Window? window = null)
    {
        if (window is not null)
            SystemThemeWatcher.UnWatch(window);

        if (Preference == AppThemePreference.System && window is not null)
        {
            SystemThemeWatcher.Watch(window, WindowBackdropType.None, updateAccents: true);
            return;
        }

        ApplicationTheme theme = Preference switch
        {
            AppThemePreference.Light => ApplicationTheme.Light,
            AppThemePreference.Dark => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.IsMatchedDark()
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light,
        };
        ApplicationThemeManager.Apply(theme, WindowBackdropType.None, updateAccent: true);
    }

    public static void SetPreference(AppThemePreference preference, Window? window)
    {
        Preference = preference;
        Save(preference);
        Apply(window);
    }

    private static AppThemePreference Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return AppThemePreference.System;
            var settings = JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(SettingsPath));
            return Enum.TryParse<AppThemePreference>(settings?.Theme, true, out var value)
                ? value
                : AppThemePreference.System;
        }
        catch
        {
            return AppThemePreference.System;
        }
    }

    private static void Save(AppThemePreference preference)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
                new GuiSettings { Theme = preference.ToString() },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Theme changes remain effective for this run even if preferences cannot be saved.
        }
    }

    private sealed class GuiSettings
    {
        public string Theme { get; set; } = nameof(AppThemePreference.System);
    }
}
