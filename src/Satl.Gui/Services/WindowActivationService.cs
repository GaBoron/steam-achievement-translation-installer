using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Satl_Gui.Services;

public static class WindowActivationService
{
    private const int RestoreWindow = 9;

    public static void AllowForegroundActivation(uint processId)
    {
        if (processId != 0)
        {
            _ = AllowSetForegroundWindow(processId);
        }
    }

    public static void ShowAndActivate(Window window)
    {
        window.Activate();
        window.AppWindow.Show(activateWindow: true);

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (windowHandle == 0)
        {
            return;
        }

        _ = ShowWindow(windowHandle, RestoreWindow);
        _ = BringWindowToTop(windowHandle);
        _ = SetForegroundWindow(windowHandle);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);
}
