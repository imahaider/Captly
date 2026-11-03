using System.ComponentModel;
using System.Windows.Controls;
using Captly.Core.Shell;
using Captly.UI.Controls;

namespace Captly.UI.Setup;

/// <summary>The first-run dialog. Closing it with ✕ counts as Skip Setup.</summary>
public partial class SetupWindow : CaptlyWindow
{
    private const double DotWidth = 6;
    private const double CurrentDotWidth = 18;

    private readonly SetupModel _model;
    private bool _finished;
    private bool _closing;

    public SetupWindow(SetupModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        model.Finished += _ =>
        {
            _finished = true;
            if (!_closing) Close();
        };
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SetupModel.StepIndex)) ShowDots();
        };
        ShowDots();
        Loaded += (_, _) => ContinueButton.Focus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        _closing = true;
        if (!_finished) _model.SkipCommand.Execute(null);
    }

    private void ShowDots()
    {
        Border[] dots = [Dot0, Dot1, Dot2];
        for (var i = 0; i < dots.Length; i++)
        {
            var current = i == _model.StepIndex;
            dots[i].Width = current ? CurrentDotWidth : DotWidth;
            if (current) dots[i].SetResourceReference(Border.BackgroundProperty, "Accent");
            else dots[i].SetResourceReference(Border.BackgroundProperty, "Toggle.Off");
        }
    }
}
