using System.Drawing;

namespace GeminiLiveTranslate.Ui;

internal static class TrayIconLoader
{
    private const string IconResourceName = "GeminiLiveTranslate.Assets.AppIcon.ico";

    internal static Icon Load()
    {
        using var stream = typeof(TrayIconLoader).Assembly.GetManifestResourceStream(IconResourceName)
            ?? throw new InvalidOperationException("The embedded tray icon resource could not be found.");

        using (var source = new Icon(stream))
        {
            return (Icon)source.Clone();
        }
    }
}
