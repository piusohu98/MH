using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MH.Client;

internal readonly record struct OverlayMonitorTarget(Rect WorkAreaPixels);

internal static class OverlayMonitorPositioner
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public static bool TryAcquireAndPosition(
        Window window,
        double marginDip,
        out OverlayMonitorTarget target)
    {
        ArgumentNullException.ThrowIfNull(window);
        target = default;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !TryGetCursorMonitor(out target))
        {
            return false;
        }

        if (TryPosition(handle, target, marginDip))
        {
            return true;
        }

        target = default;
        return false;
    }

    public static bool TryPosition(
        Window window,
        OverlayMonitorTarget target,
        double marginDip)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero && TryPosition(handle, target, marginDip);
    }

    private static bool TryGetCursorMonitor(out OverlayMonitorTarget target)
    {
        target = default;
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return false;
        }

        var monitor = NativeMethods.MonitorFromPoint(cursor, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>()
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)
            || info.WorkArea.Right <= info.WorkArea.Left
            || info.WorkArea.Bottom <= info.WorkArea.Top)
        {
            return false;
        }

        target = new OverlayMonitorTarget(new Rect(
            info.WorkArea.Left,
            info.WorkArea.Top,
            info.WorkArea.Right - info.WorkArea.Left,
            info.WorkArea.Bottom - info.WorkArea.Top));
        return true;
    }

    private static bool TryPosition(
        IntPtr handle,
        OverlayMonitorTarget target,
        double marginDip)
    {
        if (!NativeMethods.GetWindowRect(handle, out var windowRect)
            || windowRect.Right <= windowRect.Left
            || windowRect.Bottom <= windowRect.Top
            || !OverlayPositionCalculator.TryScaleMargin(
                marginDip,
                NativeMethods.GetDpiForWindow(handle),
                out var marginPixels)
            || !OverlayPositionCalculator.TryCalculate(
                target.WorkAreaPixels,
                new Size(
                    windowRect.Right - windowRect.Left,
                    windowRect.Bottom - windowRect.Top),
                marginPixels,
                out var position))
        {
            return false;
        }

        return NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            checked((int)position.X),
            checked((int)position.Y),
            0,
            0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }
}

public static class OverlayPositionCalculator
{
    private const double DefaultDpi = 96;

    public static bool TryScaleMargin(
        double marginDip,
        uint dpi,
        out double marginPixels)
    {
        marginPixels = 0;
        if (!double.IsFinite(marginDip) || marginDip < 0 || dpi == 0)
        {
            return false;
        }

        var scaled = Math.Ceiling(marginDip * dpi / DefaultDpi);
        if (!double.IsFinite(scaled))
        {
            return false;
        }

        marginPixels = scaled;
        return true;
    }

    public static bool TryCalculate(
        Rect workArea,
        Size windowSize,
        double margin,
        out Point position)
    {
        position = default;
        if (workArea.IsEmpty
            || !double.IsFinite(workArea.Left)
            || !double.IsFinite(workArea.Top)
            || !double.IsFinite(workArea.Width)
            || !double.IsFinite(workArea.Height)
            || workArea.Width <= 0
            || workArea.Height <= 0
            || !double.IsFinite(windowSize.Width)
            || !double.IsFinite(windowSize.Height)
            || windowSize.Width <= 0
            || windowSize.Height <= 0
            || !double.IsFinite(margin)
            || margin < 0
            || windowSize.Width + margin > workArea.Width
            || windowSize.Height + margin > workArea.Height)
        {
            return false;
        }

        position = new Point(
            workArea.Right - windowSize.Width - margin,
            workArea.Bottom - windowSize.Height - margin);
        return double.IsFinite(position.X) && double.IsFinite(position.Y);
    }
}
