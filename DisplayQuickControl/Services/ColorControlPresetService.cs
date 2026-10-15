using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using DisplayQuickControl.Models;

namespace DisplayQuickControl.Services;

// ColorControl 11.0.0.0: NvPreset, DisplayConfig, GenerateColorData and CCD.GetDisplayIdByPath.
// Writes preset definitions only. Never launches ColorControl or applies display settings.
public sealed class ColorControlPresetService
{
    public sealed record SyncResult(int Created, int Updated, int Unchanged, string FilePath, string? BackupPath);

    public SyncResult Synchronize(string executable, IReadOnlyList<DisplayMode> modes, DisplayState target,
        bool updateExisting)
    {
        executable = executable.Trim().Trim('"');
        if (!File.Exists(executable) || !string.Equals(Path.GetFileName(executable), "ColorControl.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("请先选择有效的 ColorControl.exe，再初始化或同步预设。");
        if (FileVersionInfo.GetVersionInfo(executable).FileMajorPart != 11)
            throw new InvalidOperationException("预设同步目前支持 ColorControl 11，请使用此版本或关闭自动同步后手动管理预设。");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Maassoft", "ColorControl");
        return SynchronizeFile(Path.Combine(root, "NvPresets.json"), modes, target, updateExisting, EnsureStopped);
    }

    public static SyncResult SynchronizeFile(string path, IReadOnlyList<DisplayMode> modes, DisplayState target,
        bool updateExisting, Action? beforeCommit = null)
    {
        ModeConfiguration.Validate(modes);
        if (modes.GroupBy(m => m.PresetName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidOperationException("同步时每个模式需使用不同的 ColorControl 预设名称。");
        var parts = target.DevicePath.Split('#');
        if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[1]))
            throw new InvalidOperationException("无法获取 ColorControl 所需的显示器标识，请重新选择显示器。");
        var displayId = parts[1];
        var original = File.Exists(path) ? File.ReadAllText(path) : null;
        JsonArray presets;
        try
        {
            presets = original is null ? [] : JsonNode.Parse(original, documentOptions: new JsonDocumentOptions
            { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonArray
                ?? throw new JsonException();
            if (presets.Any(p => p is not JsonObject)) throw new JsonException();
        }
        catch (JsonException ex)
        { throw new InvalidOperationException("ColorControl 预设文件格式异常，未修改文件。请先在 ColorControl 中修复或恢复备份。", ex); }

        var created = 0; var updated = 0; var unchanged = 0;
        var ids = presets.Select(p => p!["id"]?.GetValue<int>() ?? 0).ToHashSet();
        foreach (var mode in modes)
        {
            var matches = presets.OfType<JsonObject>().Where(p => string.Equals(p["name"]?.GetValue<string>(), mode.PresetName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException($"ColorControl 中有多个同名预设“{mode.PresetName}”，请先重命名后再同步。");
            var existing = matches.SingleOrDefault();
            if (existing is not null && !updateExisting) { unchanged++; continue; }
            var preset = existing?.DeepClone().AsObject() ?? new JsonObject();
            if (existing is null)
            {
                int id; do { id = Random.Shared.Next(1, int.MaxValue); } while (!ids.Add(id));
                preset["id"] = id; preset["name"] = mode.PresetName;
                preset["shortcut"] = ""; // Global shortcuts remain owned by this app.
                preset["Triggers"] = new JsonArray(); preset["Steps"] = new JsonArray();
            }
            preset["primaryDisplay"] = false; preset["DisplayId"] = displayId;
            preset["displayName"] = $"{target.FriendlyName} ({displayId})";
            preset["applyHDR"] = true; preset["HDREnabled"] = mode.Hdr; preset["toggleHDR"] = false;
            preset["applyColorData"] = true;
            var color = preset["colorData"] as JsonObject ?? new JsonObject();
            color["ColorFormat"] = mode.UseDefaultColorSettings ? 255 : mode.ColorEncoding switch { "RGB" => 0, "YCbCr 4:2:2" => 1, "YCbCr 4:4:4" => 2, "YCbCr 4:2:0" => 3, _ => throw new FormatException("不支持的颜色编码。") };
            // NVIDIA's BPC enum is ordinal: BPC6=1, BPC8=2, BPC10=3, BPC12=4, BPC16=5.
            color["ColorDepth"] = mode.UseDefaultColorSettings ? 0 : Array.IndexOf(ModeConfiguration.BitDepths, mode.Bits) + 1;
            color["Colorimetry"] = 255;
            color["DynamicRange"] = mode.UseDefaultColorSettings ? 2 : 0;
            color["SelectionPolicy"] = mode.UseDefaultColorSettings ? 1 : 0;
            // Keep Include enabled: default colors must actively reset a previously forced HDR color policy.
            if (color.Parent is null) preset["colorData"] = color;
            var config = preset["DisplayConfig"] as JsonObject ?? new JsonObject();
            config["ApplyResolution"] = true; config["ApplyRefreshRate"] = true;
            config["Resolution"] = new JsonObject { ["ActiveWidth"] = mode.Width, ["ActiveHeight"] = mode.Height, ["VirtualWidth"] = mode.Width, ["VirtualHeight"] = mode.Height };
            config["RefreshRate"] = new JsonObject { ["Numerator"] = (uint)Math.Round(mode.Hertz * 1_000_000, MidpointRounding.AwayFromZero), ["Denominator"] = 1_000_000 };
            // Identity rotation/scaling, without changing the primary display or connectivity.
            config["Scaling"] = 1; config["Rotation"] = 1;
            config["IsPrimary"] = null; config["Connectivity"] = 0;
            if (config.Parent is null) preset["DisplayConfig"] = config;
            if (existing is null) { presets.Add(preset); created++; }
            else if (JsonNode.DeepEquals(existing, preset)) unchanged++;
            else { presets[presets.IndexOf(existing)] = preset; updated++; }
        }
        if (created + updated == 0) return new(0, 0, unchanged, path, null);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string? backup = null;
        try
        {
            File.WriteAllText(temp, presets.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            beforeCommit?.Invoke();
            if ((File.Exists(path) ? File.ReadAllText(path) : null) != original)
                throw new IOException("ColorControl 预设文件已发生变化，未覆盖文件。请退出 ColorControl 后重试。");
            if (original is null) File.Move(temp, path); // Do not overwrite a concurrently created file.
            else
            {
                backup = path + ".dqc-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".backup";
                File.Replace(temp, path, backup);
            }
            return new(created, updated, unchanged, path, backup);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static void EnsureStopped()
    {
        foreach (var name in new[] { "ColorControl", "ColorControl.UI" })
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                if (processes.Length > 0) throw new InvalidOperationException("请先从托盘退出 ColorControl，再初始化或保存模式，避免旧预设覆盖同步结果。");
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }
}
