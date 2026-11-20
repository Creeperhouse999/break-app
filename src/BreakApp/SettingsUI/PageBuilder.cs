using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BreakApp.Core.Models;
using BreakApp.Core.Settings;
using BreakApp.Services;

namespace BreakApp.SettingsUI;

/// <summary>Shared state for all settings pages: the edited copy, search index and coverage registry.</summary>
public sealed class SettingsContext(AppSettings edit, BreakCoordinator coordinator)
{
    public AppSettings Edit { get; set; } = edit;
    public BreakCoordinator C { get; } = coordinator;
    public List<RowInfo> Rows { get; } = [];
    public HashSet<string> Paths { get; } = [];
    public bool Advanced => Edit.General.AdvancedMode;
    public event Action? Changed;
    public event Action? AppearanceChanged;
    public Action<string, string?> Navigate { get; set; } = (_, _) => { };
    public Action Rebuild { get; set; } = () => { };
    public void MarkDirty() => Changed?.Invoke();
    public void MarkAppearance() { Changed?.Invoke(); AppearanceChanged?.Invoke(); }
}

public sealed record RowInfo(string Category, string Group, string Title, string Description, string? Tip, FrameworkElement Element, string? SubPage);

/// <summary>Builds a settings page: groups (cards) of rows with title, one-line description, ⓘ tip and a control.</summary>
public sealed class PageBuilder
{
    private readonly SettingsContext _ctx;
    private readonly string _category;
    private readonly string? _subPage;
    private StackPanel? _group;
    private string _groupTitle = "";

    public StackPanel Panel { get; } = new() { Margin = new Thickness(0, 0, 0, 24) };

    public PageBuilder(SettingsContext ctx, string category, string? subPage = null)
    {
        _ctx = ctx;
        _category = category;
        _subPage = subPage;
    }

