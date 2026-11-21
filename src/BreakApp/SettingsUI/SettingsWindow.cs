using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BreakApp.Core.Models;
using BreakApp.Core.Settings;
using BreakApp.Services;
using BreakApp.Theme;

namespace BreakApp.SettingsUI;

/// <summary>
/// Settings: sidebar categories, search across every setting, Basic/Advanced switch, presets with a diff preview,
/// live theme preview, Save / Cancel. Edits a copy; nothing applies until Save.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly BreakCoordinator _c;
    private readonly SettingsContext _ctx;
    private readonly ListBox _nav = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(32, 8, 32, 8) };
    private readonly TextBlock _pageTitle = PageBuilder.T("", "H1");
    private readonly TextBox _search = new() { Width = 240 };
    private readonly ListBox _results = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
    private readonly CheckBox _advanced = new() { Content = "Advanced", VerticalAlignment = VerticalAlignment.Center };
    private bool _showAllOnce;
    private string _page = "Rules";
    private string? _sub;
    private bool _dirty, _saved;

    public SettingsWindow(BreakCoordinator c, string? page)
    {
        _c = c;
        _ctx = new SettingsContext(SettingsStore.Clone(c.Settings), c);
        _ctx.Changed += () => SetDirty(true);
        _ctx.AppearanceChanged += () => ThemeManager.Apply(_ctx.Edit.Appearance);
        _ctx.Navigate = (p, sub) => Show(p, sub);
        _ctx.Rebuild = () => Show(_page, _sub);

        Title = "Break App · Settings";
        Width = 1040;
        Height = 740;
        MinWidth = 820;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = (FontFamily)Application.Current.FindResource("AppFont");
        Icon = Theme.AppIcon.Large;
        SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "Text1Brush");
        Content = BuildLayout();
        SourceInitialized += (_, _) => ApplyTitleBar();
        ThemeManager.Changed += ApplyTitleBar;
        Closed += (_, _) => ThemeManager.Changed -= ApplyTitleBar;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { _search.Focus(); e.Handled = true; }
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { Save(); e.Handled = true; }
        };
        Show(page ?? "Rules", null);
    }

    private void ApplyTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) Native.SetDwmInt(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ThemeManager.IsDark ? 1 : 0);
    }

    // ------------------------------------------------------------------ layout

    private UIElement BuildLayout()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        // sidebar
        var side = new DockPanel { Margin = new Thickness(0) };
        var sideBg = new Border { Child = side, Padding = new Thickness(14, 18, 14, 14) };
        sideBg.SetResourceReference(Border.BackgroundProperty, "SidebarBrush");
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 18) };
        // Same icon as the window / taskbar / tray.
        brand.Children.Add(new System.Windows.Controls.Image { Width = 28, Height = 28, Source = Theme.AppIcon.Large });
        brand.Children.Add(new TextBlock { Text = "Break App", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }
            .WithRef(TextBlock.ForegroundProperty, "Text1Brush"));
        DockPanel.SetDock(brand, Dock.Top);
        side.Children.Add(brand);

        _nav.ItemContainerStyle = (Style)Application.Current.FindResource("NavItem");
        foreach (var (name, icon) in SettingsPages.Categories)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Tag = name };
            item.Children.Add(new TextBlock { Text = icon, FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 15, Width = 22, VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(new TextBlock { Text = name, FontSize = 13.5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            // Wrap in a ListBoxItem: a raw panel inherits the ListBox's (black) text colour through its logical parent.
            _nav.Items.Add(new ListBoxItem { Content = item, Tag = name });
        }
        _nav.SelectionChanged += (_, _) =>
        {
            if (_nav.SelectedItem is ListBoxItem { Tag: string name } && name != _page) Show(name, null);
        };
        side.Children.Add(_nav);
        root.Children.Add(sideBg);

        // main column
        var main = new DockPanel();
        Grid.SetColumn(main, 1);

        var header = new Grid { Margin = new Thickness(32, 20, 32, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(_pageTitle);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var searchHost = new Grid();
        searchHost.Children.Add(_search);
        var placeholder = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        placeholder.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }
            .WithRef(TextBlock.ForegroundProperty, "Text3Brush"));
        placeholder.Children.Add(new TextBlock { Text = "Search settings (Ctrl+F)", FontSize = 12.5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }
            .WithRef(TextBlock.ForegroundProperty, "Text3Brush"));
        searchHost.Children.Add(placeholder);
        _search.TextChanged += (_, _) => { placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RunSearch(); };
        tools.Children.Add(searchHost);
        _advanced.SetResourceReference(StyleProperty, "Switch");
        _advanced.Margin = new Thickness(16, 0, 0, 0);
        _advanced.ToolTip = "Show every setting. Basic hides the rarely used ones.";
        _advanced.IsChecked = _ctx.Edit.General.AdvancedMode;
        _advanced.Click += (_, _) => { _ctx.Edit.General.AdvancedMode = _advanced.IsChecked == true; SetDirty(true); Show(_page, _sub); };
        tools.Children.Add(_advanced);
        var presets = _presetsButton = new Button { Content = "Presets ▾", Margin = new Thickness(14, 0, 0, 0) };
        presets.Click += (_, _) => ShowPresets(presets);
        tools.Children.Add(presets);
        Grid.SetColumn(tools, 1);
        header.Children.Add(tools);
        DockPanel.SetDock(header, Dock.Top);
        main.Children.Add(header);

        // footer
        var footer = new Grid { Margin = new Thickness(32, 10, 32, 16) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Text3Brush");
        footer.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        // Changes save automatically; Close just closes (and flushes the last change).
        var close = new Button { Content = "Close", Height = 34, Padding = new Thickness(22, 0, 22, 0) };
        close.SetResourceReference(StyleProperty, "PrimaryButton");
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        var footerBorder = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0) };
        footerBorder.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");
        DockPanel.SetDock(footerBorder, Dock.Bottom);
        main.Children.Add(footerBorder);

        var content = new Grid();
        content.Children.Add(_scroll);
        _results.ItemContainerStyle = (Style)Application.Current.FindResource("NavItem");
        _results.Margin = new Thickness(32, 8, 32, 8);
        _results.SelectionChanged += (_, _) =>
        {
            if (_results.SelectedItem is ListBoxItem { Tag: RowInfo row }) GoTo(row);
        };
        content.Children.Add(_results);
        main.Children.Add(content);

        root.Children.Add(main);
        SetDirty(false);
        return root;
    }

    internal SettingsContext TestContext => _ctx;
    internal StackPanel? TestPanel => _scroll.Content as StackPanel;
    internal void TestNavigate(string page, string? sub = null) => Show(page, sub);
    internal void TestSearch(string text) => _search.Text = text;
    internal int TestResultCount => _results.Items.Count;
    internal void TestSave() => Save();

    /// <summary>Open a rule's editor directly.</summary>
    public void ShowRule(Guid id) => Show("Rules", id.ToString());

    // ------------------------------------------------------------------ navigation & search

    private void Show(string page, string? sub)
    {
        _ctx.ShowAll = _showAllOnce;
        _showAllOnce = false;
        _page = page;
        _sub = sub;
        _ctx.Rows.Clear();
        var panel = SettingsPages.Build(page, _ctx, sub);
        _scroll.Content = panel;
        _scroll.ScrollToTop();
        var rule = sub is null ? null : _ctx.Edit.Rules.FirstOrDefault(r => r.Id.ToString() == sub);
        _pageTitle.Text = rule is null ? page : $"Rule · {rule.Name}";
        _advanced.IsChecked = _ctx.Edit.General.AdvancedMode;
        foreach (ListBoxItem item in _nav.Items)
            if ((string)item.Tag == page && !Equals(_nav.SelectedItem, item)) _nav.SelectedItem = item;
        if (_search.Text.Length > 0) { _search.Text = ""; }
    }

    /// <summary>Builds every page off-screen to index all rows (rule rows are indexed via the first rule).</summary>
    private List<RowInfo> IndexAll()
    {
        var ctx = new SettingsContext(_ctx.Edit, _c);
        var advanced = _ctx.Edit.General.AdvancedMode; // read BEFORE flipping it (same object)
        ctx.Edit.General.AdvancedMode = true;
        try
        {
            foreach (var (name, _) in SettingsPages.Categories)
                if (name != "Stats") SettingsPages.Build(name, ctx, null);
            if (_ctx.Edit.Rules.FirstOrDefault() is { } first) SettingsPages.Build("Rules", ctx, first.Id.ToString());
        }
        finally { _ctx.Edit.General.AdvancedMode = advanced; }
        return ctx.Rows;
    }

    private string? _titleBeforeSearch;

    private void RunSearch()
    {
        var q = _search.Text.Trim();
        if (q.Length > 0 && _results.Visibility != Visibility.Visible) _titleBeforeSearch = _pageTitle.Text;
        if (q.Length == 0)
        {
            _results.Visibility = Visibility.Collapsed;
            _scroll.Visibility = Visibility.Visible;
            _pageTitle.Text = _titleBeforeSearch ?? _pageTitle.Text;
            return;
        }
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = IndexAll()
            .Where(r => words.All(w => $"{r.Category} {r.Group} {r.Title} {r.Description} {r.Tip}".Contains(w, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(r => (r.Category, r.Group, r.Title))
            .Take(40).ToList();
        _results.Items.Clear();
        foreach (var h in hits)
        {
            var sp = new StackPanel { Tag = h };
            sp.Children.Add(new TextBlock { Text = h.Title, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
            sp.Children.Add(new TextBlock { Text = $"{h.Category} › {h.Group}{(string.IsNullOrEmpty(h.Description) ? "" : " · " + h.Description)}", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }
                .WithRef(TextBlock.ForegroundProperty, "Text3Brush"));
            _results.Items.Add(new ListBoxItem { Content = sp, Tag = h });
        }
        if (hits.Count == 0) _results.Items.Add(new ListBoxItem { Content = new TextBlock { Text = "Nothing found." }, IsEnabled = false });
        _results.Visibility = Visibility.Visible;
        _scroll.Visibility = Visibility.Collapsed;
        _pageTitle.Text = "Search";
    }

    private void GoTo(RowInfo hit)
    {
        _showAllOnce = true; // reveal advanced rows for this view only; the user's setting is untouched
        _search.Text = "";
        Show(hit.Category, hit.SubPage);
        var row = _ctx.Rows.FirstOrDefault(r => r.Group == hit.Group && r.Title == hit.Title);
        if (row is null) return;
        Dispatcher.BeginInvoke(() =>
        {
            row.Element.BringIntoView();
            var flash = new SolidColorBrush(ThemeManager.Palette.A2) { Opacity = 0 };
            if (row.Element is Panel p)
            {
                p.Background = flash;
                flash.BeginAnimation(Brush.OpacityProperty, new DoubleAnimation(0, 0.25, TimeSpan.FromSeconds(0.35)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) });
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ------------------------------------------------------------------ presets

    private Button? _presetsButton;
    private ContextMenu? _openMenu;
    internal void TestOpenPresets() { ShowPresets(_presetsButton!); if (_openMenu is not null) _openMenu.StaysOpen = true; }
    internal void TestCloseMenus() { if (_openMenu is not null) _openMenu.IsOpen = false; }

    private void ShowPresets(Button anchor)
    {
        // Drop down under the button, right edges aligned (the button sits at the window's right edge).
        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Custom,
            CustomPopupPlacementCallback = (popup, target, _) =>
                [new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(target.Width - popup.Width + 10, target.Height + 4), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)],
        };
        _openMenu = menu;
        foreach (var p in Presets.All)
        {
            var item = new MenuItem { Header = new StackPanel { Children =
            {
                new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold },
                new TextBlock { Text = p.Description, FontSize = 11.5, MaxWidth = 340, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 },
            } } };
            item.Click += (_, _) =>
            {
                var after = p.Apply(_ctx.Edit);
                var diff = Presets.Diff(_ctx.Edit, after);
                if (diff.Count == 0) { ThemedDialog.Info(p.Name, "Your settings already match this preset."); return; }
                var text = $"Apply the {p.Name} preset? These will change:\n\n• " + string.Join("\n• ", diff);
                if (!ThemedDialog.Confirm($"Apply {p.Name}?", text, "Apply", "Cancel")) return;
                _ctx.Edit = after;
                SetDirty(true);
                Show(_page, null);
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    // ------------------------------------------------------------------ save / close

    private DispatcherTimer? _autosave;
    private string? _pendingNote;

    /// <summary>Autosave: every change saves itself ~0.7 s later (debounced so typing doesn't save per keystroke).</summary>
    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        if (dirty)
        {
            _autosave ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _autosave.Stop();
            _autosave.Tick -= AutosaveTick;
            _autosave.Tick += AutosaveTick;
            _autosave.Start();
            _status.Text = "Saving…";
            _status.SetResourceReference(TextBlock.ForegroundProperty, "Text3Brush");
            return;
        }
        _status.Text = _pendingNote ?? "✓ Saved automatically";
        _status.SetResourceReference(TextBlock.ForegroundProperty, _pendingNote is null ? "Text3Brush" : "WarningBrush");
    }

    private void AutosaveTick(object? sender, EventArgs e)
    {
        _autosave?.Stop();
        Save();
    }

    /// <summary>Saves the edited copy. Keeps editing the same objects (no page rebuild), so typing is never interrupted.</summary>
    private void Save()
    {
        _autosave?.Stop();
        if (!_dirty) return;
        var message = _c.SaveSettings(SettingsStore.Clone(_ctx.Edit));
        _saved = true;
        // Commitment mode: some changes wait for the cooldown; say so in the status line instead of a popup per keystroke.
        _pendingNote = _c.Guard.PendingAppliesAt is DateTime at ? $"⏳ Saved — commitment changes apply at {at:HH:mm}" : null;
        if (message is not null) _status.ToolTip = message;
        SetDirty(false);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_dirty) Save(); // flush the last change
        ThemeManager.Apply(_c.Settings.Appearance);
        base.OnClosing(e);
    }

    // ------------------------------------------------------------------ dialogs

    /// <summary>Red confirmation with an "I understand" checkbox that must be ticked.</summary>
    public static bool ConfirmDanger(string title, string text, string checkbox)
    {
        var w = new Window
        {
            Title = title, Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault(),
            FontFamily = (FontFamily)Application.Current.FindResource("AppFont"),
        };
        w.SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "  " + title, FontSize = 18, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Variable Display, Segoe UI") }
            .WithRef(TextBlock.ForegroundProperty, "ErrorBrush"));
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(0, 12, 0, 16) }.WithRef(TextBlock.ForegroundProperty, "Text1Brush"));
        var cb = new CheckBox { Content = checkbox, Margin = new Thickness(0, 0, 0, 18) };
        panel.Children.Add(cb);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "Enable", IsEnabled = false };
        ok.SetResourceReference(StyleProperty, "DangerButton");
        cb.Click += (_, _) => ok.IsEnabled = cb.IsChecked == true;
        ok.Click += (_, _) => w.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        w.Content = panel;
        return w.ShowDialog() == true;
    }

    /// <summary>Headless check: build every page with advanced rows and list settings that have no UI.</summary>
    public static IReadOnlyList<string> CoverageCheck(BreakCoordinator c)
    {
        var edit = SettingsStore.Clone(c.Settings);
        if (edit.Rules.Count == 0) edit.Rules.Add(RuleTemplates.LongBreak());
        edit.General.AdvancedMode = true;
        var ctx = new SettingsContext(edit, c);
        foreach (var (name, _) in SettingsPages.Categories) SettingsPages.Build(name, ctx, null);
        SettingsPages.Build("Rules", ctx, edit.Rules[0].Id.ToString());
        return SettingsPaths.Missing(ctx.Paths);
    }
}
