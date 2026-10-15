using System.Runtime.InteropServices;
using System.Text.Json;
using DisplayQuickControl.Models;
using DisplayQuickControl.Services;
using static DisplayQuickControl.Native.NativeMethods;
using Box = DisplayQuickControl.Services.PanelPlacement.Box;

var passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    passed++; Console.WriteLine("PASS: " + description);
}
async Task Throws<T>(Func<Task> operation, string description) where T : Exception
{
    try { await operation(); }
    catch (T) { Check(true, description); return; }
    throw new Exception("FAIL: " + description);
}
var state = new DisplayState("test", "Monitor", "Adapter", 2560, 1440, 143.97, true, 10, "RGB", true);
Check(DisplayMode.Hdr144.Matches(state), "143.97 Hz HDR matches HDR 144");
Check(DisplayMode.Sdr180.Matches(state with { RefreshRate = 179.95, HdrEnabled = false, BitsPerChannel = 8 }), "179.95 Hz SDR matches SDR 180");
Check(DisplayMode.Safe120.Matches(state with { RefreshRate = 119.88 }), "119.88 Hz HDR matches Safe 120");
Check(!DisplayMode.Hdr144.IsActive(state with { HdrEnabled = null }), "Unknown HDR never activates a card");
Check(!DisplayMode.Hdr144.Matches(state with { BitsPerChannel = 8 }), "Known wrong bit depth fails verification");
Check(!DisplayMode.Hdr144.Matches(state with { ColorEncoding = "YCbCr 4:2:2" }), "Known wrong encoding fails verification");
Check(!DisplayMode.Hdr144.Matches(state with { Width = 1920 }), "Wrong resolution fails verification");
Check(!DisplayMode.Hdr144.IsActive(state with { RefreshRate = double.NaN }), "Invalid refresh cannot activate card");
Check(DisplayMode.Hdr144.Matches(state with { BitsPerChannel = null, ColorEncoding = "Unknown" }), "Unavailable optional fields do not invent values");
var customMode = new DisplayMode { Name = "电影 4K", PresetName = "Movie-4K", Width = 3840, Height = 2160, Hertz = 59.94,
    Hdr = false, Bits = 12, ColorEncoding = "YCbCr 4:4:4" };
