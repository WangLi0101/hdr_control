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
    private string _message = "Select a ColorControl executable in Settings to get started.", _loading = "";
    private DisplayState? _state;
    private DisplayState? _selectedDisplay;
    public ObservableCollection<DisplayState> Displays { get; } = [];
    public DisplayState? SelectedDisplay
    {
        get => _selectedDisplay;
        set
        {
            if (!Set(ref _selectedDisplay, value) || _selecting || value is null) return;
            _settings.Current.TargetDisplayPath = value.DevicePath;
            try { _settings.Save(_settings.Current); } catch (Exception) { Message = "Display selected for this session; settings could not be saved."; }
            State = value;
        }
    }
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) { Changed(nameof(CanInteract)); Changed(nameof(CanRefresh)); CommandManager.InvalidateRequerySuggested(); } } }
    public bool IsRefreshing { get => _refreshing; private set { if (Set(ref _refreshing, value)) { Changed(nameof(CanRefresh)); CommandManager.InvalidateRequerySuggested(); } } }
    public bool CanInteract => !IsBusy;
    public bool CanRefresh => !IsBusy && !IsRefreshing;
    public string Message { get => _message; set => Set(ref _message, value); }
    public bool ShowSafeSuggestion { get => _showSafe; private set => Set(ref _showSafe, value); }
    public string LoadingMode { get => _loading; private set { Set(ref _loading, value); Changed(nameof(HdrCaption)); Changed(nameof(SdrCaption)); Changed(nameof(SafeCaption)); } }
    public string HdrCaption => LoadingMode == "hdr" ? "Applying…" : "144 Hz · 10 bit";
    public string SdrCaption => LoadingMode == "sdr" ? "Applying…" : "180 Hz · 8 bit";
    public string SafeCaption => LoadingMode == "safe" ? "Applying…" : "120 Hz · 10 bit · Reduced bandwidth";
    public DisplayState? State
    {
        get => _state;
        private set
        {
            Set(ref _state, value);
            foreach (var property in new[] { nameof(MonitorName), nameof(Resolution), nameof(Adapter), nameof(HdrText), nameof(Signal), nameof(HdrActive), nameof(SdrActive), nameof(SafeActive) }) Changed(property);
        }
    }
    public string MonitorName => State?.FriendlyName ?? "Display unavailable";
    public string Resolution => State is { } s ? $"{s.Width} × {s.Height}" : "Unknown resolution";
    public string Adapter => State?.AdapterName ?? "Unknown adapter";
    public string HdrText => State?.HdrText ?? "HDR Unknown";
    public string Signal => State?.SignalText ?? "Refresh to read the current display signal";
    public bool HdrActive => DisplayMode.Hdr144.IsActive(State);
    public bool SdrActive => DisplayMode.Sdr180.IsActive(State);
    public bool SafeActive => DisplayMode.Safe120.IsActive(State);
    public ICommand HdrCommand { get; }
    public ICommand SdrCommand { get; }
    public ICommand SafeCommand { get; }
    public ICommand RefreshCommand { get; }
    public ControlPanelViewModel(SettingsService settings, DisplayService display, ColorControlService color, LogService log)
    {
        _settings = settings; _display = display; _color = color; _log = log;
        if (!string.IsNullOrWhiteSpace(settings.Current.ColorControlPath))
            _message = "Display state is read from Windows. Choose a mode to apply its ColorControl preset.";
        HdrCommand = new RelayCommand(async _ => await ApplyAsync(DisplayMode.Hdr144), _ => CanInteract);
        SdrCommand = new RelayCommand(async _ => await ApplyAsync(DisplayMode.Sdr180), _ => CanInteract);
        SafeCommand = new RelayCommand(async _ => await ApplyAsync(DisplayMode.Safe120), _ => CanInteract);
        RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => CanRefresh);
    }
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        try { await ReadStateAsync(); }
        catch (Exception ex) { State = null; Message = "Display state unavailable. " + ex.Message; }
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
            if (selected is null) throw new InvalidOperationException("The selected monitor is disconnected or unavailable. Select an active monitor, then Refresh.");
            _log.Write("Display State", $"HDR={selected.HdrEnabled?.ToString() ?? "Unknown"}; {selected.Width}x{selected.Height}; {selected.RefreshRate:0.###}Hz; {selected.BitsPerChannel?.ToString() ?? "Unknown"}bpc; {selected.ColorEncoding}.");
        }
        finally { IsRefreshing = false; _refreshGate.Release(); }
    }
    public async Task ApplyAsync(DisplayMode mode)
    {
        if (IsBusy) return;
        IsBusy = true; LoadingMode = mode.Id; ShowSafeSuggestion = false;
        var target = State?.DevicePath ?? _settings.Current.TargetDisplayPath;
        try
        {
            // Pin the actual physical target before applying, including first use / reconnect.
            await ReadStateAsync(target);
            target = State!.DevicePath;
            Message = $"Applying {mode.Name}… The display may briefly go black.";
            await _color.ApplyPresetAsync(mode.Preset(_settings.Current));
            Message = "Waiting for the display link to stabilize…";
            await Task.Delay(1800);
            await ReadStateAsync(target);
            if (mode.Matches(State))
                Message = $"{mode.Name} verified from Windows. " +
                    (State!.BitsPerChannel is null || State.ColorEncoding == "Unknown" ? "Some color details are unavailable; " : "") +
                    "Output range is not verified by Windows.";
            else
            {
                Message = "Preset applied, but current display state does not match the expected configuration.";
                ShowSafeSuggestion = mode == DisplayMode.Hdr144;
                _log.Write("ColorControl Error", "Post-apply state mismatch; no retry.");
            }
        }
        catch (Exception ex)
        {
            State = null; Message = ex.Message;
            // Keep an explicit user action available. No preset is ever reapplied automatically.
            ShowSafeSuggestion = mode == DisplayMode.Hdr144 && ex is not System.IO.FileNotFoundException;
        }
        finally { LoadingMode = ""; IsBusy = false; }
    }
}
