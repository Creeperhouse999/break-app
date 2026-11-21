using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace BreakApp.SettingsUI;

/// <summary>
/// App-themed replacement for MessageBox (UX guideline 5: no plain Windows controls).
/// Borderless dark card, coloured icon per kind, themed buttons; Esc = cancel, Enter = OK.
/// </summary>
public static class ThemedDialog
{
    public enum Kind { Info, Question, Warning, Danger, Error }

    /// <summary>OK/Cancel question. Returns true when the user confirms.</summary>
    public static bool Confirm(string title, string message, string ok = "OK", string cancel = "Cancel", Kind kind = Kind.Question) =>
        Show(title, message, ok, cancel, kind);

    /// <summary>A message with a single button.</summary>
    public static void Info(string title, string message, Kind kind = Kind.Info) => Show(title, message, "OK", null, kind);

    private static bool Show(string title, string message, string ok, string? cancel, Kind kind)
    {
        var (glyph, colorKey) = kind switch
        {
            Kind.Warning => ("", "WarningBrush"),
            Kind.Danger => ("", "ErrorBrush"),
            Kind.Error => ("", "ErrorBrush"),
            Kind.Question => ("", "Accent2Brush"),
            _ => ("", "Accent2Brush"),
        };
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);
        var w = new Window
        {
            Title = title, Width = 460, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None,
            AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = owner is null,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner, Topmost = owner is null,
            FontFamily = (FontFamily)Application.Current!.FindResource("AppFont"),
        };

        var card = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(24, 22, 24, 20), Margin = new Thickness(20), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "ButtonBorderBrush");
        card.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.35, Color = Colors.Black };
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) w.DragMove(); };

        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var chip = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 12, 0) };
        chip.SetResourceReference(Border.BackgroundProperty, "SelectedBrush");
        var icon = new TextBlock
        {
            Text = glyph, FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, colorKey);
        chip.Child = icon;
        head.Children.Add(chip);
        var titleText = new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 330 };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "Text1Brush");
        head.Children.Add(titleText);
        stack.Children.Add(head);

        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 20, Margin = new Thickness(0, 0, 0, 20) };
        body.SetResourceReference(TextBlock.ForegroundProperty, "Text2Brush");
        stack.Children.Add(body);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        bool result = false;
        if (cancel is not null)
        {
            var c = new Button { Content = cancel, Height = 34, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            c.SetResourceReference(FrameworkElement.StyleProperty, "BaseButton");
            c.Click += (_, _) => w.Close();
            buttons.Children.Add(c);
        }
        var okButton = new Button { Content = ok, Height = 34, Padding = new Thickness(22, 0, 22, 0), IsDefault = true, IsCancel = cancel is null };
        okButton.SetResourceReference(FrameworkElement.StyleProperty, kind is Kind.Danger ? "DangerButton" : "PrimaryButton");
        okButton.Click += (_, _) => { result = true; w.Close(); };
        buttons.Children.Add(okButton);
        stack.Children.Add(buttons);

        card.Child = stack;
        w.Content = card;
        w.Loaded += (_, _) => okButton.Focus();
        w.ShowDialog();
        return result;
    }
}