var customState = state with { Width = 3840, Height = 2160, RefreshRate = 59.95, HdrEnabled = false, BitsPerChannel = 12, ColorEncoding = "YCbCr 4:4:4" };
Check(customMode.Matches(customState), "Custom resolution, fractional rate, SDR, bit depth and YCbCr are verified");
Check(!customMode.Matches(customState with { ColorEncoding = "RGB" }), "Custom encoding is not hardcoded to RGB");
Check(!customMode.IsActive(customState with { Width = 2560 }), "Different resolutions do not activate the same mode");
void RejectModes(List<DisplayMode> modes, string description)
{
    try { ModeConfiguration.Validate(modes); }
    catch (FormatException) { Check(true, description); return; }
    throw new Exception("FAIL: " + description);
}
ModeConfiguration.Validate([customMode]);
RejectModes([], "Empty mode lists cannot be saved");
RejectModes([customMode, customMode with { Name = "Duplicate" }], "Duplicate mode identities cannot be saved");
RejectModes([customMode, customMode with { Id = "other" }], "Duplicate mode names cannot be saved");
RejectModes([customMode with { PresetName = "" }], "Empty presets cannot be saved");
RejectModes([customMode with { Hertz = double.NaN }], "Non-finite refresh targets cannot be saved");
RejectModes([customMode with { Width = 0 }], "Invalid resolution cannot be saved");
RejectModes([customMode with { Bits = 9 }], "Unsupported bit depth cannot be saved");
RejectModes([customMode with { ColorEncoding = "Fake" }], "Unsupported encoding cannot be saved");
RejectModes([DisplayMode.Safe120, customMode with { IsSafeFallback = true }], "Only one explicit fallback can be saved");
Check(Marshal.SizeOf<DisplayPath>() == 72 && Marshal.SizeOf<ModeInfo>() == 64, "DisplayConfig native buffer layouts");
Check(Marshal.SizeOf<AdvancedColor>() == 32 && Marshal.SizeOf<AdvancedColor2>() == 36, "Legacy and new advanced color layouts");
Check(Marshal.SizeOf<TargetName>() == 420 && Marshal.SizeOf<SourceName>() == 84, "Native Unicode name layouts");
foreach (var scale in new[] { 1d, 1.25, 1.5, 1.75, 2 })
{
    var width = (int)(400*scale); var height = (int)(540*scale); var margin = (int)(12*scale);
    var monitor = new Box(-2560, 0, 0, 1440); var work = new Box(-2560, 0, 0, 1390);
    var (x, y) = PanelPlacement.Calculate(monitor, work, new(-60, 1390, -30, 1430), width, height, margin);
    Check(x >= work.Left && y >= work.Top && x+width <= work.Right && y+height <= work.Bottom, $"Negative-origin monitor placement at {scale*100}% DPI");
}
foreach (var work in new[] { new Box(50,0,1920,1080), new Box(0,50,1920,1080), new Box(0,0,1870,1080), new Box(0,0,1920,1030) })
{
    var (x,y) = PanelPlacement.Calculate(new(0,0,1920,1080), work, new(1870,1030,1900,1060), 400,540,12);
    Check(x >= work.Left && y >= work.Top && x+400 <= work.Right && y+540 <= work.Bottom, "Taskbar edge placement " + work);
}
var root = Path.Combine(Path.GetTempPath(), "dqc-tests-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    var log = new LogService(root); var settings = new SettingsService(root, log);
    File.WriteAllText(Path.Combine(root, "settings.json"), "{broken"); settings.Load();
    Check(settings.Current.Modes[0].PresetName == "HDR-144" && settings.LoadWarning != null, "Damaged JSON falls back to defaults");
    settings.Current.Modes[0].PresetName = "HDR test 中文"; settings.Save(settings.Current);
    var reload = new SettingsService(root, log); reload.Load();
    Check(reload.Current.Modes[0].PresetName == "HDR test 中文", "Atomic JSON save and Unicode round trip");
    var legacyRoot = Path.Combine(root, "legacy"); Directory.CreateDirectory(legacyRoot);
    var legacyPath = Path.Combine(legacyRoot, "settings.json");
    File.WriteAllText(legacyPath, """{"HdrPreset":"My HDR","SdrPreset":"My SDR","SafePreset":"Safe custom","HdrHotkey":"","SdrHotkey":"Ctrl+Shift+S","HotkeysEnabled":false}""");
    var legacy = new SettingsService(legacyRoot, log); legacy.Load();
    Check(legacy.Current.Modes.Count == 3 && legacy.Current.Modes[0].PresetName == "My HDR" && legacy.Current.Modes[2].PresetName == "Safe custom", "Old preset names migrate into editable modes");
    Check(legacy.Current.Modes[0].Hotkey == "" && legacy.Current.Modes[1].Hotkey == "Ctrl+Shift+S" && !legacy.Current.HotkeysEnabled, "Migration preserves disabled/custom shortcuts");
    legacy.Save(legacy.Current);
    Check(!File.ReadAllText(legacyPath).Contains("HdrPreset"), "New saves retire legacy fields");
    var draft = legacy.Copy(); draft.Modes[0].Name = "Unsaved edit"; draft.Modes.Add(customMode);
    Check(legacy.Current.Modes.Count == 3 && legacy.Current.Modes[0].Name != "Unsaved edit", "Editing drafts cannot mutate live configuration");
    draft.Modes.Reverse(); legacy.Save(draft); legacy.Load();
    Check(legacy.Current.Modes[0].Id == customMode.Id && legacy.Current.Modes[0].Hertz == 59.94 && legacy.Current.Modes.Count == 4, "Added modes, decimal rate and order survive restart");
    var withoutCustom = legacy.Copy(); withoutCustom.Modes.RemoveAt(0); legacy.Save(withoutCustom); legacy.Load();
    Check(legacy.Current.Modes.All(m => m.Id != customMode.Id), "Deleted modes stay deleted after restart");
    File.WriteAllText(legacyPath, """{"Modes":[{"Name":"Invalid","PresetName":"P","Width":0}]}"""); legacy.Load();
    Check(legacy.LoadWarning is not null && legacy.Current.Modes.Count == 3, "Invalid persisted mode data safely falls back to defaults");
    for (var i=0; i<12; i++) File.WriteAllText(Path.Combine(root,"Logs",$"old-{i}.log"),"old");
    log.Write("Test","rotation");
    Check(Directory.GetFiles(Path.Combine(root,"Logs"),"*.log").Length <= 7, "Log retention is bounded");
    var service = new ColorControlService(settings, log);
    await Throws<FileNotFoundException>(() => service.ApplyPresetAsync("test"), "Missing ColorControl produces explicit error");
    if (args.Length == 0) throw new Exception("Pass the compiled ColorControl fixture executable path.");
    settings.Current.ColorControlPath = Path.GetFullPath(args[0]);
    var marker = Path.Combine(root,"calls.txt"); Environment.SetEnvironmentVariable("DQC_TEST_MARKER",marker);
    var special = "HDR \"quoted\" 中文 & $test";
    await service.ApplyPresetAsync(special);
    var recorded = JsonSerializer.Deserialize<string[]>(File.ReadAllLines(marker)[0])!;
    Check(recorded.SequenceEqual(new[] { "--nvpreset", special, "--nogui" }), "Preset arguments preserve spaces/quotes/Unicode without a shell");
    await Throws<InvalidOperationException>(() => service.ApplyPresetAsync("fail"), "Nonzero process exit is reported");
    await Throws<InvalidOperationException>(() => service.ApplyPresetAsync("missing"), "Upstream failure text is detected despite exit zero");
    var slow = service.ApplyPresetAsync("slow");
    await Throws<InvalidOperationException>(() => service.ApplyPresetAsync("success"), "Concurrent command is rejected");
    await slow;
    var timeoutService = new ColorControlService(settings,log,TimeSpan.FromMilliseconds(150));
    await Throws<TimeoutException>(() => timeoutService.ApplyPresetAsync("slow"), "Hung command returns bounded timeout");
    await Throws<InvalidOperationException>(() => timeoutService.ApplyPresetAsync("success"), "Timeout keeps gate closed while process runs");
    await Task.Delay(1100);
    await service.ApplyPresetAsync("success");
    Check(File.ReadAllLines(marker).Length == 6, "Exactly one invocation per accepted action; no retry after error or timeout");
    await timeoutService.ApplyPresetAsync("success");
    Check(File.ReadAllLines(marker).Length == 7, "Gate reopens only after timed-out process exits");
    if (OperatingSystem.IsWindows())
    {
        // Read-only smoke test. CI virtual displays may not expose Advanced Color.
        try
        {
            var displays = await new DisplayService(log).ReadAsync();
            Check(displays.Count > 0, "Windows DisplayConfig returns active targets");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode is 5 or 50)
        { Console.WriteLine("SKIP: this Windows session has no accessible display console (" + ex.NativeErrorCode + ")."); }
    }
}
finally { Environment.SetEnvironmentVariable("DQC_TEST_MARKER", null); Directory.Delete(root,true); }
Console.WriteLine($"All {passed} assertions passed.");
