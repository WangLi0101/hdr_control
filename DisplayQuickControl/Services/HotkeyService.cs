using System.Runtime.InteropServices;
using System.Windows.Input;
using DisplayQuickControl.Models;
using static DisplayQuickControl.Native.NativeMethods;

namespace DisplayQuickControl.Services;

public sealed class HotkeyService(nint handle) : IDisposable
{
    private readonly Dictionary<int, DisplayMode> _registered = [];
    public event Action<DisplayMode>? Invoked;
    public static (uint Modifiers, uint Key) Parse(string text)
    {
        uint modifiers = 0, key = 0;
        var tokens = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            switch (token.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": modifiers |= 2; break;
                case "ALT": modifiers |= 1; break;
                case "SHIFT": modifiers |= 4; break;
                case "WIN": modifiers |= 8; break;
                default:
                    if (key != 0 || !Enum.TryParse<Key>(token, true, out var parsed) || !Enum.IsDefined(parsed) || parsed == System.Windows.Input.Key.None)
                        throw new FormatException($"Invalid hotkey: {text}. Example: Ctrl+Alt+H; leave empty to disable.");
                    key = (uint)KeyInterop.VirtualKeyFromKey(parsed);
                    if (key is 0 or 16 or 17 or 18 or 91 or 92) throw new FormatException("Choose a letter, number (D0–D9), or function key.");
                    break;
            }
        }
        if (key == 0 || modifiers == 0) throw new FormatException("A hotkey needs a modifier and a key, for example Ctrl+Alt+H.");
        return (modifiers | 0x4000, key); // MOD_NOREPEAT
    }
    public static void Validate(AppSettings settings)
    {
        if (!settings.HotkeysEnabled) return;
        var used = new HashSet<(uint, uint)>();
        foreach (var text in new[] { settings.HdrHotkey, settings.SdrHotkey, settings.SafeHotkey }.Where(s => !string.IsNullOrWhiteSpace(s)))
            if (!used.Add(Parse(text))) throw new FormatException("Two modes cannot use the same hotkey.");
    }
    public string Apply(AppSettings settings)
    {
        Dispose();
        if (!settings.HotkeysEnabled) return "";
        var errors = new List<string>();
        var entries = new[] { (DisplayMode.Hdr144, settings.HdrHotkey), (DisplayMode.Sdr180, settings.SdrHotkey), (DisplayMode.Safe120, settings.SafeHotkey) };
        var id = 100;
        foreach (var (mode, text) in entries)
        {
            id++; if (string.IsNullOrWhiteSpace(text)) continue;
            try
            {
                var (modifiers, key) = Parse(text);
                if (RegisterHotKey(handle, id, modifiers, key)) _registered[id] = mode;
                else errors.Add($"{mode.Name}: {text} could not be registered (Windows error {Marshal.GetLastWin32Error()}). It may be in use.");
            }
            catch (FormatException ex) { errors.Add(ex.Message); }
        }
        return string.Join(Environment.NewLine, errors);
    }
    public void Handle(int id) { if (_registered.TryGetValue(id, out var mode)) Invoked?.Invoke(mode); }
    public void Dispose() { foreach (var id in _registered.Keys) UnregisterHotKey(handle, id); _registered.Clear(); }
}
