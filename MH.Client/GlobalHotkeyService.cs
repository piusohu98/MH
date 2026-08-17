using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MH.Client;

public sealed record GlobalHotkeyGesture(uint Modifiers, uint VirtualKey)
{
    public static GlobalHotkeyGesture CtrlAltM { get; } = new(
        GlobalHotkeyNativeMethods.ModifierControl | GlobalHotkeyNativeMethods.ModifierAlt,
        GlobalHotkeyNativeMethods.VirtualKeyM);
}

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x4D48;
    private readonly GlobalHotkeyGesture gesture;
    private HwndSource? source;
    private IntPtr windowHandle;
    private Action? callback;

    public GlobalHotkeyService(GlobalHotkeyGesture? gesture = null)
    {
        this.gesture = gesture ?? GlobalHotkeyGesture.CtrlAltM;
    }

    public bool IsRegistered { get; private set; }

    public string? RegistrationError { get; private set; }

    public bool TryRegister(Window window, Action hotkeyCallback)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(hotkeyCallback);
        if (IsRegistered)
        {
            RegistrationError = "全局热键已经注册。";
            return false;
        }

        var handle = new WindowInteropHelper(window).Handle;
        var hookSource = handle == IntPtr.Zero ? null : HwndSource.FromHwnd(handle);
        if (handle == IntPtr.Zero || hookSource is null)
        {
            RegistrationError = "主窗口句柄尚未就绪。";
            return false;
        }

        if (!GlobalHotkeyNativeMethods.RegisterHotKey(handle, HotkeyId, gesture.Modifiers, gesture.VirtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            RegistrationError = $"Ctrl+Alt+M 注册失败：{new Win32Exception(error).Message}";
            return false;
        }

        windowHandle = handle;
        source = hookSource;
        callback = hotkeyCallback;
        source.AddHook(WindowProcedure);
        IsRegistered = true;
        RegistrationError = null;
        return true;
    }

    public void Dispose()
    {
        if (source is not null)
        {
            source.RemoveHook(WindowProcedure);
        }

        if (IsRegistered)
        {
            GlobalHotkeyNativeMethods.UnregisterHotKey(windowHandle, HotkeyId);
        }

        source = null;
        windowHandle = IntPtr.Zero;
        callback = null;
        IsRegistered = false;
    }

    private IntPtr WindowProcedure(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == GlobalHotkeyNativeMethods.WindowHotkeyMessage
            && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            callback?.Invoke();
        }

        return IntPtr.Zero;
    }

}

internal static class GlobalHotkeyNativeMethods
{
    public const uint ModifierAlt = 0x0001;
    public const uint ModifierControl = 0x0002;
    public const uint VirtualKeyM = 0x4D;
    public const int WindowHotkeyMessage = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint fsModifiers,
        uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
