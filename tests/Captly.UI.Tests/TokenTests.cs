using System.Windows;
using System.Windows.Media;
using Captly.Core.Settings;

namespace Captly.UI.Tests;

public class TokenTests
{
    private static Dictionary<string, Color> Tokens(EffectiveTheme theme) => Ui.Run(() =>
        ThemeManager.LoadTokens(theme).Keys.Cast<string>().ToDictionary(k => k, k => ((SolidColorBrush)ThemeManager.LoadTokens(theme)[k]).Color));

    // PLAN.md §6.1
    [Theory]
    [InlineData("Bg.Window", "#F5F5F7", "#1E1E1E")]
    [InlineData("Bg.Sidebar", "#E8E8ED", "#2A2A2C")]
    [InlineData("Bg.Card", "#FFFFFF", "#2C2C2E")]
    [InlineData("Bg.Subtle", "#EFEFF4", "#3A3A3C")]
    [InlineData("Border.Hairline", "#E5E5EA", "#38383A")]
    [InlineData("Border.Control", "#D2D2D7", "#48484A")]
    [InlineData("Text.Primary", "#1D1D1F", "#F5F5F7")]
    [InlineData("Text.Secondary", "#6E6E73", "#A1A1A6")]
    [InlineData("Text.Tertiary", "#AEAEB2", "#636366")]
    [InlineData("Accent", "#0066CC", "#0073E6")] // Dark departs from §6.1 for contrast, see DECISIONS.md D21.
    [InlineData("Accent.Text", "#0066CC", "#64B5FF")]
    [InlineData("Success", "#34C759", "#30D158")]
    [InlineData("Success.Text", "#0B6E2E", "#7EE2A0")]
    [InlineData("Success.TextBg", "#DDF5E3", "#1F3A28")]
    [InlineData("Info.Badge", "#0B4F9E", "#9CC9FF")]
    [InlineData("Info.BadgeBg", "#E1EEFC", "#1C3350")]
    [InlineData("Close.Hover", "#C42B1C", "#C42B1C")]
    public void Colors_MatchThePlan(string key, string light, string dark)
    {
        Assert.Equal(Parse(light), Tokens(EffectiveTheme.Light)[key]);
        Assert.Equal(Parse(dark), Tokens(EffectiveTheme.Dark)[key]);
    }

    [Fact]
    public void EveryTheme_DefinesTheSameKeys()
    {
        var light = Tokens(EffectiveTheme.Light).Keys.Order();

        Assert.Equal(light, Tokens(EffectiveTheme.Dark).Keys.Order());
        Assert.Equal(light, Tokens(EffectiveTheme.HighContrast).Keys.Order());
    }

    [Theory]
    [InlineData("Text.Primary", "Bg.Card")]
    [InlineData("Text.Primary", "Bg.Window")]
    [InlineData("Text.Primary", "Bg.Sidebar")]
    [InlineData("Text.Primary", "Bg.Subtle")]
    [InlineData("Text.Secondary", "Bg.Card")]
    [InlineData("Accent.Text", "Bg.Card")]
    [InlineData("Accent.Text", "Bg.Subtle")]
    [InlineData("Success.Text", "Success.TextBg")]
    [InlineData("Info.Badge", "Info.BadgeBg")]
    [InlineData("OnAccent", "Accent")]
    [InlineData("OnAccent", "Accent.Hover")]
    public void Text_MeetsTheContrastTarget(string text, string background)
    {
        foreach (var theme in new[] { EffectiveTheme.Light, EffectiveTheme.Dark })
        {
            var tokens = Tokens(theme);
            Assert.True(Contrast(tokens[text], tokens[background]) >= 4.5, $"{text} on {background} in {theme}: {Contrast(tokens[text], tokens[background]):F2}");
        }
    }

    // Toggles and selection are not text, so the target for them is 3:1 against their background.
    [Theory]
    [InlineData(EffectiveTheme.Light)]
    [InlineData(EffectiveTheme.Dark)]
    public void Accent_StandsOutFromCards(EffectiveTheme theme)
    {
        var tokens = Tokens(theme);
        Assert.True(Contrast(tokens["Accent"], tokens["Bg.Card"]) >= 3, $"{Contrast(tokens["Accent"], tokens["Bg.Card"]):F2}");
    }

    [Theory]
    [InlineData(ThemeChoice.Light, true, false, EffectiveTheme.Light)]
    [InlineData(ThemeChoice.Light, false, false, EffectiveTheme.Light)]
    [InlineData(ThemeChoice.Dark, true, false, EffectiveTheme.Dark)]
    [InlineData(ThemeChoice.System, true, false, EffectiveTheme.Light)]
    [InlineData(ThemeChoice.System, false, false, EffectiveTheme.Dark)]
    [InlineData(ThemeChoice.Light, true, true, EffectiveTheme.HighContrast)]
    [InlineData(ThemeChoice.Dark, false, true, EffectiveTheme.HighContrast)]
    public void Resolve_FollowsTheChoice_AndHighContrastWins(ThemeChoice choice, bool systemLight, bool highContrast, EffectiveTheme expected) =>
        Assert.Equal(expected, ThemeManager.Resolve(choice, systemLight, highContrast));

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static double Contrast(Color a, Color b)
    {
        var (light, dark) = (Luminance(a), Luminance(b));
        if (light < dark) (light, dark) = (dark, light);
        return (light + 0.05) / (dark + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
