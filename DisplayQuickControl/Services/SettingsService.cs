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
            Current = new(); LoadWarning = "Settings could not be read. Defaults are in use; save Settings to replace the damaged file.";
            log.Write("Settings", $"Load failed ({ex.GetType().Name}); defaults used.");
        }
    }
    public AppSettings Copy() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(Current))!;
    public void Save(AppSettings settings)
    {
        settings.Normalize(); ModeConfiguration.Validate(settings.Modes); Directory.CreateDirectory(root);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, _path, true); Current = settings;
    }
}
