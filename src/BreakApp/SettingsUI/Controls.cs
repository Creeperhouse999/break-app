using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BreakApp.Core.Models;
using BreakApp.Core.Settings;
using BreakApp.Services;
using HotkeyModel = BreakApp.Core.Settings.Hotkey;

namespace BreakApp.SettingsUI;

/// <summary>Number + unit (seconds / minutes / hours) editor for a TimeSpan.</summary>
public sealed class DurationBox : StackPanel
{
    private static readonly (string Label, double Seconds)[] Units = [("seconds", 1), ("minutes", 60), ("hours", 3600)];
    private readonly TextBox _num = new() { Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right };
    private readonly ComboBox _unit = new() { MinWidth = 104, Margin = new Thickness(6, 0, 0, 0) };
    private readonly TimeSpan _min, _max;
    private TimeSpan _value;

    public event Action<TimeSpan>? ValueChanged;

    public DurationBox(TimeSpan value, TimeSpan min, TimeSpan max)
    {
        Orientation = Orientation.Horizontal;
        _min = min; _max = max; _value = value;
        foreach (var u in Units) _unit.Items.Add(u.Label);
        Show(value);
        Children.Add(_num);
        Children.Add(_unit);
        _num.LostKeyboardFocus += (_, _) => Commit();
        _num.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        _unit.SelectionChanged += (_, _) => Commit();
    }

    private void Show(TimeSpan v)
    {
        var s = v.TotalSeconds;
        var idx = s >= 3600 && s % 3600 == 0 ? 2 : s >= 60 && s % 60 == 0 ? 1 : 0;
        _unit.SelectedIndex = idx;
        _num.Text = (s / Units[idx].Seconds).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void Commit()
    {
        if (_unit.SelectedIndex < 0) return;
        if (!double.TryParse(_num.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n < 0) { Show(_value); return; }
        var v = TimeSpan.FromSeconds(Math.Round(n * Units[_unit.SelectedIndex].Seconds));
        if (v < _min) v = _min;
        if (v > _max) v = _max;
        if (v == _value) return;
        _value = v;
        ValueChanged?.Invoke(v);
    }
}

/// <summary>Colour swatch + hex box + system picker + reset to theme.</summary>
public sealed class ColorPicker : StackPanel
{
    private readonly Border _swatch = new() { Width = 26, Height = 26, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1) };
    private readonly TextBox _hex = new() { Width = 92, Height = 30, MinHeight = 30, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(8, 0, 0, 0) };
    private string? _value;

    public event Action<string?>? ValueChanged;

    public ColorPicker(string? value, string emptyLabel)
    {
        Orientation = Orientation.Horizontal;
        _swatch.SetResourceReference(Border.BorderBrushProperty, "ButtonBorderBrush");
        Children.Add(_swatch);
        Children.Add(_hex);
        var pick = new Button { Content = "Pick…", Margin = new Thickness(6, 0, 0, 0) };
        var reset = new Button { Content = emptyLabel, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Use the theme colour" };
        reset.SetResourceReference(StyleProperty, "GhostButton");
        Children.Add(pick);
        Children.Add(reset);
        Set(value, false);
        _hex.LostKeyboardFocus += (_, _) => FromText();
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) FromText(); };
        pick.Click += (_, _) =>
        {
            using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
            if (ColorHex.TryParse(_value, out var r, out var g, out var b)) dlg.Color = System.Drawing.Color.FromArgb(r, g, b);
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) Set(ColorHex.ToHex(dlg.Color.R, dlg.Color.G, dlg.Color.B), true);
        };
        reset.Click += (_, _) => Set(null, true);
    }

    private void FromText()
    {
        if (string.IsNullOrWhiteSpace(_hex.Text)) { Set(null, true); return; }
        var n = ColorHex.Normalize(_hex.Text);
        if (n is null) { Set(_value, false); return; }
        Set(n, true);
    }

    private void Set(string? v, bool notify)
    {
        var changed = v != _value;
        _value = v;
        _hex.Text = v ?? "";
        if (v is not null && ColorHex.TryParse(v, out var r, out var g, out var b))
            _swatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        else _swatch.SetResourceReference(Border.BackgroundProperty, "AccentGradientBrush");
        if (notify && changed) ValueChanged?.Invoke(v);
    }
}

