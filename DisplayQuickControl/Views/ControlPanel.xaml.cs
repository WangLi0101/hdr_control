using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DisplayQuickControl.Services;
using DisplayQuickControl.ViewModels;
using static DisplayQuickControl.Native.NativeMethods;

namespace DisplayQuickControl.Views;

public partial class ControlPanel : Window
{
    private readonly SettingsService _settings;
    private readonly PanelPositionService _position;
    private readonly ControlPanelViewModel _viewModel;
    private bool _positioning, _queued, _allowClose;
    public event Action? SettingsRequested;
    public event Action? CustomizeModesRequested;
    public ControlPanel(ControlPanelViewModel viewModel, SettingsService settings, PanelPositionService position)
    {
        InitializeComponent(); DataContext = _viewModel = viewModel; _settings = settings; _position = position;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = (long)GetWindowLongPtr(hwnd, -20);
            SetWindowLongPtr(hwnd, -20, (nint)((style | 0x80) & ~0x40000)); // WS_EX_TOOLWINDOW, no Alt+Tab entry
            var rounded = 2; DwmSetWindowAttribute(hwnd, 33, ref rounded, 4);
            ThemeService.ConfigureWindow(this);
            HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
        };
        Closing += (_, e) => { if (!_allowClose) { e.Cancel = true; Hide(); } };
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x2E0) QueuePosition(); // WM_DPICHANGED: let WPF process it first.
        return 0;
    }
    public async Task OpenAsync()
    {
        Surface.Opacity = 0;
        if (!IsVisible) Show();
        Reposition(); Activate(); SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Surface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        if (_settings.Current.RefreshOnOpen) await _viewModel.RefreshAsync();
    }
    public void Reposition()
    {
        if (!IsVisible || _positioning) return;
        _positioning = true; try { _position.Position(this); } finally { _positioning = false; }
    }
    private void QueuePosition()
    {
        if (_queued || _positioning) return;
        _queued = true; Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { _queued = false; Reposition(); });
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => QueuePosition();
    private void OnDeactivated(object? sender, EventArgs e) { if (_settings.Current.CloseOnDeactivate) Hide(); }
    private void OnPreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } }
    private void SettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void CustomizeModesClick(object sender, RoutedEventArgs e) => CustomizeModesRequested?.Invoke();
    private void DisplaySettingsClick(object sender, RoutedEventArgs e) => OpenUri("ms-settings:display");
    private void HdrSettingsClick(object sender, RoutedEventArgs e) => OpenUri("ms-settings:display-hdr");
    private void OpenUri(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { _viewModel.Message = "Windows settings could not be opened: " + ex.Message; }
    }
    public void CloseForExit() { _allowClose = true; Close(); }
}
