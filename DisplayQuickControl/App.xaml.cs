using System.IO;
using System.Windows;
using System.Windows.Threading;
using DisplayQuickControl.Models;
using DisplayQuickControl.Services;
using DisplayQuickControl.ViewModels;
using DisplayQuickControl.Views;

namespace DisplayQuickControl;

public partial class App : Application
{
    private readonly SingleInstanceService _instance = new();
    private LogService? _log;
    private SettingsService _settings = null!;
    private TrayService? _tray;
    private HotkeyService? _hotkeys;
    private ControlPanel? _panel;
    private ControlPanelViewModel _viewModel = null!;
    private SettingsWindow? _settingsWindow;
    private readonly StartupService _startup = new();
    private readonly ThemeService _theme = new();
    private readonly DispatcherTimer _displayDebounce = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private bool _exiting;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!_instance.Acquire())
        {
            try { await _instance.SignalExistingAsync(); }
            catch (IOException ex) { MessageBox.Show(ex.Message, "Display Quick Control", MessageBoxButton.OK, MessageBoxImage.Warning); }
            Shutdown(); return;
        }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayQuickControl");
        _log = new LogService(root); _log.Write("Application Start", "Display Quick Control 1.0.0");
        try
        {
            _settings = new SettingsService(root, _log); _settings.Load(); _theme.Apply(_settings.Current.Theme);
            _viewModel = new ControlPanelViewModel(_settings, new DisplayService(_log), new ColorControlService(_settings, _log), _log);
            _tray = new TrayService(root, _log);
            _panel = new ControlPanel(_viewModel, _settings, new PanelPositionService(_tray));
            _panel.SettingsRequested += OpenSettings;
            _tray.OpenRequested += () => _ = OpenPanelAsync();
            _tray.RefreshRequested += () => _ = RefreshFromTrayAsync();
            _tray.SettingsRequested += OpenSettings;
            _tray.ExitRequested += ExitApplication;
            _tray.DisplayChanged += () => { _displayDebounce.Stop(); if (_panel.IsVisible) _displayDebounce.Start(); };
            _tray.ThemeChanged += () => _theme.Apply(_settings.Current.Theme);
            _displayDebounce.Tick += async (_, _) => { _displayDebounce.Stop(); if (_panel.IsVisible) { await _viewModel.RefreshAsync(); _panel.Reposition(); } };
            _hotkeys = new HotkeyService(_tray.Handle);
            _tray.HotkeyPressed += _hotkeys.Handle;
            _hotkeys.Invoked += mode => _ = ApplyHotkeyAsync(mode);
            var warning = _hotkeys.Apply(_settings.Current);
            if (_settings.LoadWarning is { } loadWarning) _viewModel.Message = loadWarning;
            if (!string.IsNullOrEmpty(warning))
            {
                _viewModel.Message = warning;
                MessageBox.Show(warning, "Display Quick Control · Hotkey conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            _ = _instance.ListenAsync(() => Dispatcher.BeginInvoke(() => _ = OpenPanelAsync()), _log);
            // No StartupUri or Show(): normal startup creates only the tray icon.
        }
        catch (Exception ex)
        {
            _log.Write("Application Error", ex.GetType().Name);
            MessageBox.Show("Display Quick Control could not start. " + ex.Message, "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private async Task OpenPanelAsync()
    {
        if (_exiting || _panel is null) return;
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        try { await _panel.OpenAsync(); }
        catch (Exception ex) { _log?.Write("Panel Error", ex.GetType().Name); _viewModel.Message = ex.Message; }
    }
    private async Task RefreshFromTrayAsync()
    {
        await OpenPanelAsync();
        if (!_settings.Current.RefreshOnOpen || _settingsWindow is { IsVisible: true }) await _viewModel.RefreshAsync();
    }
    private async Task ApplyHotkeyAsync(DisplayMode mode)
    {
        if (_viewModel.IsBusy) return;
        await OpenPanelAsync(); await _viewModel.ApplyAsync(mode);
    }
    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        if (_viewModel.IsBusy) { _viewModel.Message = "Wait for the current display operation to finish before editing Settings."; _ = OpenPanelAsync(); return; }
        _panel?.Hide();
        var draft = _settings.Copy();
        try { draft.StartWithWindows = _startup.IsEnabled(); }
        catch (Exception ex) { _log?.Write("Startup", ex.GetType().Name); }
        var vm = new SettingsViewModel(draft, SaveSettings);
        _settingsWindow = new SettingsWindow(vm);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }
    private string SaveSettings(AppSettings settings)
    {
        if (_viewModel.IsBusy) throw new InvalidOperationException("Wait until the display operation finishes before saving Settings.");
        var oldStartup = _startup.IsEnabled();
        _startup.SetEnabled(settings.StartWithWindows);
        try { _settings.Save(settings); }
        catch { _startup.SetEnabled(oldStartup); throw; }
        _theme.Apply(settings.Theme);
        var warning = _hotkeys!.Apply(settings);
        _viewModel.Message = string.IsNullOrEmpty(warning) ? "Settings saved. Ready to switch displays." : warning;
        return string.IsNullOrEmpty(warning) ? "" : "Settings saved, but some shortcuts are unavailable:\n" + warning;
    }
    private void ExitApplication()
    {
        if (_viewModel.IsBusy) { _viewModel.Message = "A display operation is in progress. Wait for it to finish before exiting."; _ = OpenPanelAsync(); return; }
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true; _displayDebounce.Stop(); _hotkeys?.Dispose(); _tray?.Dispose(); _instance.Dispose();
        _panel?.CloseForExit(); _settingsWindow?.Close();
        _log?.Write("Application Exit", "Stopped."); base.OnExit(e);
    }
}