/// <summary>Click, then press a key combo. Esc cancels, Backspace clears (when allowed).</summary>
public sealed class HotkeyBox : Button
{
    private string _value;
    private readonly bool _allowEmpty;
    private bool _recording;

    public event Action<string>? ValueChanged;

    public HotkeyBox(string value, bool allowEmpty)
    {
        // Implicit styles only match the exact type, so a Button subclass must opt into the theme explicitly.
        SetResourceReference(StyleProperty, "BaseButton");
        _value = value;
        _allowEmpty = allowEmpty;
        MinWidth = 170;
        Content = Label();
        ToolTip = "Click, then press the new key combination (needs Ctrl, Alt, Shift or Win). Esc cancels" + (allowEmpty ? ", Backspace clears." : ".");
        Click += (_, _) => { _recording = true; Content = "Press keys…"; Focus(); };
        LostKeyboardFocus += (_, _) => { _recording = false; Content = Label(); };
        PreviewKeyDown += OnKey;
    }

    private string Label() => string.IsNullOrEmpty(_value) ? "(none)" : _value;

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { _recording = false; Content = Label(); return; }
        if (key == Key.Back && _allowEmpty) { Commit(""); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        var mods = Keyboard.Modifiers;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        if (HotkeyModel.TryParse(string.Join("+", parts), out var hk)) Commit(hk.ToString());
        else Content = "Needs Ctrl/Alt/Shift/Win…";
    }

    private void Commit(string v)
    {
        _recording = false;
        _value = v;
        Content = Label();
        ValueChanged?.Invoke(v);
    }
}

/// <summary>File path with Browse / Clear.</summary>
public sealed class FilePicker : StackPanel
{
    private readonly TextBlock _name = new() { Width = 170, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private string? _value;
    public event Action<string?>? ValueChanged;

    public FilePicker(string? value, string filter)
    {
        Orientation = Orientation.Horizontal;
        _value = value;
        _name.SetResourceReference(TextBlock.ForegroundProperty, "Text2Brush");
        Show();
        var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
        var clear = new Button { Content = "Clear", Margin = new Thickness(4, 0, 0, 0) };
        clear.SetResourceReference(StyleProperty, "GhostButton");
        Children.Add(_name);
        Children.Add(browse);
        Children.Add(clear);
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter };
            if (dlg.ShowDialog() == true) { _value = dlg.FileName; Show(); ValueChanged?.Invoke(_value); }
        };
        clear.Click += (_, _) => { _value = null; Show(); ValueChanged?.Invoke(null); };
    }

    private void Show()
    {
        _name.Text = string.IsNullOrEmpty(_value) ? "(none)" : Path.GetFileName(_value);
        _name.ToolTip = _value;
    }
}

/// <summary>Built-in chime / no sound / custom file, plus a test button.</summary>
public sealed class SoundPicker : StackPanel
{
    private readonly ComboBox _combo = new() { MinWidth = 150 };
    private string? _value;
    public event Action<string?>? ValueChanged;

    public SoundPicker(string? value, SoundService sound)
    {
        Orientation = Orientation.Horizontal;
        _value = value;
        Refill();
        var test = new Button { Content = "", Margin = new Thickness(6, 0, 0, 0), ToolTip = "Play" };
        test.SetResourceReference(StyleProperty, "IconButton");
        test.Click += (_, _) => sound.Play(_value);
        Children.Add(_combo);
        Children.Add(test);
        _combo.SelectionChanged += (_, _) =>
        {
            switch (_combo.SelectedIndex)
            {
                case 0: Set(null); break;
                case 1: Set(""); break;
                case 2:
                    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Sounds|*.wav;*.mp3;*.wma;*.m4a|All files|*.*" };
                    if (dlg.ShowDialog() == true) Set(dlg.FileName);
                    else Refill();
                    break;
            }
        };
    }

    private void Refill()
    {
        _combo.Items.Clear();
        _combo.Items.Add("Built-in chime");
        _combo.Items.Add("No sound");
        _combo.Items.Add(string.IsNullOrEmpty(_value) ? "Choose file…" : Path.GetFileName(_value));
        _combo.SelectedIndex = _value is null ? 0 : _value == "" ? 1 : 2;
    }

