using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.AppLifecycle;
using Satl_Gui.Services;
using Satl_Gui.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Satl_Gui;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private AppInstance? _mainInstance;
    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;
    public static MainViewModel ViewModel { get; } = new();
    public static LogService Logs { get; } = new();

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        UnhandledException += App_UnhandledException;
        try
        {
            InitializeComponent();
        }
        catch (Exception exception)
        {
            LogStartupException(exception);
            throw;
        }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var current = AppInstance.GetCurrent();
            var activation = current.GetActivatedEventArgs();
            _mainInstance = AppInstance.FindOrRegisterForKey("SATLInstaller.MainWindow");
            if (!_mainInstance.IsCurrent)
            {
                WindowActivationService.AllowForegroundActivation(_mainInstance.ProcessId);
                await _mainInstance.RedirectActivationToAsync(activation);
                Exit();
                return;
            }
            _mainInstance.Activated += MainInstance_Activated;
            Window = new MainWindow();
            WindowActivationService.ShowAndActivate(Window);
        }
        catch (Exception exception)
        {
            LogStartupException(exception);
            throw;
        }
    }

    private static void MainInstance_Activated(object? sender, AppActivationArguments args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Window is null)
            {
                return;
            }
            WindowActivationService.ShowAndActivate(Window);
        });
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _ = Logs.WriteAsync("错误", "应用", e.Exception.ToString());
        LogStartupException(e.Exception);
    }

    private static void LogStartupException(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteamAchievementTranslationInstaller");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "startup-error.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
        }
        catch
        {
            // Startup logging must never hide the original exception.
        }
    }
}
