using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
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
    public MainWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Resize(new SizeInt32(1120, 760));
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
        titleBar.BackgroundColor = dark
            ? Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32)
            : Microsoft.UI.ColorHelper.FromArgb(255, 243, 243, 243);
        titleBar.ForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        titleBar.InactiveBackgroundColor = dark
            ? Microsoft.UI.ColorHelper.FromArgb(255, 43, 43, 43)
            : Microsoft.UI.ColorHelper.FromArgb(255, 249, 249, 249);
        titleBar.InactiveForegroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 128, 128, 128);
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        titleBar.ButtonHoverBackgroundColor = dark
            ? Microsoft.UI.ColorHelper.FromArgb(255, 58, 58, 58)
            : Microsoft.UI.ColorHelper.FromArgb(255, 229, 229, 229);
        titleBar.ButtonHoverForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        titleBar.ButtonPressedBackgroundColor = dark
            ? Microsoft.UI.ColorHelper.FromArgb(255, 72, 72, 72)
            : Microsoft.UI.ColorHelper.FromArgb(255, 204, 204, 204);
        titleBar.ButtonPressedForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveForegroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 128, 128, 128);
    }
}