    public static TextBlock T(string text, string style)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(FrameworkElement.StyleProperty, style);
        return t;
    }

    public void Intro(string text) => Panel.Children.Add(new TextBlock
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 4), FontSize = 13,
    }.WithRef(TextBlock.ForegroundProperty, "Text2Brush"));

    public void Group(string title, string? note = null)
    {
        _groupTitle = title;
        Panel.Children.Add(T(title.ToUpperInvariant(), "H2"));
        if (note is not null)
            Panel.Children.Add(new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(2, -4, 0, 8) }
                .WithRef(TextBlock.ForegroundProperty, "Text3Brush"));
        _group = new StackPanel();
        var card = new Border { Child = _group };
        card.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        Panel.Children.Add(card);
    }

    /// <summary>Adds a full-width element (lists, editors, buttons) inside the current group.</summary>
    public void Add(UIElement e, bool advanced = false)
    {
        if (advanced && !_ctx.Advanced) return;
        EnsureGroup();
        if (e is FrameworkElement fe && fe.Margin == default) fe.Margin = new Thickness(0, 10, 0, 10);
        AddDivider();
        _group!.Children.Add(e);
    }

    /// <summary>Adds an element outside of any card.</summary>
    public void AddLoose(UIElement e) => Panel.Children.Add(e);

    private void EnsureGroup()
    {
        if (_group is null) Group("General");
    }

    private void AddDivider()
    {
        if (_group!.Children.Count == 0) return;
        _group.Children.Add(new Border { Height = 1 }.WithRef(Border.BackgroundProperty, "DividerBrush"));
    }

    /// <summary>The standard row: title + description on the left, control on the right.</summary>
    public FrameworkElement Row(string path, string title, string description, FrameworkElement control, string? tip = null,
        bool advanced = false, bool danger = false, bool wideControl = false)
    {
        _ctx.Paths.Add(path);
        if (advanced && !_ctx.Advanced) return control;
        EnsureGroup();

        var grid = new Grid { Margin = new Thickness(0, 11, 0, 11) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var titleText = new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, danger ? "ErrorBrush" : "Text1Brush");
        titleRow.Children.Add(titleText);
        if (advanced)
            titleRow.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "advanced", FontSize = 10 }.WithRef(TextBlock.ForegroundProperty, "AccentTextBrush"),
            }.WithRef(Border.BackgroundProperty, "SelectedBrush"));
        if (tip is not null)
        {
            var info = new TextBlock
            {
                Text = "", FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 12,
                Margin = new Thickness(7, 2, 0, 0), Cursor = Cursors.Help, ToolTip = tip,
            };
            info.SetResourceReference(TextBlock.ForegroundProperty, "Text3Brush");
            ToolTipService.SetInitialShowDelay(info, 150);
            titleRow.Children.Add(info);
        }
        left.Children.Add(titleRow);
        if (!string.IsNullOrEmpty(description))
            left.Children.Add(new TextBlock { Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) }
                .WithRef(TextBlock.ForegroundProperty, "Text2Brush"));

        if (wideControl)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 11, 0, 11) };
            left.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(left);
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(control);
            AddDivider();
            _group!.Children.Add(stack);
            _ctx.Rows.Add(new RowInfo(_category, _groupTitle, title, description, tip, stack, _subPage));
            return stack;
        }

        Grid.SetColumn(left, 0);
        Grid.SetColumn(control, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(left);
        grid.Children.Add(control);
        AddDivider();
        _group!.Children.Add(grid);
        _ctx.Rows.Add(new RowInfo(_category, _groupTitle, title, description, tip, grid, _subPage));
        return grid;
    }

    // ------------------------------------------------------------------ typed rows

    public FrameworkElement Toggle(string path, string title, string description, Func<bool> get, Action<bool> set,
        string? tip = null, bool advanced = false, bool danger = false, Func<bool, bool>? confirm = null, bool appearance = false, Action? after = null)
    {
        var cb = new CheckBox { IsChecked = get() };
        cb.SetResourceReference(FrameworkElement.StyleProperty, "Switch");
        cb.Click += (_, _) =>
        {
            var v = cb.IsChecked == true;
            if (confirm is not null && !confirm(v)) { cb.IsChecked = !v; return; }
            set(v);
            Changed(appearance);
            after?.Invoke();
        };
        return Row(path, title, description, cb, tip, advanced, danger);
    }

    public FrameworkElement Number(string path, string title, string description, Func<double> get, Action<double> set,
        double min, double max, string? suffix = null, string? tip = null, bool advanced = false, int decimals = 0)
    {
        var box = new TextBox { Width = 80, Text = get().ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture), HorizontalContentAlignment = HorizontalAlignment.Right };
        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        void Commit()
        {
            if (!double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) v = get();
            v = Math.Clamp(Math.Round(v, decimals), min, max);
            box.Text = v.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture);
            if (Math.Abs(v - get()) > 1e-9) { set(v); Changed(false); }
        }
        return Row(path, title, description, WithSuffix(box, suffix), tip, advanced);
    }

    public FrameworkElement Duration(string path, string title, string description, Func<TimeSpan> get, Action<TimeSpan> set,
        TimeSpan min, TimeSpan max, string? tip = null, bool advanced = false, Action? after = null)
    {
        var box = new DurationBox(get(), min, max);
        box.ValueChanged += v => { set(v); Changed(false); after?.Invoke(); };
        return Row(path, title, description, box, tip, advanced);
    }

    public FrameworkElement Choice<T>(string path, string title, string description, Func<T> get, Action<T> set,
        IReadOnlyList<(T Value, string Label)> options, string? tip = null, bool advanced = false, Func<T, bool>? confirm = null,
        bool appearance = false, Action? after = null)
    {
        var combo = new ComboBox { MinWidth = 170 };
        foreach (var (_, label) in options) combo.Items.Add(label);
        int Index(T v) => Math.Max(0, options.ToList().FindIndex(o => Equals(o.Value, v)));
        combo.SelectedIndex = Index(get());
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0) return;
            var v = options[combo.SelectedIndex].Value;
            if (Equals(v, get())) return;
            if (confirm is not null && !confirm(v)) { combo.SelectedIndex = Index(get()); return; }
            set(v);
            Changed(appearance);
            after?.Invoke();
        };
        return Row(path, title, description, combo, tip, advanced);
    }

    public FrameworkElement Text(string path, string title, string description, Func<string?> get, Action<string> set,
        string? tip = null, bool advanced = false, double width = 260, bool appearance = false)
    {
        var box = new TextBox { Width = width, Text = get() ?? "" };
        box.TextChanged += (_, _) => { if (box.Text != (get() ?? "")) { set(box.Text); Changed(appearance); } };
        return Row(path, title, description, box, tip, advanced);
    }

    /// <summary>A list of strings edited one per line.</summary>
    public FrameworkElement Lines(string path, string title, string description, Func<List<string>> get, Action<List<string>> set,
        string? tip = null, bool advanced = false, int minLines = 3)
    {
        var box = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 18 * minLines + 14, MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top,
            Text = string.Join(Environment.NewLine, get()),
        };
        box.TextChanged += (_, _) =>
        {
            set(box.Text.Split('\n').Select(l => l.Trim('\r', ' ')).Where(l => l.Length > 0).ToList());
            Changed(false);
        };
        return Row(path, title, description, box, tip, advanced, wideControl: true);
    }

    public FrameworkElement Slider(string path, string title, string description, Func<double> get, Action<double> set,
        double min, double max, Func<double, string> format, string? tip = null, bool advanced = false, bool appearance = false)
    {
        var label = new TextBlock { Width = 46, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = format(get()) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Text2Brush");
        var slider = new Slider { Minimum = min, Maximum = max, Value = get(), Width = 180, SmallChange = (max - min) / 100, LargeChange = (max - min) / 10 };
        slider.ValueChanged += (_, e) => { label.Text = format(e.NewValue); set(e.NewValue); Changed(appearance); };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(slider);
        panel.Children.Add(label);
        return Row(path, title, description, panel, tip, advanced);
    }

    public FrameworkElement Color(string path, string title, string description, Func<string?> get, Action<string?> set,
        string? tip = null, bool advanced = false, string emptyLabel = "Theme")
    {
        var picker = new ColorPicker(get(), emptyLabel);
        picker.ValueChanged += v => { set(v); Changed(true); };
        return Row(path, title, description, picker, tip, advanced);
    }

    public FrameworkElement Hotkey(string path, string title, string description, Func<string> get, Action<string> set,
        string? tip = null, bool advanced = false, bool allowEmpty = false)
    {
        var box = new HotkeyBox(get(), allowEmpty);
        box.ValueChanged += v => { set(v); Changed(false); };
        return Row(path, title, description, box, tip, advanced);
    }

    public FrameworkElement File(string path, string title, string description, Func<string?> get, Action<string?> set, string filter,
        string? tip = null, bool advanced = false, bool appearance = false)
    {
        var picker = new FilePicker(get(), filter);
        picker.ValueChanged += v => { set(v); Changed(appearance); };
        return Row(path, title, description, picker, tip, advanced);
    }

    /// <summary>Sound choice: built-in chime / none / a file, with a ▶ test button.</summary>
    public FrameworkElement Sound(string path, string title, string description, Func<string?> get, Action<string?> set,
        string? tip = null, bool advanced = false)
    {
        var picker = new SoundPicker(get(), _ctx.C.Sound);
        picker.ValueChanged += v => { set(v); Changed(false); };
        return Row(path, title, description, picker, tip, advanced);
    }

    public Button Button(string text, Action click, string style = "BaseButton")
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) };
        b.SetResourceReference(FrameworkElement.StyleProperty, style);
        b.Click += (_, _) => click();
        return b;
    }

    public static StackPanel Buttons(params UIElement[] buttons)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }

    private static FrameworkElement WithSuffix(FrameworkElement control, string? suffix)
    {
        if (suffix is null) return control;
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(control);
        p.Children.Add(new TextBlock { Text = suffix, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }
            .WithRef(TextBlock.ForegroundProperty, "Text2Brush"));
        return p;
    }

    private void Changed(bool appearance)
    {
        if (appearance) _ctx.MarkAppearance();
        else _ctx.MarkDirty();
    }
}

internal static class UiExtensions
{
    public static T WithRef<T>(this T e, DependencyProperty p, string key) where T : FrameworkElement
    {
        e.SetResourceReference(p, key);
        return e;
    }
}
