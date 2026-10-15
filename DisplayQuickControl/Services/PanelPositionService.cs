using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static DisplayQuickControl.Native.NativeMethods;
using Point = DisplayQuickControl.Native.NativeMethods.Point;
using Rect = DisplayQuickControl.Native.NativeMethods.Rect;

namespace DisplayQuickControl.Services;

public sealed class PanelPositionService(TrayService tray)
{
    public void Position(Window window)
    {
        var anchor = tray.Anchor();
        var monitor = MonitorFromPoint(new Point { X = (anchor.Left + anchor.Right) / 2, Y = (anchor.Top + anchor.Bottom) / 2 }, 2);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        // Move first: WPF processes per-monitor DPI change before measuring its content.
        SetWindowPos(handle, 0, info.Work.Left, info.Work.Top, 0, 0, 0x1 | 0x4 | 0x10);
        var scale = GetDpiForWindow(handle) / 96.0;
        if (scale <= 0) scale = 1;
        window.MaxHeight = Math.Max(100, (info.Work.Bottom - info.Work.Top) / scale - 24);
        window.Width = Math.Min(400, Math.Max(100, (info.Work.Right - info.Work.Left) / scale - 24));
        window.UpdateLayout();
        var width = (int)Math.Ceiling(window.Width * scale);
        var height = (int)Math.Ceiling(window.ActualHeight * scale);
        if (height == 0) height = (int)Math.Ceiling(470 * scale);
        static PanelPlacement.Box Box(Rect r) => new(r.Left, r.Top, r.Right, r.Bottom);
        var (x, y) = PanelPlacement.Calculate(Box(info.Monitor), Box(info.Work), Box(anchor), width, height, (int)Math.Ceiling(12 * scale));
        SetWindowPos(handle, new nint(-1), x, y, width, height, 0x10);
    }
}
