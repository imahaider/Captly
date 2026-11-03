using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Captly.Core.Settings;
using Captly.Core.Shell;
using Captly.Core.Speech;
using Captly.UI.Setup;

namespace Captly.UI.Tests;

public class SetupWindowTests
{
    private static (SetupWindow Window, SetupModel Model, List<bool> Finished) Open()
    {
        var settings = new SettingsService(new AppSettings(), _ => { }, TimeSpan.Zero);
        var model = new SetupModel(settings, new NoModels(), (m, _) => Task.FromResult(new BenchResult(m.Id, m.MillionParameters, 0.3)));
        var finished = new List<bool>();
        model.Finished += finished.Add;
        var window = new SetupWindow(model) { Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false };
        window.Show();
        Ui.Settle();
        return (window, model, finished);
    }

    private static List<string> ShownTexts(DependencyObject root)
    {
        var texts = new List<string>();
        void Walk(DependencyObject element)
        {
            if (element is UIElement { Visibility: not Visibility.Visible }) return;
            if (element is TextBlock { Text.Length: > 0 } text) texts.Add(text.Text);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Walk(VisualTreeHelper.GetChild(element, i));
        }
        Walk(root);
        return texts;
    }

    [Fact]
    public void Welcome_ShowsOnlyTheFirstStep() => Ui.Run(() =>
    {
        var (window, _, _) = Open();

        var texts = ShownTexts(window);
        window.Close();

        Assert.Contains("Welcome to Captly", texts);
        Assert.DoesNotContain("Get the speech model", texts);
        Assert.DoesNotContain("You're all set", texts);
        Assert.DoesNotContain(texts, t => t.Contains("graphics card", StringComparison.OrdinalIgnoreCase));
    });

    [Fact]
    public void Steps_ChangeWithTheModel() => Ui.Run(() =>
    {
        var (window, model, _) = Open();

        model.ContinueCommand.Execute(null);
        Ui.Settle();
        var texts = ShownTexts(window);

        Assert.Contains("Get the speech model", texts);
        Assert.DoesNotContain("Welcome to Captly", texts);
        window.Close();
    });

    [Fact]
    public void Closing_CountsAsSkip() => Ui.Run(() =>
    {
        var (window, _, finished) = Open();

        window.Close();

        Assert.Equal([false], finished);
    });

    [Fact]
    public void Skip_ClosesTheWindow_Once() => Ui.Run(() =>
    {
        var (window, model, finished) = Open();

        model.SkipCommand.Execute(null);
        Ui.Settle();

        Assert.False(window.IsVisible);
        Assert.Equal([false], finished);
    });
}
