using System.IO;
using System.Text.Json;
using DisplayQuickControl.Models;

namespace DisplayQuickControl.Services;

public sealed class SettingsService(string root, LogService log)
{
    private readonly string _path = Path.Combine(root, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public AppSettings Current { get; private set; } = new();
    public string? LoadWarning { get; private set; }
    public void Load()
    {
        LoadWarning = null;
        try
        {
            if (File.Exists(_path)) Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new();
            Current.Normalize();
            ModeConfiguration.Validate(Current.Modes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            Current = new(); LoadWarning = "无法读取设置，已使用默认配置。保存设置可替换损坏的配置文件。";
            log.Write("Settings", $"Load failed ({ex.GetType().Name}); defaults used.");
        }
    }
    public AppSettings Copy() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(Current))!;
    public void SaveWithSync(AppSettings settings, Action<AppSettings> synchronize)
    {
        var previous = Copy();
        Save(settings);
        try { synchronize(settings); }
        catch (Exception syncError)
        {
            try { Save(previous); }
            catch (Exception rollbackError)
            {
                throw new InvalidOperationException("ColorControl 同步失败，且无法恢复本工具原有设置。请检查配置目录的写入权限。", new AggregateException(syncError, rollbackError));
            }
            throw;
        }
    }
    public void Save(AppSettings settings)
    {
        settings.Normalize(); ModeConfiguration.Validate(settings.Modes); Directory.CreateDirectory(root);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, _path, true); Current = settings;
    }
}
