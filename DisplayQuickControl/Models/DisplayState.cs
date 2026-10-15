namespace DisplayQuickControl.Models;

public sealed record DisplayState(string DevicePath, string FriendlyName, string AdapterName,
    uint Width, uint Height, double RefreshRate, bool? HdrEnabled, uint? BitsPerChannel,
    string ColorEncoding, bool IsPrimary)
{
    public string Label => $"{FriendlyName} · {Width} × {Height}";
    public string HdrText => HdrEnabled switch { true => "HDR Enabled", false => "HDR Disabled", _ => "HDR Unknown" };
    public string SignalText => $"{RefreshRate:0.##} Hz · {ColorEncoding} · {(BitsPerChannel is { } bits ? $"{bits} bit" : "Unknown bit depth")}";
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
    public string Hotkey { get; set; } = "";
    public bool IsSafeFallback { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string Summary => $"{Width} × {Height} · {Hertz:0.##} Hz · {(Hdr ? "HDR" : "SDR")} · {Bits} bit · {ColorEncoding}";
    public static DisplayMode Hdr144 => new() { Id = "hdr", Name = "HDR 144", PresetName = "HDR-144", Hotkey = "Ctrl+Alt+H" };
    public static DisplayMode Sdr180 => new() { Id = "sdr", Name = "SDR 180", PresetName = "SDR-180", Hertz = 180, Hdr = false, Bits = 8, Hotkey = "Ctrl+Alt+S" };
    public static DisplayMode Safe120 => new() { Id = "safe", Name = "HDR Safe", PresetName = "HDR-120", Hertz = 120, Hotkey = "Ctrl+Alt+J", IsSafeFallback = true };
    public static List<DisplayMode> CreateDefaults() => [Hdr144, Sdr180, Safe120];
    public bool IsActive(DisplayState? state) => state is not null && state.HdrEnabled == Hdr
        && state.Width == Width && state.Height == Height
        && double.IsFinite(state.RefreshRate) && Math.Abs(state.RefreshRate - Hertz) < 1;
    public bool Matches(DisplayState? state) => IsActive(state)
        && (state!.BitsPerChannel is null || state.BitsPerChannel == Bits)
        && (state.ColorEncoding == "Unknown" || state.ColorEncoding == ColorEncoding);
}
