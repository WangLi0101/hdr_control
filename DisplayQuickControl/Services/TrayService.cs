using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static DisplayQuickControl.Native.NativeMethods;
using Rect = DisplayQuickControl.Native.NativeMethods.Rect;

namespace DisplayQuickControl.Services;

public sealed class TrayService : IDisposable
{
    private const uint Callback = 0x8001;
    private readonly HwndSource _source;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private NotifyIconData _data;
    private readonly Guid _guid = new("9B0E031A-167B-454E-8D7B-71309DC23E31");
    private readonly LogService _log;
    public event Action? OpenRequested, RefreshRequested, SettingsRequested, ExitRequested, DisplayChanged, ThemeChanged;
    public event Action<int>? HotkeyPressed;
    public nint Handle => _source.Handle;
    public TrayService(string root, LogService log)
    {
        _log = log;
        _source = new HwndSource(new HwndSourceParameters("DisplayQuickControl.MessageWindow") { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(Hook);
        Directory.CreateDirectory(root);
        var iconPath = Path.Combine(root, "tray.ico");
        using (var input = Application.GetResourceStream(new Uri("pack://application:,,,/DisplayQuickControl;component/Resources/Icons/app.ico"))!.Stream)
        using (var output = File.Create(iconPath)) input.CopyTo(output);
        var icon = LoadImage(0, iconPath, 1, 32, 32, 0x10); // LR_LOADFROMFILE, multi-resolution original artwork
        if (icon == 0) throw new InvalidOperationException("Tray icon could not be loaded.");
        _data = new NotifyIconData { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = Handle, Id = 1,
            Flags = 1 | 2 | 4 | 0x20 | 0x80, CallbackMessage = Callback, Icon = icon,
            Tip = "Display Quick Control", Info = "", InfoTitle = "", Guid = _guid };
        Add();
    }
    private void Add()
    {
        if (!Shell_NotifyIcon(0, ref _data)) _log.Write("Tray", "Shell rejected icon registration.");
        _data.TimeoutOrVersion = 4; Shell_NotifyIcon(4, ref _data);
    }
    internal Rect Anchor()
    {
        var id = new NotifyIconIdentifier { Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), Window = Handle, Id = 1, Guid = _guid };
        if (Shell_NotifyIconGetRect(ref id, out var rect) == 0) return rect;
        GetCursorPos(out var point); return new Rect { Left = point.X, Right = point.X, Top = point.Y, Bottom = point.Y };
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message == _taskbarCreated) { Add(); return 0; }
        if (message == 0x312) { HotkeyPressed?.Invoke((int)wParam); handled = true; }
        if (message == 0x7E) DisplayChanged?.Invoke(); // WM_DISPLAYCHANGE
        if (message is 0x1A or 0x31A) { ThemeChanged?.Invoke(); DisplayChanged?.Invoke(); }
        if ((uint)message != Callback) return 0;
        var notification = (uint)(long)lParam & 0xffff;
        if (notification is 0x400 or 0x401) OpenRequested?.Invoke(); // NIN_SELECT / keyboard activation
        else if (notification == 0x7B) ShowMenu(); // WM_CONTEXTMENU
        handled = true; return 0;
    }
    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, 0, 1, "打开控制面板"); AppendMenu(menu, 0, 2, "刷新状态");
            AppendMenu(menu, 0, 3, "设置"); AppendMenu(menu, 0x800, 0, null); AppendMenu(menu, 0, 4, "退出");
            GetCursorPos(out var point); SetForegroundWindow(Handle);
            var selected = TrackPopupMenuEx(menu, 0x100 | 0x2, point.X, point.Y, Handle, 0);
            PostMessage(Handle, 0, 0, 0);
            switch (selected) { case 1: OpenRequested?.Invoke(); break; case 2: RefreshRequested?.Invoke(); break; case 3: SettingsRequested?.Invoke(); break; case 4: ExitRequested?.Invoke(); break; }
        }
        finally { DestroyMenu(menu); }
    }
    public void Dispose() { Shell_NotifyIcon(2, ref _data); DestroyIcon(_data.Icon); _source.Dispose(); }
}
