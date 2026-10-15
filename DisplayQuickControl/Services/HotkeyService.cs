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
                        throw new FormatException($"快捷键无效：{text}。示例：Ctrl+Alt+H；留空可禁用。");
                    key = (uint)KeyInterop.VirtualKeyFromKey(parsed);
                    if (key is 0 or 16 or 17 or 18 or 91 or 92) throw new FormatException("请选择字母键、数字键（D0–D9）或功能键。");
                    break;
            }
        }
        if (key == 0 || modifiers == 0) throw new FormatException("快捷键需包含修饰键和普通按键，例如 Ctrl+Alt+H。");
        return (modifiers | 0x4000, key); // MOD_NOREPEAT
    }
    public static void Validate(AppSettings settings)
    {
        if (!settings.HotkeysEnabled) return;
        var used = new HashSet<(uint, uint)>();
        foreach (var text in settings.Modes.Select(m => m.Hotkey).Where(s => !string.IsNullOrWhiteSpace(s)))
            if (!used.Add(Parse(text))) throw new FormatException("两个模式不能使用相同的快捷键。");
    }
    public string Apply(AppSettings settings)
    {
        Dispose();
        if (!settings.HotkeysEnabled) return "";
        var errors = new List<string>();
        var id = 100;
        foreach (var mode in settings.Modes)
        {
            var text = mode.Hotkey;
            id++; if (string.IsNullOrWhiteSpace(text)) continue;
            try
            {
                var (modifiers, key) = Parse(text);
                if (RegisterHotKey(handle, id, modifiers, key)) _registered[id] = mode;
                else errors.Add($"{mode.Name}：无法注册 {text}（Windows 错误 {Marshal.GetLastWin32Error()}），可能已被占用。");
            }
            catch (FormatException ex) { errors.Add(ex.Message); }
        }
        return string.Join(Environment.NewLine, errors);
    }
    public void Handle(int id) { if (_registered.TryGetValue(id, out var mode)) Invoked?.Invoke(mode); }
    public void Dispose() { foreach (var id in _registered.Keys) UnregisterHotKey(handle, id); _registered.Clear(); }
}
