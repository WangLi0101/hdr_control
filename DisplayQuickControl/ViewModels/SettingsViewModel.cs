using System.IO;
using System.Windows.Input;
using DisplayQuickControl.Models;
using DisplayQuickControl.Services;
using Microsoft.Win32;

namespace DisplayQuickControl.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    public AppSettings Draft { get; }
    public string[] Themes { get; } = ["System", "Dark", "Light"];
    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public ICommand BrowseCommand { get; }
    public ICommand SaveCommand { get; }
    public event Action? Saved;
    public SettingsViewModel(AppSettings draft, Func<AppSettings, string> save)
    {
        Draft = draft;
        BrowseCommand = new RelayCommand(_ =>
        {
            var dialog = new OpenFileDialog { Title = "Select ColorControl.exe", Filter = "ColorControl executable|ColorControl.exe", CheckFileExists = true };
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
                    throw new InvalidOperationException("Select an existing ColorControl.exe, or leave the path empty to configure it later.");
                foreach (var preset in new[] { Draft.HdrPreset, Draft.SdrPreset, Draft.SafePreset })
                    if (string.IsNullOrWhiteSpace(preset) || preset.Any(char.IsControl)) throw new InvalidOperationException("Each preset needs a non-empty name without control characters.");
                HotkeyService.Validate(Draft);
                Status = save(Draft);
                if (Status.Length == 0) Saved?.Invoke();
            }
            catch (Exception ex) { Status = ex.Message; }
        });
    }
}
