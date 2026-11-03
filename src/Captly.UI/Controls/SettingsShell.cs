using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace Captly.UI.Controls;

/// <summary>One sidebar entry and the page it opens.</summary>
public sealed class SettingsPage
{
    /// <summary>The heading above the page.</summary>
    public string Title { get; init; } = "";

    /// <summary>The sidebar label, when it differs from the heading.</summary>
    public string? SidebarLabel { get; init; }

    public string NavTitle => SidebarLabel ?? Title;
    public Geometry? Icon { get; init; }
    public Brush? TileBrush { get; init; }
    public object? Content { get; init; }

    /// <summary>Pinned pages sit at the bottom of the sidebar, as About does.</summary>
    public bool IsPinned { get; init; }

    /// <summary>Words search should also find, for rows built from data such as the shortcut list.</summary>
    public string? SearchTerms { get; init; }
}

/// <summary>
/// Sidebar with search and navigation, and a header with back and forward history. Search keeps
/// the pages that mention the words, and on the page shown, hides the rows that do not.
/// </summary>
[TemplatePart(Name = "PART_Back", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_Forward", Type = typeof(ButtonBase))]
public sealed class SettingsShell : Control
{
    public static readonly DependencyProperty SelectedPageProperty = DependencyProperty.Register(
        nameof(SelectedPage), typeof(SettingsPage), typeof(SettingsShell), new PropertyMetadata(null, (d, e) =>
            ((SettingsShell)d).OnPageChanged((SettingsPage?)e.OldValue)));

    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(
        nameof(SearchText), typeof(string), typeof(SettingsShell),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SettingsShell)d).ApplySearch()));

    private readonly List<UIElement> _hiddenBySearch = [];

    private readonly Stack<SettingsPage> _back = new();
    private readonly Stack<SettingsPage> _forward = new();
    private ButtonBase? _backButton;
    private ButtonBase? _forwardButton;
    private bool _navigatingHistory;
    private bool _restoring;

    static SettingsShell() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingsShell), new FrameworkPropertyMetadata(typeof(SettingsShell)));

    public ObservableCollection<SettingsPage> Pages { get; } = [];

    public SettingsPage? SelectedPage
    {
        get => (SettingsPage?)GetValue(SelectedPageProperty);
        set => SetValue(SelectedPageProperty, value);
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    /// <summary>Raised whenever another page shows, including by back and forward.</summary>
    public event Action<SettingsPage?>? SelectedPageChanged;

    /// <summary>True if the page's title, labels, headers or extra terms contain the words.</summary>
    public static bool Matches(SettingsPage page, string query) =>
        Contains(page.Title, query) || Contains(page.SidebarLabel, query) || Contains(page.SearchTerms, query)
        || Labels(page.Content as DependencyObject).Any(label => Contains(label, query));

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _backButton = GetTemplateChild("PART_Back") as ButtonBase;
        _forwardButton = GetTemplateChild("PART_Forward") as ButtonBase;
        if (_backButton != null) _backButton.Click += (_, _) => GoBack();
        if (_forwardButton != null) _forwardButton.Click += (_, _) => GoForward();
        UpdateButtons();
    }

    internal void GoBack() => Step(_back, _forward);

    internal void GoForward() => Step(_forward, _back);

    private void Step(Stack<SettingsPage> from, Stack<SettingsPage> to)
    {
        if (from.Count == 0 || SelectedPage == null) return;
        _navigatingHistory = true;
        to.Push(SelectedPage);
        SelectedPage = from.Pop();
        _navigatingHistory = false;
        UpdateButtons();
    }

    private void OnPageChanged(SettingsPage? previous)
    {
        // Filtering the sidebar can clear the list's selection. A page always stays shown.
        if (SelectedPage == null)
        {
            if (previous == null || _restoring) return;
            _restoring = true;
            SetCurrentValue(SelectedPageProperty, previous);
            _restoring = false;
            return;
        }
        SelectedPageChanged?.Invoke(SelectedPage);
        FilterRowsSoon();
        if (_navigatingHistory || previous == null) return;
        _back.Push(previous);
        _forward.Clear();
        UpdateButtons();
    }

    private void ApplySearch()
    {
        var query = SearchText.Trim();
        var shown = SelectedPage;
        var first = query.Length == 0 ? null : Pages.FirstOrDefault(p => Matches(p, query));
        if (shown != null && first != null && !Matches(shown, query)) SelectedPage = first;

        var view = CollectionViewSource.GetDefaultView(Pages);
        view.Filter = query.Length == 0 ? null : page => Matches((SettingsPage)page, query);
        FilterRowsSoon();
    }

    // Rows built from templates exist only once the page is laid out.
    private void FilterRowsSoon() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FilterRows);

    internal void FilterRows()
    {
        foreach (var element in _hiddenBySearch) element.Visibility = Visibility.Visible;
        _hiddenBySearch.Clear();

        var query = SearchText.Trim();
        if (query.Length == 0 || SelectedPage is not { Content: DependencyObject root } page || Contains(page.Title, query)) return;

        foreach (var card in Descendants(root).OfType<GroupCard>())
        {
            if (Contains(card.Header, query) || Contains(card.Description, query)) continue;
            var rows = Descendants(card).OfType<SettingRow>().ToList();
            var shown = 0;
            foreach (var row in rows)
            {
                if (Contains(row.Label, query) || Contains(row.Description, query))
                {
                    shown++;
                    continue;
                }
                Hide(Ancestor<GroupCardItem>(row) ?? (UIElement)row);
            }
            if (rows.Count > 0 && shown == 0) Hide(card);
        }
    }

    private void Hide(UIElement element)
    {
        if (element.Visibility != Visibility.Visible) return;
        element.Visibility = Visibility.Collapsed;
        _hiddenBySearch.Add(element);
    }

    private static bool Contains(string? text, string query) => text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;

    private static IEnumerable<string> Labels(DependencyObject? root)
    {
        if (root == null) yield break;
        var label = root switch
        {
            SettingRow row => row.Label + " " + row.Description,
            GroupCard card => card.Header,
            ChoiceCard choice => choice.Title + " " + choice.Description,
            ContentControl { Content: string text } => text,
            TextBlock text => text.Text,
            _ => null,
        };
        if (label != null) yield return label;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (var inner in Labels(child)) yield return inner;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        if (root is not Visual and not System.Windows.Media.Media3D.Visual3D) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var inner in Descendants(child)) yield return inner;
        }
    }

    private static T? Ancestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is T found) return found;
        }
        return null;
    }

    private void UpdateButtons()
    {
        if (_backButton != null) _backButton.IsEnabled = _back.Count > 0;
        if (_forwardButton != null) _forwardButton.IsEnabled = _forward.Count > 0;
    }
}
