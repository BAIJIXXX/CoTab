using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CoTab.Helpers;

public static class ScreenUtil
{
    /// <summary>Work area of the monitor under the window, in physical pixels.</summary>
    public static Rect GetWorkAreaPixels(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return new Rect(0, 0, 1920, 1080);
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            var work = info.Work;
            return new Rect(work.Left, work.Top, work.Width, work.Height);
        }
        return new Rect(0, 0, 1920, 1080);
    }

    /// <summary>Work area of the monitor under the window, in DIPs.</summary>
    public static Rect GetWorkAreaDip(Window window)
    {
        var work = GetWorkAreaPixels(window);
        var dpi = VisualTreeHelper.GetDpi(window);
        return new Rect(
            work.Left / dpi.DpiScaleX,
            work.Top / dpi.DpiScaleY,
            work.Width / dpi.DpiScaleX,
            work.Height / dpi.DpiScaleY);
    }

    /// <summary>Window rectangle in physical screen pixels.</summary>
    public static Rect GetWindowPixels(Window window)
    {
        var topLeft = window.PointToScreen(new Point(0, 0));
        var bottomRight = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));
        return new Rect(topLeft, bottomRight);
    }

    public static DockEdge DetectDock(Window window, double thresholdDip = 26)
    {
        var win = GetWindowPixels(window);
        var work = GetWorkAreaPixels(window);
        var dpi = VisualTreeHelper.GetDpi(window);
        var threshold = thresholdDip * Math.Max(dpi.DpiScaleX, dpi.DpiScaleY);

        var distances = EdgeDistances(win, work);
        var best = distances.OrderBy(d => d.Distance).First();
        return best.Distance <= threshold ? best.Edge : DockEdge.None;
    }

    /// <summary>
    /// Distance from each work-area edge to the corresponding window edge.
    /// A negative distance (the window extends past the edge) is treated as 0:
    /// a partially off-screen panel is already docked and must be able to
    /// auto-collapse instead of being ignored as "too far from the edge".
    /// </summary>
    private static (DockEdge Edge, double Distance)[] EdgeDistances(Rect win, Rect work) =>
        new[]
        {
            (Edge: DockEdge.Left, Distance: Math.Max(0, win.Left - work.Left)),
            (Edge: DockEdge.Right, Distance: Math.Max(0, work.Right - win.Right)),
            (Edge: DockEdge.Top, Distance: Math.Max(0, win.Top - work.Top)),
            (Edge: DockEdge.Bottom, Distance: Math.Max(0, work.Bottom - win.Bottom))
        };

    /// <summary>The nearest work-area edge, regardless of the dock threshold.</summary>
    public static DockEdge NearestEdge(Window window)
    {
        var win = GetWindowPixels(window);
        var work = GetWorkAreaPixels(window);

        return EdgeDistances(win, work).OrderBy(d => d.Distance).First().Edge;
    }

    /// <summary>Moves the window so the requested edge sits exactly on the monitor work-area edge.</summary>
    public static void SnapToDock(Window window, DockEdge edge)
    {
        if (edge == DockEdge.None) return;
        var win = GetWindowPixels(window);
        var work = GetWorkAreaPixels(window);
        var dpi = VisualTreeHelper.GetDpi(window);

        var dx = 0.0;
        var dy = 0.0;
        switch (edge)
        {
            case DockEdge.Left:
                dx = work.Left - win.Left;
                break;
            case DockEdge.Right:
                dx = work.Right - win.Right;
                break;
            case DockEdge.Top:
                dy = work.Top - win.Top;
                break;
            case DockEdge.Bottom:
                dy = work.Bottom - win.Bottom;
                break;
        }

        if (Math.Abs(dx) > 0.1) window.Left += dx / dpi.DpiScaleX;
        if (Math.Abs(dy) > 0.1) window.Top += dy / dpi.DpiScaleY;
    }

    public static void PlaceAtDefaultPosition(Window window)
    {
        var work = SystemParameters.WorkArea;
        window.Width = 400;
        window.Height = 600;
        window.Left = work.Right - window.Width;
        window.Top = work.Top + Math.Max(24, (work.Height - window.Height) / 2);
    }

    public static bool IsPositionVisible(double left, double top, double width, double height)
    {
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        var windowRect = new Rect(left, top, width, height);
        var intersection = Rect.Intersect(windowRect, virtualScreen);
        return !intersection.IsEmpty && intersection.Width > 80 && intersection.Height > 80;
    }
}
