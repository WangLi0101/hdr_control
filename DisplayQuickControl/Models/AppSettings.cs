namespace DisplayQuickControl.Models;

public sealed class AppSettings
{
    public string ColorControlPath { get; set; } = "";
    public string HdrPreset { get; set; } = "HDR-144";
    public string SdrPreset { get; set; } = "SDR-180";
    public string SafePreset { get; set; } = "HDR-120";
    public string TargetDisplayPath { get; set; } = "";
    public bool StartWithWindows { get; set; }
    public bool CloseOnDeactivate { get; set; } = true;
    public bool RefreshOnOpen { get; set; } = true;
    public bool HotkeysEnabled { get; set; } = true;
    public string HdrHotkey { get; set; } = "Ctrl+Alt+H";
    public string SdrHotkey { get; set; } = "Ctrl+Alt+S";
    public string SafeHotkey { get; set; } = "Ctrl+Alt+J";
    public string Theme { get; set; } = "System";

    public void Normalize()
    {
        ColorControlPath ??= ""; TargetDisplayPath ??= "";
        HdrPreset = Default(HdrPreset, "HDR-144"); SdrPreset = Default(SdrPreset, "SDR-180");
        SafePreset = Default(SafePreset, "HDR-120");
        HdrHotkey ??= ""; SdrHotkey ??= ""; SafeHotkey ??= "";
        if (Theme is not ("System" or "Dark" or "Light")) Theme = "System";
    }
    private static string Default(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
