using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GeminiLiveTranslate.Settings;

namespace GeminiLiveTranslate.Diagnostics;

internal static class WindowSizeDiagnostics
{
    private static readonly object Gate = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "gemini-live-translate-dotnet",
        "window-size-debug.log");

    [Conditional("WINDOW_SIZE_DIAGNOSTICS")]
    public static void Log(
        string stage,
        AppSettings? settings = null,
        Window? window = null,
        string? details = null)
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            var line = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture))
                .Append(" | stage=").Append(Sanitize(stage))
                .Append(" | pid=").Append(process.Id)
                .Append(" | version=").Append(version)
                .Append(" | exe=").Append(Sanitize(Environment.ProcessPath ?? "unknown"));

            if (settings is not null)
            {
                line.Append(" | saved=")
                    .Append(Rect(
                        settings.Hud.Left,
                        settings.Hud.Top,
                        settings.Hud.Width,
                        settings.Hud.Height));
            }

            var workArea = SystemParameters.WorkArea;
            line.Append(" | workArea=")
                .Append(Rect(workArea.Left, workArea.Top, workArea.Width, workArea.Height));

            if (window is not null)
            {
                var dpi = VisualTreeHelper.GetDpi(window);
                var handle = new WindowInteropHelper(window).Handle;
                line.Append(" | window=")
                    .Append(Rect(window.Left, window.Top, window.Width, window.Height))
                    .Append(" | actual=").Append(Size(window.ActualWidth, window.ActualHeight))
                    .Append(" | min=").Append(Size(window.MinWidth, window.MinHeight))
                    .Append(" | max=").Append(Size(window.MaxWidth, window.MaxHeight))
                    .Append(" | state=").Append(window.WindowState)
                    .Append(" | sizeToContent=").Append(window.SizeToContent)
                    .Append(" | visibility=").Append(window.Visibility)
                    .Append(" | dpi=").Append(Number(dpi.PixelsPerInchX)).Append('x').Append(Number(dpi.PixelsPerInchY))
                    .Append(" | dpiScale=").Append(Number(dpi.DpiScaleX)).Append('x').Append(Number(dpi.DpiScaleY))
                    .Append(" | hwnd=0x").Append(handle.ToInt64().ToString("X", CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(details))
                line.Append(" | details=").Append(Sanitize(details));

            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, line.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never interfere with application startup.
        }
    }

    private static string Rect(double left, double top, double width, double height) =>
        $"({Number(left)},{Number(top)},{Number(width)},{Number(height)})";

    private static string Size(double width, double height) => $"({Number(width)},{Number(height)})";

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Sanitize(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
