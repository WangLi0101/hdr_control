using System.Collections.ObjectModel;
using System.Windows.Input;
using DisplayQuickControl.Models;
using DisplayQuickControl.Services;

namespace DisplayQuickControl.ViewModels;

public sealed class ControlPanelViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly DisplayService _display;
    private readonly ColorControlService _color;
    private readonly LogService _log;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _busy, _refreshing, _selecting, _showSafe;
    private string _message = "请先在“设置”中选择 ColorControl 程序。", _loading = "";
    private DisplayState? _state;
    private DisplayState? _selectedDisplay;
    public ObservableCollection<DisplayState> Displays { get; } = [];
    public ObservableCollection<ModeCardViewModel> Modes { get; } = [];
    public DisplayState? SelectedDisplay
    {
        get => _selectedDisplay;
        set
        {
            if (!Set(ref _selectedDisplay, value) || _selecting || value is null) return;
            _settings.Current.TargetDisplayPath = value.DevicePath;
            try { _settings.Save(_settings.Current); } catch (Exception) { Message = "已为本次运行选择显示器，但无法保存设置。"; }
            State = value;
        }
    }
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) { Changed(nameof(CanInteract)); Changed(nameof(CanRefresh)); CommandManager.InvalidateRequerySuggested(); } } }
    public bool IsRefreshing { get => _refreshing; private set { if (Set(ref _refreshing, value)) { Changed(nameof(CanRefresh)); CommandManager.InvalidateRequerySuggested(); } } }
    public bool CanInteract => !IsBusy;
    public bool CanRefresh => !IsBusy && !IsRefreshing;
    public string Message { get => _message; set => Set(ref _message, value); }
    public bool ShowSafeSuggestion { get => _showSafe; private set => Set(ref _showSafe, value); }
    public string LoadingMode { get => _loading; private set { Set(ref _loading, value); UpdateCards(); } }
    private DisplayMode? Fallback => _settings.Current.Modes.FirstOrDefault(m => m.IsSafeFallback);
    public string FallbackMessage => $"所选 HDR 模式可能不稳定，可尝试手动切换到“{Fallback?.Name}”。";
    public string FallbackButtonText => $"切换到 {Fallback?.Name}";
    public DisplayState? State
    {
        get => _state;
        private set
        {
            Set(ref _state, value);
            foreach (var property in new[] { nameof(MonitorName), nameof(Resolution), nameof(Adapter), nameof(HdrText), nameof(Signal), nameof(AutoColorManagementText) }) Changed(property);
            UpdateCards();
        }
    }
    public string MonitorName => State?.FriendlyName ?? "显示器不可用";
    public string Resolution => State is { } s ? $"{s.Width} × {s.Height}" : "未知分辨率";
    public string Adapter => State?.AdapterName ?? "未知显卡";
    public string HdrText => State?.HdrText ?? "HDR 状态未知";
    public string Signal => State?.SignalText ?? "请刷新以读取当前显示信号";
    public string AutoColorManagementText => State?.AutoColorManagementText ?? "自动颜色管理：状态未知";
    public string CurrentModeName
    {
        get
        {
            if (State is null) return "未读取（请刷新状态）";
            if (State.HdrEnabled is null) return "模式未知（HDR 状态不可用）";
            var names = Modes.Where(card => card.IsActive).Select(card => card.Name).ToArray();
            if (names.Length == 0) return "自定义模式（未匹配预设）";
            return string.Join(" / ", names) +
                (State.BitsPerChannel is null || State.ColorEncoding == "Unknown" ? "（部分色彩参数未知）" : "");
        }
    }
    public ICommand SafeCommand { get; }
    public ICommand RefreshCommand { get; }
    public ControlPanelViewModel(SettingsService settings, DisplayService display, ColorControlService color, LogService log)
    {
        _settings = settings; _display = display; _color = color; _log = log;
        if (!string.IsNullOrWhiteSpace(settings.Current.ColorControlPath))
            _message = "显示状态读取自 Windows。选择模式即可应用对应的 ColorControl 预设。";
        SafeCommand = new RelayCommand(async _ => { if (Fallback is { } mode) await ApplyAsync(mode); }, _ => CanInteract && Fallback is not null);
        RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => CanRefresh);
        ReloadModes();
    }
    public void ReloadModes()
    {
        Modes.Clear();
        foreach (var mode in _settings.Current.Modes) Modes.Add(new ModeCardViewModel(mode, ApplyAsync, () => CanInteract));
        ShowSafeSuggestion = false; Changed(nameof(FallbackMessage)); Changed(nameof(FallbackButtonText)); UpdateCards();
        CommandManager.InvalidateRequerySuggested();
    }
    private void UpdateCards()
    {
        foreach (var card in Modes) card.Update(State, LoadingMode);
        Changed(nameof(CurrentModeName));
    }
    private bool CanSuggestFallback(DisplayMode failed) => failed.Hdr && Fallback is { } fallback && fallback.Id != failed.Id;
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        try { await ReadStateAsync(); }
        catch (Exception ex) { State = null; Message = "无法读取显示状态。" + ex.Message; }
    }
    private async Task ReadStateAsync(string? pinnedTarget = null)
    {
        await _refreshGate.WaitAsync(); IsRefreshing = true;
        try
        {
            var displays = await _display.ReadAsync();
            var target = pinnedTarget ?? _settings.Current.TargetDisplayPath;
            var selected = string.IsNullOrEmpty(target)
                ? displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault()
                : displays.FirstOrDefault(d => string.Equals(d.DevicePath, target, StringComparison.OrdinalIgnoreCase));
            _selecting = true;
            try { Displays.Clear(); foreach (var d in displays) Displays.Add(d); SelectedDisplay = selected; }
            finally { _selecting = false; }
            State = selected;
            if (selected is null) throw new InvalidOperationException("所选显示器已断开或不可用。请选择已连接的显示器，然后刷新。");
            _log.Write("Display State", $"HDR={selected.HdrEnabled?.ToString() ?? "Unknown"}; {selected.Width}x{selected.Height}; {selected.RefreshRate:0.###}Hz; {selected.BitsPerChannel?.ToString() ?? "Unknown"}bpc; {selected.ColorEncoding}; ACM={selected.AutoColorManagementEnabled?.ToString() ?? "Unknown"}.");
        }
        finally { IsRefreshing = false; _refreshGate.Release(); }
    }
    public async Task ApplyAsync(DisplayMode mode)
    {
        if (IsBusy) return;
        var configured = _settings.Current.Modes.FirstOrDefault(m => m.Id == mode.Id);
        if (configured is null) { Message = "此模式已被删除。请重新打开面板并选择模式。"; return; }
        mode = configured with { }; // Stable snapshot throughout this single operation.
        IsBusy = true; LoadingMode = mode.Id; ShowSafeSuggestion = false;
        var target = State?.DevicePath ?? _settings.Current.TargetDisplayPath;
        try
        {
            // Pin the actual physical target before applying, including first use / reconnect.
            await ReadStateAsync(target);
            target = State!.DevicePath;
            _color.ValidatePreset(mode.PresetName);
            Message = $"正在应用 {mode.Name}…显示器可能短暂黑屏。";
            var transition = new DisplayTransitionService(_display, _color.ApplyPresetAsync, _log);
            await transition.ApplyAsync(mode, State!);
            await ReadStateAsync(target);
            if (mode.Matches(State))
                Message = (mode.UseDefaultColorSettings
                    ? $"已通过 Windows 验证 {mode.Name} 的分辨率、刷新率及 HDR 状态。默认颜色策略由 NVIDIA 管理，Windows 未验证该策略。"
                    : $"已通过 Windows 验证 {mode.Name}。" +
                    (State!.BitsPerChannel is null || State.ColorEncoding == "Unknown" ? "部分色彩信息无法获取；" : "") +
                    "Windows 未验证输出动态范围。") + " 自动颜色管理状态见面板；屏幕观感仍需实际确认。";
            else
            {
                Message = "预设已执行，但当前显示状态与目标配置不一致。";
                ShowSafeSuggestion = CanSuggestFallback(mode);
                _log.Write("ColorControl Error", "Post-apply state mismatch; no retry.");
            }
        }
        catch (Exception ex)
        {
            State = null; Message = ex.Message;
            // Keep an explicit user action available. No preset is ever reapplied automatically.
            ShowSafeSuggestion = CanSuggestFallback(mode) && ex is not System.IO.FileNotFoundException;
        }
        finally { LoadingMode = ""; IsBusy = false; }
    }
}
