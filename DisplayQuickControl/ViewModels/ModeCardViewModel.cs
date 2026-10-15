using System.Windows.Input;
using DisplayQuickControl.Models;

namespace DisplayQuickControl.ViewModels;

public sealed class ModeCardViewModel : ObservableObject
{
    public DisplayMode Definition { get; }
    public string Name => Definition.Name;
    public string Hertz => Definition.Hertz.ToString("0.##");
    public string Details => $"{Definition.Width} × {Definition.Height}" + (Definition.UseDefaultColorSettings ? "" : $" · {Definition.Bits} 位");
    public string Kind => Definition.IsSafeFallback ? "安全备用" : Definition.Hdr ? "HDR" : "SDR";
    public string Icon => Definition.IsSafeFallback ? "\uE73E" : Definition.Hdr ? "\uE706" : "\uE708";
    public string Caption => IsApplying ? "正在应用…" : $"{(Definition.UseDefaultColorSettings ? "默认颜色" : Definition.ColorEncoding)} · {Kind}";
    private bool _active, _applying;
    public string SelectionStatus => IsActive ? "当前模式" : "";
    public bool IsActive { get => _active; private set { if (Set(ref _active, value)) Changed(nameof(SelectionStatus)); } }
    public bool IsApplying { get => _applying; private set { if (Set(ref _applying, value)) Changed(nameof(Caption)); } }
    public ICommand ApplyCommand { get; }
    public ModeCardViewModel(DisplayMode mode, Func<DisplayMode, Task> apply, Func<bool> canApply)
    {
        Definition = mode with { };
        ApplyCommand = new RelayCommand(async _ => await apply(Definition), _ => canApply());
    }
    public void Update(DisplayState? state, string loadingId) { IsActive = Definition.Matches(state); IsApplying = loadingId == Definition.Id; }
}
