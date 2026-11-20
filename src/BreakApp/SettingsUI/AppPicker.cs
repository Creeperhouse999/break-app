using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BreakApp.Services;

namespace BreakApp.SettingsUI;

/// <summary>An app with at least one open window.</summary>
public sealed record RunningApp(string ProcessName, string Title, string? Path);

public static class RunningApps
{
    /// <summary>Shell / system processes that are never useful as exceptions.</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "TextInputHost", "ApplicationFrameHost", "ShellExperienceHost", "StartMenuExperienceHost",
        "SearchHost", "SearchApp", "LockApp", "SystemSettingsBroker", "dwm", "ctfmon", "BreakApp",
    };

    /// <summary>Apps with a visible, titled window (one entry per process name), sorted by name.</summary>
    public static IReadOnlyList<RunningApp> Scan()
    {
        var own = (uint)Environment.ProcessId;
        var byName = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var (title, pid) in Native.AppWindows())
        {
            if (pid == own || string.IsNullOrWhiteSpace(title) || title is "Default IME" or "MSCTFIME UI") continue;
            var path = Native.ProcessPath(pid);
            if (path is null) continue;
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (Ignored.Contains(name)) continue;
            if (!byName.TryGetValue(name, out var existing) || title.Length > existing.Title.Length)
                byName[name] = new RunningApp(name, title, path);
        }
        return byName.Values.OrderBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? IconFor(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        if (IconCache.TryGetValue(exePath, out var cached)) return cached;
        ImageSource? img = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon is not null)
            {
                img = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                img.Freeze();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception) { }
        return IconCache[exePath] = img;
    }
}

/// <summary>"Choose apps" dialog: open apps with icon and window title, search, click to add; or type a name.</summary>
public sealed class AppPickerWindow : Window
{
    private readonly List<RunningApp> _apps;
    private readonly HashSet<string> _already;
    private readonly TextBox _search = new();
    private readonly ListBox _list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly TextBox _manual = new() { Width = 220 };

    /// <summary>Process names picked in this dialog (in order).</summary>
    public List<string> Chosen { get; } = [];

    public AppPickerWindow(IEnumerable<RunningApp> apps, IEnumerable<string> alreadyAdded)
    {
        _apps = apps.ToList();
        _already = new HashSet<string>(alreadyAdded, StringComparer.OrdinalIgnoreCase);
        Title = "Choose apps";
        Width = 560;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = (FontFamily)Application.Current.FindResource("AppFont");
        SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        SourceInitialized += (_, _) =>
            Native.SetDwmInt(new WindowInteropHelper(this).Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, Theme.ThemeManager.IsDark ? 1 : 0);

        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        var head = new StackPanel();
        head.Children.Add(PageBuilder.T("Choose apps", "H1"));
        head.Children.Add(new TextBlock
        {
            Text = "Apps with an open window right now. Click one to add it. Not here? Open it first, or type its name below.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 4, 0, 12),
        }.WithRef(TextBlock.ForegroundProperty, "Text2Brush"));
        var searchHost = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        searchHost.Children.Add(_search);
        var hint = new TextBlock { Text = "Search apps", Margin = new Thickness(13, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, FontSize = 12.5 };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Text3Brush");
        searchHost.Children.Add(hint);
        _search.TextChanged += (_, _) => { hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Fill(); };
        head.Children.Add(searchHost);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var foot = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition());
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var manualRow = new StackPanel { Orientation = Orientation.Horizontal };
        manualRow.Children.Add(_manual);
        var addName = new Button { Content = "Add name", Margin = new Thickness(6, 0, 0, 0) };
        addName.Click += (_, _) => AddManual();
        _manual.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddManual(); };
        _manual.ToolTip = "Process name, e.g. obs64 (Task Manager → Details shows it)";
        manualRow.Children.Add(addName);
        foot.Children.Add(manualRow);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var refresh = new Button { Content = "Refresh", Margin = new Thickness(0, 0, 8, 0) };
        refresh.SetResourceReference(StyleProperty, "GhostButton");
        refresh.Click += (_, _) => { _apps.Clear(); _apps.AddRange(RunningApps.Scan()); Fill(); };
        var done = new Button { Content = "Done", IsDefault = true, Padding = new Thickness(20, 0, 20, 0) };
        done.SetResourceReference(StyleProperty, "PrimaryButton");
        done.Click += (_, _) => Close();
        buttons.Children.Add(refresh);
        buttons.Children.Add(done);
        Grid.SetColumn(buttons, 1);
        foot.Children.Add(buttons);
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);

        _list.ItemContainerStyle = (Style)Application.Current.FindResource("NavItem");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is ListBoxItem { Tag: RunningApp app }) Pick(app.ProcessName);
            _list.SelectedItem = null;
        };
        var card = new Border { Child = _list, Padding = new Thickness(6) };
        card.SetResourceReference(StyleProperty, "Card");
        root.Children.Add(card);
        Content = root;
        Fill();
        Loaded += (_, _) => _search.Focus();
    }

    private bool Added(string name) => _already.Contains(name) || Chosen.Contains(name, StringComparer.OrdinalIgnoreCase);

    private void Pick(string name)
    {
        if (Added(name)) return;
        Chosen.Add(name);
        Fill();
    }

    private void AddManual()
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(_manual.Text.Trim());
        if (name.Length == 0) return;
        Pick(name);
        _manual.Text = "";
    }

    private void Fill()
    {
        var q = _search.Text.Trim();
        _list.Items.Clear();
        foreach (var app in _apps.Where(a => q.Length == 0 || a.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Title.Contains(q, StringComparison.OrdinalIgnoreCase)))
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = RunningApps.IconFor(app.Path);
            grid.Children.Add(icon is null
                ? new TextBlock { Text = "", FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 18, VerticalAlignment = VerticalAlignment.Center }
                : new Image { Source = icon, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = app.ProcessName, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = app.Title, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }.WithRef(TextBlock.ForegroundProperty, "Text3Brush"));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var state = new TextBlock { Text = Added(app.ProcessName) ? "✓ added" : "+ add", FontSize = 12, Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            state.SetResourceReference(TextBlock.ForegroundProperty, Added(app.ProcessName) ? "SuccessBrush" : "AccentTextBrush");
            Grid.SetColumn(state, 2);
            grid.Children.Add(state);
            _list.Items.Add(new ListBoxItem { Content = grid, Tag = app, Cursor = Added(app.ProcessName) ? Cursors.Arrow : Cursors.Hand });
        }
        if (_list.Items.Count == 0)
            _list.Items.Add(new ListBoxItem { Content = new TextBlock { Text = q.Length > 0 ? "No open app matches. Type its name below." : "No apps with an open window." }, IsEnabled = false });
    }

    internal int TestVisibleCount => _list.Items.OfType<ListBoxItem>().Count(i => i.Tag is RunningApp);
    internal void TestFilter(string text) => _search.Text = text;
    internal void TestSelect(string processName) => Pick(processName);
}

