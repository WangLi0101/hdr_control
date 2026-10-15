namespace DisplayQuickControl.Models;

public sealed record DisplayState(string DevicePath, string FriendlyName, string AdapterName,
    uint Width, uint Height, double RefreshRate, bool? HdrEnabled, uint? BitsPerChannel,
    string ColorEncoding, bool IsPrimary)
{
    // GET_ADVANCED_COLOR_INFO_2.wideColorUserEnabled; independent of the active HDR mode.
    public bool? AutoColorManagementEnabled { get; init; }
    public string AutoColorManagementText => AutoColorManagementEnabled switch
    { true => "自动颜色管理：开", false => "自动颜色管理：关", _ => "自动颜色管理：状态未知" };
    public string Label => $"{FriendlyName} · {Width} × {Height}";
    public string HdrText => HdrEnabled switch { true => "HDR 已开启", false => "HDR 已关闭", _ => "HDR 状态未知" };
    public string SignalText => $"{RefreshRate:0.##} Hz · {(ColorEncoding switch { "Unknown" => "未知颜色编码", "Intensity" => "灰度", _ => ColorEncoding })} · {(BitsPerChannel is { } bits ? $"{bits} 位" : "未知色深")}";
}

public sealed record DisplayMode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新模式";
    public string PresetName { get; set; } = "";
    public uint Width { get; set; } = 2560;
    public uint Height { get; set; } = 1440;
    public double Hertz { get; set; } = 144;
    public bool Hdr { get; set; } = true;
    public uint Bits { get; set; } = 10;
    public string ColorEncoding { get; set; } = "RGB";
    private bool? _useDefaultColorSettings;
    public bool UseDefaultColorSettings { get => _useDefaultColorSettings ?? false; set => _useDefaultColorSettings = value; }
    internal bool HasColorSettingsPreference => _useDefaultColorSettings.HasValue;
    public string Hotkey { get; set; } = "";
    public bool IsSafeFallback { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string Summary => $"{Width} × {Height} · {Hertz:0.##} Hz · {(Hdr ? "HDR" : "SDR")} · {(UseDefaultColorSettings ? "默认颜色" : $"{Bits} 位 · {ColorEncoding}")}";
    public static DisplayMode Hdr180 => new() { Id = "hdr", Name = "HDR 180", PresetName = "HDR-180", Hertz = 180, Bits = 8, Hotkey = "Ctrl+Alt+H" };
    public static DisplayMode Sdr180 => new() { Id = "sdr", Name = "SDR 180", PresetName = "SDR-180", Hertz = 180, Hdr = false, Bits = 8, UseDefaultColorSettings = true, Hotkey = "Ctrl+Alt+S" };
    public static DisplayMode Safe120 => new() { Id = "safe", Name = "HDR 安全备用", PresetName = "HDR-120", Hertz = 120, Hotkey = "Ctrl+Alt+J", IsSafeFallback = true };
    public static List<DisplayMode> CreateDefaults() => [Hdr180, Sdr180, Safe120];
    public bool IsActive(DisplayState? state) => state is not null && state.HdrEnabled == Hdr
        && state.Width == Width && state.Height == Height
        && double.IsFinite(state.RefreshRate) && Math.Abs(state.RefreshRate - Hertz) < 1;
    public bool Matches(DisplayState? state) => IsActive(state)
        && (UseDefaultColorSettings ||
            ((state!.BitsPerChannel is null || state.BitsPerChannel == Bits)
                && (state.ColorEncoding == "Unknown" || state.ColorEncoding == ColorEncoding)));
}
