using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BreakApp.Theme;

/// <summary>The app icon at its sharpest size (BitmapFrame.Create picks the first, 16 px frame, which looks blurry when scaled up).</summary>
public static class AppIcon
{
    private static ImageSource? _large;

    public static ImageSource Large => _large ??= Load();

    private static ImageSource Load()
    {
        var decoder = new IconBitmapDecoder(new Uri("pack://application:,,,/Assets/icon.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var best = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
        best.Freeze();
        return best;
    }
}
