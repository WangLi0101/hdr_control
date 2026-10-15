using System.Text.Json.Nodes;
using DisplayQuickControl.Models;
using DisplayQuickControl.Services;

internal static class PresetSyncChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var path = Path.Combine(root, "colorcontrol", "NvPresets.json");
        var target = new DisplayState(@"\\?\DISPLAY#HEC1066#test-instance#{guid}", "测试显示器", "NVIDIA", 2560, 1440, 180, false, 8, "RGB", true);
        var modes = DisplayMode.CreateDefaults();
        var result = ColorControlPresetService.SynchronizeFile(path, modes, target, false);
        var presets = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        var hdr = presets[0]!;
        check(result.Created == 3 && presets.Count == 3, "Initializer creates all three presets in an empty data directory");
        check(hdr["name"]!.GetValue<string>() == "HDR-180" && hdr["applyHDR"]!.GetValue<bool>() && hdr["HDREnabled"]!.GetValue<bool>() && !hdr["toggleHDR"]!.GetValue<bool>(), "HDR presets explicitly include HDR On instead of Toggle");
        check(hdr["colorData"]!["ColorDepth"]!.GetValue<int>() == 2 && presets[2]!["colorData"]!["ColorDepth"]!.GetValue<int>() == 3, "NVIDIA BPC enum encodes 8 bit as 2 and 10 bit as 3");
        var sdr = presets[1]!;
        check(sdr["applyColorData"]!.GetValue<bool>() && sdr["colorData"]!["ColorDepth"]!.GetValue<int>() == 0
            && sdr["colorData"]!["ColorFormat"]!.GetValue<int>() == 255 && sdr["colorData"]!["DynamicRange"]!.GetValue<int>() == 2
            && sdr["colorData"]!["SelectionPolicy"]!.GetValue<int>() == 1, "SDR preset actively applies NVIDIA default colors rather than leaving forced HDR colors unchanged");
        check(hdr["DisplayId"]!.GetValue<string>() == "HEC1066" && !hdr["primaryDisplay"]!.GetValue<bool>(), "Presets bind to the selected physical display identifier");
        check(hdr["DisplayConfig"]!["ApplyResolution"]!.GetValue<bool>() && hdr["DisplayConfig"]!["ApplyRefreshRate"]!.GetValue<bool>() && hdr["colorData"]!["DynamicRange"]!.GetValue<int>() == 0, "Initialization includes resolution, refresh rate and Full range");
        check(presets.Select(p => p!["id"]!.GetValue<int>()).Distinct().Count() == 3 && presets.All(p => p!["Triggers"]!.AsArray().Count == 0 && p["shortcut"]!.GetValue<string>() == ""), "Generated preset IDs are unique and do not add startup triggers or competing hotkeys");
        var original = File.ReadAllText(path);
        result = ColorControlPresetService.SynchronizeFile(path, modes, target, false);
        check(result.Created == 0 && result.Unchanged == 3 && File.ReadAllText(path) == original && result.BackupPath is null, "Repeated initialization is idempotent");
        modes[1].UseDefaultColorSettings = false;
        ColorControlPresetService.SynchronizeFile(path, modes, target, true);
        sdr = JsonNode.Parse(File.ReadAllText(path))![1]!;
        check(sdr["colorData"]!["ColorDepth"]!.GetValue<int>() == 2 && sdr["colorData"]!["SelectionPolicy"]!.GetValue<int>() == 0, "Custom SDR colors can be enabled again through preset synchronization");
        modes[1].UseDefaultColorSettings = true;
        ColorControlPresetService.SynchronizeFile(path, modes, target, true);
        sdr = JsonNode.Parse(File.ReadAllText(path))![1]!;
        check(sdr["id"]!.GetValue<int>() == presets[1]!["id"]!.GetValue<int>() && sdr["colorData"]!["SelectionPolicy"]!.GetValue<int>() == 1, "Changing an existing SDR preset back to defaults preserves its identity");
        modes[0].Bits = 12;
        ColorControlPresetService.SynchronizeFile(path, modes, target, false);
        check(File.ReadAllText(path) == original, "Initialization preserves existing same-name presets");
        // Simulate unrelated ColorControl settings on an existing preset and an unrelated preset.
        presets[0]!["driverSettings"] = new JsonObject { ["123"] = 456 };
        presets.Add(new JsonObject { ["id"] = -42, ["name"] = "Other app preset", ["UnknownField"] = "keep" });
        File.WriteAllText(path, presets.ToJsonString()); original = File.ReadAllText(path);
        var movie = new DisplayMode { Name = "电影", PresetName = "电影 4K", Width = 3840, Height = 2160, Hertz = 59.940059, Bits = 12, ColorEncoding = "YCbCr 4:4:4" };
        result = ColorControlPresetService.SynchronizeFile(path, [..modes, movie], target, true);
        presets = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        check(result.Created == 1 && result.Updated == 1 && presets.Count == 5, "Saving modes adds custom presets and updates existing display parameters");
        check(File.ReadAllText(result.BackupPath!) == original, "Updating an existing file preserves an exact backup");
        check(presets[0]!["driverSettings"]!["123"]!.GetValue<int>() == 456 && presets[3]!["UnknownField"]!.GetValue<string>() == "keep" && presets[0]!["id"]!.GetValue<int>() == hdr["id"]!.GetValue<int>(), "Sync preserves preset IDs, unrelated fields and unrelated presets");
        check(presets[4]!["colorData"]!["ColorFormat"]!.GetValue<int>() == 2 && presets[4]!["DisplayConfig"]!["RefreshRate"]!["Numerator"]!.GetValue<uint>() == 59940059, "Custom YCbCr 4:4:4 and fractional refresh rate use ColorControl's schema");
        original = File.ReadAllText(path);
        result = ColorControlPresetService.SynchronizeFile(path, [..modes, movie], target, true);
        check(result.Created + result.Updated == 0 && File.ReadAllText(path) == original, "Unchanged saves do not rewrite presets or produce redundant backups");
        modes[0].Bits = 8;
        try { ColorControlPresetService.SynchronizeFile(path, modes, target, true, () => throw new InvalidOperationException("ColorControl running")); throw new Exception("Guard not enforced"); }
        catch (InvalidOperationException) { check(File.ReadAllText(path) == original, "A running-process guard leaves the preset file untouched"); }
        try { ColorControlPresetService.SynchronizeFile(path, modes, target, true, () => File.WriteAllText(path, "[]")); throw new Exception("Concurrent write lost"); }
        catch (IOException) { check(File.ReadAllText(path) == "[]", "Concurrent ColorControl file edits are detected and preserved"); }
        File.WriteAllText(path, "{broken");
        try { ColorControlPresetService.SynchronizeFile(path, modes, target, true); throw new Exception("Corrupt file overwritten"); }
        catch (InvalidOperationException) { check(File.ReadAllText(path) == "{broken", "Malformed preset files are never overwritten"); }
        File.WriteAllText(path, "[]");
        try { ColorControlPresetService.SynchronizeFile(path, [modes[0], modes[1] with { PresetName = modes[0].PresetName }], target, true); throw new Exception("Ambiguous name accepted"); }
        catch (InvalidOperationException) { check(File.ReadAllText(path) == "[]", "Conflicting mode preset names cannot overwrite each other"); }
        check(!Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(), "Failed sync operations clean up temporary files");
    }
}
