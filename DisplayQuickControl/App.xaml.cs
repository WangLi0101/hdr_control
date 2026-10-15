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
    private ModeEditorWindow? _modeWindow;
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
            catch (IOException ex) { MessageBox.Show(ex.Message, "显示快捷控制", MessageBoxButton.OK, MessageBoxImage.Warning); }
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
            _panel.CustomizeModesRequested += OpenModeEditor;
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
                MessageBox.Show(warning, "显示快捷控制 · 快捷键冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            _ = _instance.ListenAsync(() => Dispatcher.BeginInvoke(() => _ = OpenPanelAsync()), _log);
            // No StartupUri or Show(): normal startup creates only the tray icon.
        }
        catch (Exception ex)
        {
            _log.Write("Application Error", ex.GetType().Name);
            MessageBox.Show("显示快捷控制无法启动。" + ex.Message, "启动错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private async Task OpenPanelAsync()
    {
        if (_exiting || _panel is null) return;
        if (_modeWindow is { IsVisible: true }) { _modeWindow.Activate(); return; }
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
        if (_viewModel.IsBusy || _settingsWindow is { IsVisible: true } || _modeWindow is { IsVisible: true }) return;
        await OpenPanelAsync(); await _viewModel.ApplyAsync(mode);
    }
    private void OpenSettings()
    {
        if (_modeWindow is { IsVisible: true }) { _modeWindow.Activate(); return; }
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        if (_viewModel.IsBusy) { _viewModel.Message = "请等待当前显示操作完成后再编辑设置。"; _ = OpenPanelAsync(); return; }
        _panel?.Hide();
        var draft = _settings.Copy();
        try { draft.StartWithWindows = _startup.IsEnabled(); }
        catch (Exception ex) { _log?.Write("Startup", ex.GetType().Name); }
        var vm = new SettingsViewModel(draft, SaveSettings, InitializePresets);
        _settingsWindow = new SettingsWindow(vm);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }
    private void OpenModeEditor()
    {
        if (_modeWindow is { IsVisible: true }) { _modeWindow.Activate(); return; }
        if (_viewModel.IsBusy) return;
        _panel?.Hide();
        var editor = new ModeEditorViewModel(_settings.Current.Modes, modes =>
        {
            if (_viewModel.IsBusy) throw new InvalidOperationException("请等待模式切换完成后保存。");
            var updated = _settings.Copy(); updated.Modes = modes;
            HotkeyService.Validate(updated);
            _settings.SaveWithSync(updated, SyncPresetsIfEnabled);
            return ApplyConfiguration();
        });
        _modeWindow = new ModeEditorWindow(editor) { WindowStartupLocation = WindowStartupLocation.CenterScreen };
        _modeWindow.Closed += (_, _) => { _modeWindow = null; _ = OpenPanelAsync(); };
        _modeWindow.Show();
    }
    private string SaveSettings(AppSettings settings)
    {
        if (_viewModel.IsBusy) throw new InvalidOperationException("请等待显示操作完成后再保存设置。");
        var oldStartup = _startup.IsEnabled();
        _startup.SetEnabled(settings.StartWithWindows);
        try { _settings.SaveWithSync(settings, SyncPresetsIfEnabled); }
        catch { _startup.SetEnabled(oldStartup); throw; }
        return ApplyConfiguration();
    }
    private string ApplyConfiguration()
    {
        _theme.Apply(_settings.Current.Theme); _viewModel.ReloadModes();
        var warning = _hotkeys!.Apply(_settings.Current);
        _viewModel.Message = string.IsNullOrEmpty(warning) ? "设置已保存，可以切换显示模式。" : warning;
        return string.IsNullOrEmpty(warning) ? "" : "设置已保存，但部分快捷键不可用：\n" + warning;
    }
    private void SyncPresetsIfEnabled(AppSettings settings)
    {
        if (!settings.SyncColorControlPresets || string.IsNullOrWhiteSpace(settings.ColorControlPath)) return;
        SynchronizePresets(settings, updateExisting: true);
    }
    private string InitializePresets(AppSettings settings)
    {
        var result = SynchronizePresets(settings, updateExisting: false);
        return $"初始化完成：新增 {result.Created} 个，保留 {result.Unchanged} 个已有预设。请保存设置后再点击模式卡片切换。";
    }
    private ColorControlPresetService.SyncResult SynchronizePresets(AppSettings settings, bool updateExisting)
    {
        if (_viewModel.IsBusy) throw new InvalidOperationException("请等待显示模式切换完成后同步预设。");
        ModeConfiguration.Validate(settings.Modes);
        // DisplayService reads on a worker task; no display operation is performed here.
        var displays = new DisplayService(_log!).ReadAsync().GetAwaiter().GetResult();
        var target = string.IsNullOrWhiteSpace(settings.TargetDisplayPath)
            ? displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault()
            : displays.FirstOrDefault(d => string.Equals(d.DevicePath, settings.TargetDisplayPath, StringComparison.OrdinalIgnoreCase));
        if (target is null) throw new InvalidOperationException("目标显示器不可用，请在主面板重新选择显示器。");
        var displayId = target.DevicePath.Split('#').ElementAtOrDefault(1);
        if (displays.Count(d => string.Equals(d.DevicePath.Split('#').ElementAtOrDefault(1), displayId, StringComparison.OrdinalIgnoreCase)) != 1)
            throw new InvalidOperationException("ColorControl 无法区分当前同型号显示器，请关闭自动同步并在 ColorControl 中手动绑定预设。");
        var result = new ColorControlPresetService().Synchronize(settings.ColorControlPath, settings.Modes, target, updateExisting);
        _log!.Write("Preset Sync", $"Created={result.Created}; Updated={result.Updated}; Unchanged={result.Unchanged}.");
        return result;
    }
    private void ExitApplication()
    {
        if (_viewModel.IsBusy) { _viewModel.Message = "正在执行显示操作，请等待完成后再退出。"; _ = OpenPanelAsync(); return; }
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true; _displayDebounce.Stop(); _hotkeys?.Dispose(); _tray?.Dispose(); _instance.Dispose();
        _panel?.CloseForExit(); _settingsWindow?.Close(); _modeWindow?.Close();
        _log?.Write("Application Exit", "Stopped."); base.OnExit(e);
    }
}
