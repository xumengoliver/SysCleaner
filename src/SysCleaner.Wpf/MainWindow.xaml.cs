using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SysCleaner.Wpf;

public partial class MainWindow : Window
{
    private const uint WmDropFiles = 0x0233;
    private const uint WmCopyData = 0x004A;
    private const uint WmCopyGlobalData = 0x0049;
    private const uint MsgfltAllow = 1;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        AllowMessage(windowHandle, WmDropFiles);
        AllowMessage(windowHandle, WmCopyData);
        AllowMessage(windowHandle, WmCopyGlobalData);
    }

    private static void AllowMessage(IntPtr windowHandle, uint message)
    {
        try
        {
            ChangeWindowMessageFilterEx(windowHandle, message, MsgfltAllow, IntPtr.Zero);
        }
        catch
        {
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr changeFilterStruct);
}