/// <summary>A list of apps shown as removable chips, with "+ Add app…" opening the picker.</summary>
public sealed class AppListEditor : StackPanel
{
    private readonly Func<List<string>> _get;
    private readonly Action<List<string>> _set;
    private readonly WrapPanel _chips = new();

    public AppListEditor(Func<List<string>> get, Action<List<string>> set)
    {
        _get = get;
        _set = set;
        Children.Add(_chips);
        var add = new Button { Content = "+ Add app…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        add.Click += (_, _) =>
        {
            var picker = new AppPickerWindow(RunningApps.Scan(), _get()) { Owner = Window.GetWindow(this) };
            picker.ShowDialog();
            Add(picker.Chosen);
        };
        Children.Add(add);
        Build();
    }

    public void Add(IEnumerable<string> names)
    {
        var list = _get().ToList();
        var changed = false;
        foreach (var n in names.Select(n => n.Trim()).Where(n => n.Length > 0))
        {
            if (list.Contains(n, StringComparer.OrdinalIgnoreCase)) continue;
            list.Add(n);
            changed = true;
        }
        if (!changed) return;
        _set(list);
        Build();
    }

    public void Remove(string name)
    {
        var list = _get().Where(n => !n.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        _set(list);
        Build();
    }

    private void Build()
    {
        _chips.Children.Clear();
        var list = _get();
        if (list.Count == 0)
        {
            _chips.Children.Add(new TextBlock { Text = "No apps yet.", FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0) }.WithRef(TextBlock.ForegroundProperty, "Text3Brush"));
            return;
        }
        foreach (var name in list)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var path = RunningApps.Scan().FirstOrDefault(a => a.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Path;
            if (RunningApps.IconFor(path) is { } icon) row.Children.Add(new Image { Source = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) });
            row.Children.Add(new TextBlock { Text = name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center }.WithRef(TextBlock.ForegroundProperty, "Text1Brush"));
            var x = new Button { Content = "", Width = 20, Height = 20, FontSize = 9, Margin = new Thickness(6, 0, -4, 0), ToolTip = "Remove" };
            x.SetResourceReference(StyleProperty, "IconButton");
            var n = name;
            x.Click += (_, _) => Remove(n);
            row.Children.Add(x);
            var chip = new Border { Child = row, CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 4, 8, 4), Margin = new Thickness(0, 4, 6, 4) };
            chip.SetResourceReference(Border.BackgroundProperty, "SelectedBrush");
            _chips.Children.Add(chip);
        }
    }

    internal int TestChipCount => _chips.Children.OfType<Border>().Count();
}
