using System.Text.Json.Serialization;

namespace DisplayQuickControl.Models;

public sealed class AppSettings
{
    public string ColorControlPath { get; set; } = "";
    public string TargetDisplayPath { get; set; } = "";
    public bool StartWithWindows { get; set; }
    public bool CloseOnDeactivate { get; set; } = true;
    public bool RefreshOnOpen { get; set; } = true;
    public bool HotkeysEnabled { get; set; } = true;
    public bool SyncColorControlPresets { get; set; } = true;
    public string Theme { get; set; } = "System";
    private List<DisplayMode>? _modes;
    public List<DisplayMode> Modes { get => _modes ??= MigrateLegacyModes(); set => _modes = value; }

    // Read old settings.json without losing preset names or shortcuts. Omitted after migration.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? HdrPreset { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SdrPreset { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SafePreset { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? HdrHotkey { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SdrHotkey { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SafeHotkey { get; set; }

    private List<DisplayMode> MigrateLegacyModes()
    {
        var modes = DisplayMode.CreateDefaults();
        modes[0].PresetName = Default(HdrPreset == "HDR-144" ? null : HdrPreset, modes[0].PresetName);
        modes[1].PresetName = Default(SdrPreset, modes[1].PresetName);
        modes[2].PresetName = Default(SafePreset, modes[2].PresetName);
        modes[0].Hotkey = HdrHotkey ?? modes[0].Hotkey;
        modes[1].Hotkey = SdrHotkey ?? modes[1].Hotkey;
        modes[2].Hotkey = SafeHotkey ?? modes[2].Hotkey;
        return modes;
    }

    public void Normalize()
    {
        ColorControlPath ??= ""; TargetDisplayPath ??= "";
        var modes = Modes;
        HdrPreset = SdrPreset = SafePreset = HdrHotkey = SdrHotkey = SafeHotkey = null;
        foreach (var mode in modes)
        {
            if (mode is null) continue;
            mode.Name = (mode.Name ?? "").Trim(); mode.PresetName = (mode.PresetName ?? "").Trim();
            // Upgrade only the original HDR default; preserve edited modes and presets.
            if (mode.Id == "hdr" && mode.Name == "HDR 144" && mode.PresetName == "HDR-144"
                && mode.Width == 2560 && mode.Height == 1440 && mode.Hertz == 144
                && mode.Hdr && mode.Bits == 10 && mode.ColorEncoding == "RGB" && !mode.IsSafeFallback)
            {
                mode.Name = "HDR 180"; mode.PresetName = "HDR-180"; mode.Hertz = 180; mode.Bits = 8;
            }
            if (mode.Id == "safe" && mode.Name == "HDR Safe") mode.Name = "HDR 安全备用";
            // Migrate the old SDR default once; an explicit user choice of fixed colors is preserved.
            if (!mode.HasColorSettingsPreference && mode.Id == "sdr" && mode.Name == "SDR 180" && mode.PresetName == "SDR-180"
                && mode.Width == 2560 && mode.Height == 1440 && mode.Hertz == 180 && !mode.Hdr
                && mode.Bits == 8 && mode.ColorEncoding == "RGB" && !mode.IsSafeFallback)
                mode.UseDefaultColorSettings = true;
            mode.Hotkey = (mode.Hotkey ?? "").Trim(); mode.ColorEncoding ??= "RGB";
        }
        if (Theme is not ("System" or "Dark" or "Light")) Theme = "System";
    }
    private static string Default(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
