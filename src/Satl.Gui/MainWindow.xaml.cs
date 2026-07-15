using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Satl_Gui.Services;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Satl_Gui;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly WindowPlacementService _windowPlacement = new();

    public MainWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        RestoreWindowPlacement();
        AppWindow.Closing += AppWindow_Closing;
        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            SystemBackdrop = null;
        }

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
        RootFrame.ActualThemeChanged += (_, _) => ApplyTitleBarTheme(RootFrame.ActualTheme);
        ApplyTitleBarTheme(RootFrame.ActualTheme);
    }

    private void RestoreWindowPlacement()
    {
        var saved = _windowPlacement.Load();
        var displayArea = saved is null
            ? DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary)
            : DisplayArea.GetFromPoint(
                new PointInt32(saved.X, saved.Y),
                DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var placement = saved is null
            ? WindowPlacementService.CenterDefault(workArea.X, workArea.Y, workArea.Width, workArea.Height)
            : WindowPlacementService.FitToWorkArea(
                saved,
                workArea.X,
                workArea.Y,
                workArea.Width,
                workArea.Height);
        AppWindow.MoveAndResize(new RectInt32(
            placement.X,
            placement.Y,
            placement.Width,
            placement.Height));
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        var position = sender.Position;
        var size = sender.Size;
        _windowPlacement.Save(new WindowPlacement(position.X, position.Y, size.Width, size.Height));
    }

    public void ApplyTitleBarTheme(ElementTheme theme)
    {
        var titleBar = AppWindow.TitleBar;
        if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
        {
            var systemColors = new Windows.UI.ViewManagement.UISettings();
            var background = systemColors.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            var foreground = systemColors.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground);
            var accent = systemColors.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            titleBar.BackgroundColor = background;
            titleBar.ForegroundColor = foreground;
            titleBar.InactiveBackgroundColor = background;
            titleBar.InactiveForegroundColor = foreground;
            titleBar.ButtonBackgroundColor = background;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonHoverBackgroundColor = accent;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor = accent;
            titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.ButtonInactiveBackgroundColor = background;
            titleBar.ButtonInactiveForegroundColor = foreground;
            return;
        }

        var dark = theme == ElementTheme.Dark;
        var titleBarBackground = dark
            ? Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32)
            : Microsoft.UI.ColorHelper.FromArgb(255, 243, 243, 243);
        var titleBarForeground = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        var titleBarInactiveBackground = dark
            ? Microsoft.UI.ColorHelper.FromArgb(255, 43, 43, 43)
            : Microsoft.UI.ColorHelper.FromArgb(255, 249, 249, 249);
        titleBar.BackgroundColor = titleBarBackground;
        titleBar.ForegroundColor = titleBarForeground;
        titleBar.InactiveBackgroundColor = titleBarInactiveBackground;
        titleBar.InactiveForegroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 128, 128, 128);
        titleBar.ButtonBackgroundColor = titleBarBackground;
        titleBar.ButtonForegroundColor = titleBarForeground;
        titleBar.ButtonHoverBackgroundColor = dark
            ? Microsoft.UI.ColorHelper.FromArgb(255, 58, 58, 58)
            : Microsoft.UI.ColorHelper.FromArgb(255, 229, 229, 229);
        titleBar.ButtonHoverForegroundColor = titleBarForeground;
        titleBar.ButtonPressedBackgroundColor = dark
            ? Microsoft.UI.ColorHelper.FromArgb(255, 72, 72, 72)
            : Microsoft.UI.ColorHelper.FromArgb(255, 204, 204, 204);
        titleBar.ButtonPressedForegroundColor = titleBarForeground;
        titleBar.ButtonInactiveBackgroundColor = titleBarInactiveBackground;
        titleBar.ButtonInactiveForegroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 128, 128, 128);
    }
}