    private void Set(string? v)
    {
        if (v == _value) return;
        _value = v;
        ValueChanged?.Invoke(v);
    }
}

/// <summary>Editor for weekly time windows (quiet times, rule schedules): day chips, start, end, label.</summary>
public sealed class TimeWindowsEditor : StackPanel
{
    private static readonly DayOfWeek[] Week = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];
    private readonly List<TimeWindow> _list;
    private readonly Action _changed;
    private readonly string _addLabel;

    public TimeWindowsEditor(List<TimeWindow> list, Action changed, string addLabel)
    {
        _list = list;
        _changed = changed;
        _addLabel = addLabel;
        Build();
    }

    private void Build()
    {
        Children.Clear();
        foreach (var w in _list.ToList()) Children.Add(RowFor(w));
        var add = new Button { Content = "+ " + _addLabel, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        add.Click += (_, _) =>
        {
            _list.Add(new TimeWindow { Days = [.. Week.Take(5)], Start = new TimeOnly(9, 0), End = new TimeOnly(10, 0), Label = "" });
            _changed();
            Build();
        };
        Children.Add(add);
    }

    private FrameworkElement RowFor(TimeWindow w)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        foreach (var d in Week)
        {
            var chip = new ToggleButton
            {
                Content = d.ToString()[..2], Width = 34, Height = 28, Margin = new Thickness(0, 0, 4, 0), IsChecked = w.Days.Contains(d),
                Cursor = Cursors.Hand, ToolTip = d.ToString(),
            };
            chip.Template = ChipTemplate();
            // Style setter (not a local value) so the template's "checked = white" trigger still wins.
            chip.Style = ChipStyle;
            chip.Click += (_, _) =>
            {
                if (chip.IsChecked == true) { if (!w.Days.Contains(d)) w.Days.Add(d); }
                else w.Days.Remove(d);
                _changed();
            };
            row.Children.Add(chip);
        }
        row.Children.Add(TimeBox(() => w.Start, v => w.Start = v));
        row.Children.Add(new TextBlock { Text = "–", Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center }.WithRef(TextBlock.ForegroundProperty, "Text2Brush"));
        row.Children.Add(TimeBox(() => w.End, v => w.End = v));
        var label = new TextBox { Width = 130, Margin = new Thickness(8, 0, 0, 0), Text = w.Label, ToolTip = "Label (shown in the tray, e.g. \"Lesson\")" };
        label.TextChanged += (_, _) => { w.Label = label.Text; _changed(); };
        row.Children.Add(label);
        var del = new Button { Content = "", ToolTip = "Remove", Margin = new Thickness(4, 0, 0, 0) };
        del.SetResourceReference(StyleProperty, "IconButton");
        del.Click += (_, _) => { _list.Remove(w); _changed(); Build(); };
        row.Children.Add(del);
        return row;
    }

    private TextBox TimeBox(Func<TimeOnly> get, Action<TimeOnly> set)
    {
        var box = new TextBox { Width = 64, Text = get().ToString("HH:mm", CultureInfo.InvariantCulture), Margin = new Thickness(6, 0, 0, 0), ToolTip = "24-hour time, e.g. 13:30" };
        box.LostKeyboardFocus += (_, _) =>
        {
            if (TimeOnly.TryParse(box.Text, CultureInfo.InvariantCulture, out var t)) { set(t); _changed(); }
            box.Text = get().ToString("HH:mm", CultureInfo.InvariantCulture);
        };
        return box;
    }

    private static readonly Style ChipStyle = CreateChipStyle();

    private static Style CreateChipStyle()
    {
        var s = new Style(typeof(ToggleButton));
        s.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Text1Brush")));
        return s;
    }

    private static ControlTemplate ChipTemplate()
    {
        var t = new ControlTemplate(typeof(ToggleButton));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetResourceReference(Border.BorderBrushProperty, "ButtonBorderBrush");
        border.SetResourceReference(Border.BackgroundProperty, "InputBackgroundBrush");
        var text = new FrameworkElementFactory(typeof(ContentPresenter));
        text.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(text);
        t.VisualTree = border;
        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Accent1Brush"), "Bd"));
        on.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Accent1Brush"), "Bd"));
        on.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        t.Triggers.Add(on);
        return t;
    }
}
