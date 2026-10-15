using System.IO;
using System.Windows.Input;
using DisplayQuickControl.Models;
using DisplayQuickControl.Services;
using Microsoft.Win32;

namespace DisplayQuickControl.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    public AppSettings Draft { get; }
    public sealed record ThemeOption(string Value, string Label);
    public ThemeOption[] Themes { get; } = [new("System", "跟随系统"), new("Dark", "深色"), new("Light", "浅色")];
    public IReadOnlyList<DisplayMode> Modes => Draft.Modes;
    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public ICommand BrowseCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand InitializePresetsCommand { get; }
    public event Action? Saved;
    public SettingsViewModel(AppSettings draft, Func<AppSettings, string> save, Func<AppSettings, string>? initializePresets = null)
    {
        Draft = draft;
        InitializePresetsCommand = new RelayCommand(_ =>
        {
            try { Status = initializePresets!(Draft); }
            catch (Exception ex) { Status = ex.Message; }
        }, _ => initializePresets is not null);
        BrowseCommand = new RelayCommand(_ =>
        {
            var dialog = new OpenFileDialog { Title = "选择 ColorControl.exe", Filter = "ColorControl 程序|ColorControl.exe", CheckFileExists = true };
            if (dialog.ShowDialog() != true) return;
            Draft.ColorControlPath = dialog.FileName; Changed(nameof(Draft));
        });
        SaveCommand = new RelayCommand(_ =>
        {
            try
            {
                Draft.ColorControlPath = Draft.ColorControlPath.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(Draft.ColorControlPath) && (!File.Exists(Draft.ColorControlPath)
                    || !string.Equals(Path.GetFileName(Draft.ColorControlPath), "ColorControl.exe", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("请选择有效的 ColorControl.exe，也可留空并稍后配置。");
                Draft.Normalize(); ModeConfiguration.Validate(Draft.Modes);
                HotkeyService.Validate(Draft);
                Status = save(Draft);
                if (Status.Length == 0) Saved?.Invoke();
            }
            catch (Exception ex) { Status = ex.Message; }
        });
    }
    public void ReplaceModes(List<DisplayMode> modes)
    {
        var validation = new AppSettings { HotkeysEnabled = Draft.HotkeysEnabled, Modes = modes };
        ModeConfiguration.Validate(modes); HotkeyService.Validate(validation);
        Draft.Modes = modes; Changed(nameof(Modes));
    }
}
