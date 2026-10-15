namespace DisplayQuickControl.Models;

public sealed record DisplayState(string DevicePath, string FriendlyName, string AdapterName,
    uint Width, uint Height, double RefreshRate, bool? HdrEnabled, uint? BitsPerChannel,
    string ColorEncoding, bool IsPrimary)
{
    public string Label => $"{FriendlyName} · {Width} × {Height}";
    public string HdrText => HdrEnabled switch { true => "HDR Enabled", false => "HDR Disabled", _ => "HDR Unknown" };
    public string SignalText => $"{RefreshRate:0.##} Hz · {ColorEncoding} · {(BitsPerChannel is { } bits ? $"{bits} bit" : "Unknown bit depth")}";
}

public sealed record DisplayMode(string Id, string Name, int Hertz, bool Hdr, uint Bits)
{
    public static readonly DisplayMode Hdr144 = new("hdr", "HDR 144", 144, true, 10);
    public static readonly DisplayMode Sdr180 = new("sdr", "SDR 180", 180, false, 8);
    public static readonly DisplayMode Safe120 = new("safe", "HDR Safe", 120, true, 10);
    public bool IsActive(DisplayState? state) => state is not null && state.HdrEnabled == Hdr
        && double.IsFinite(state.RefreshRate) && Math.Abs(state.RefreshRate - Hertz) < 1;
    public bool Matches(DisplayState? state) => IsActive(state) && state!.Width == 2560 && state.Height == 1440
        && (state.BitsPerChannel is null || state.BitsPerChannel == Bits)
        && (state.ColorEncoding == "Unknown" || state.ColorEncoding == "RGB");
    public string Preset(AppSettings settings) => Id switch { "hdr" => settings.HdrPreset, "sdr" => settings.SdrPreset, _ => settings.SafePreset };
}
