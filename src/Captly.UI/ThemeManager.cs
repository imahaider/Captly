using System.ComponentModel;
using System.Windows;
using Captly.Core.Settings;
using Microsoft.Win32;

namespace Captly.UI;

public enum EffectiveTheme { Light, Dark, HighContrast }

/// <summary>Owns the app's token dictionary and swaps it live, with no restart.</summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static Application? _app;
    private static ResourceDictionary? _tokens;
    private static ThemeChoice _choice;

    public static EffectiveTheme Current { get; private set; }

    public static event Action? ThemeChanged;

    /// <summary>Call once at start-up.</summary>
    public static void Install(Application app, ThemeChoice choice)
    {
        _app = app;
        _choice = choice;
        app.Resources.MergedDictionaries.Add(Load("Base"));
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Accessibility or UserPreferenceCategory.Color) Refresh();
        };
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        Refresh();
    }

    public static void Apply(ThemeChoice choice)
    {
        _choice = choice;
        Refresh();
    }

    /// <summary>High contrast always wins, because it is an accessibility need.</summary>
    public static EffectiveTheme Resolve(ThemeChoice choice, bool systemUsesLightTheme, bool highContrast)
    {
        if (highContrast) return EffectiveTheme.HighContrast;
        return choice switch
        {
            ThemeChoice.Light => EffectiveTheme.Light,
            ThemeChoice.Dark => EffectiveTheme.Dark,
            _ => systemUsesLightTheme ? EffectiveTheme.Light : EffectiveTheme.Dark,
        };
    }

    public static ResourceDictionary LoadTokens(EffectiveTheme theme) => Load(theme.ToString());

    private static ResourceDictionary Load(string name) =>
        new() { Source = new Uri($"pack://application:,,,/Captly.UI;component/Themes/{name}.xaml") };

    private static void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) Refresh();
    }

    private static void Refresh()
    {
        if (_app == null) return;
        var systemLight = Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1) is not 0;
        var theme = Resolve(_choice, systemLight, SystemParameters.HighContrast);
        if (_tokens != null && theme == Current) return;

        var next = LoadTokens(theme);
        var dictionaries = _app.Resources.MergedDictionaries;
        if (_tokens != null) dictionaries.Remove(_tokens);
        dictionaries.Add(next);
        _tokens = next;
        Current = theme;
        ThemeChanged?.Invoke();
    }
}

/// <summary>Durations that respect the Windows "Animation effects" setting.</summary>
public static class Motion
{
    public static Duration Fast => For(TimeSpan.FromMilliseconds(150));

    private static Duration For(TimeSpan time) => SystemParameters.ClientAreaAnimation ? time : TimeSpan.Zero;
}
