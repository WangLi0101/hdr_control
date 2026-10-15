using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using DisplayQuickControl.Models;

namespace DisplayQuickControl.ViewModels;

public sealed class ModeDraftViewModel : ObservableObject
{
    private readonly string _id;
    private string _name, _preset, _width, _height, _hertz, _encoding, _hotkey;
    private bool _hdr, _safe, _defaultColors;
    private uint _bits;
    public string Name { get => _name; set => Set(ref _name, value); }
    public string PresetName { get => _preset; set => Set(ref _preset, value); }
    public string Width { get => _width; set => Set(ref _width, value); }
    public string Height { get => _height; set => Set(ref _height, value); }
    public string Hertz { get => _hertz; set => Set(ref _hertz, value); }
    public bool Hdr { get => _hdr; set => Set(ref _hdr, value); }
    public uint Bits { get => _bits; set => Set(ref _bits, value); }
    public string ColorEncoding { get => _encoding; set => Set(ref _encoding, value); }
    public bool UseDefaultColorSettings { get => _defaultColors; set { if (Set(ref _defaultColors, value)) Changed(nameof(UseCustomColorSettings)); } }
    public bool UseCustomColorSettings => !UseDefaultColorSettings;
    public string Hotkey { get => _hotkey; set => Set(ref _hotkey, value); }
    public bool IsSafeFallback { get => _safe; set => Set(ref _safe, value); }
    public ModeDraftViewModel(DisplayMode mode)
    {
        _id = mode.Id; _name = mode.Name; _preset = mode.PresetName;
        _width = mode.Width.ToString(CultureInfo.InvariantCulture); _height = mode.Height.ToString(CultureInfo.InvariantCulture);
        _hertz = mode.Hertz.ToString("0.#####", CultureInfo.InvariantCulture); _hdr = mode.Hdr; _bits = mode.Bits;
        _encoding = mode.ColorEncoding; _hotkey = mode.Hotkey; _safe = mode.IsSafeFallback;
        _defaultColors = mode.UseDefaultColorSettings;
    }
    public DisplayMode ToMode()
    {
        if (!uint.TryParse(Width, NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            || !uint.TryParse(Height, NumberStyles.None, CultureInfo.InvariantCulture, out var height))
            throw new FormatException($"{Name}：分辨率宽高必须是正整数。");
        if (!double.TryParse(Hertz, NumberStyles.Float, CultureInfo.InvariantCulture, out var hertz))
            throw new FormatException($"{Name}：刷新率必须是数字，小数请使用 . 分隔。");
        return new DisplayMode { Id = _id, Name = Name.Trim(), PresetName = PresetName.Trim(), Width = width, Height = height,
            Hertz = hertz, Hdr = Hdr, Bits = Bits, ColorEncoding = ColorEncoding, UseDefaultColorSettings = UseDefaultColorSettings, Hotkey = Hotkey.Trim(), IsSafeFallback = IsSafeFallback };
    }
}

public sealed class ModeEditorViewModel : ObservableObject
{
    public ObservableCollection<ModeDraftViewModel> Modes { get; } = [];
    public uint[] BitDepths => ModeConfiguration.BitDepths;
    public string[] Encodings => ModeConfiguration.Encodings;
    private ModeDraftViewModel? _selected;
    private string _status = "";
    public ModeDraftViewModel? Selected { get => _selected; set { if (Set(ref _selected, value)) { Changed(nameof(HasSelection)); CommandManager.InvalidateRequerySuggested(); } } }
    public bool HasSelection => Selected is not null;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public ICommand AddCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand DownCommand { get; }
    public ICommand SaveCommand { get; }
    public event Action? Saved;
    public ModeEditorViewModel(IEnumerable<DisplayMode> modes, Func<List<DisplayMode>, string> save)
    {
        foreach (var mode in modes) Add(new(mode));
        Selected = Modes.FirstOrDefault();
        AddCommand = new RelayCommand(_ =>
        {
            var number = 1;
            while (Modes.Any(m => m.Name == $"新模式 {number}")) number++;
            var draft = new ModeDraftViewModel(new() { Name = $"新模式 {number}" }); Add(draft); Selected = draft;
        }, _ => Modes.Count < 32);
        DeleteCommand = new RelayCommand(_ =>
        {
            var index = Modes.IndexOf(Selected!); var removed = Selected!;
            removed.PropertyChanged -= OnModeChanged; Modes.Remove(removed);
            Selected = Modes[Math.Min(index, Modes.Count - 1)];
        }, _ => Selected is not null && Modes.Count > 1);
        UpCommand = new RelayCommand(_ => Move(-1), _ => Selected is not null && Modes.IndexOf(Selected) > 0);
        DownCommand = new RelayCommand(_ => Move(1), _ => Selected is not null && Modes.IndexOf(Selected) < Modes.Count - 1);
        SaveCommand = new RelayCommand(_ =>
        {
            try
            {
                var values = Modes.Select(m => m.ToMode()).ToList(); ModeConfiguration.Validate(values);
                Status = save(values);
                if (Status.Length == 0) Saved?.Invoke();
            }
            catch (Exception ex) { Status = ex.Message; }
        });
    }
    private void Add(ModeDraftViewModel mode) { mode.PropertyChanged += OnModeChanged; Modes.Add(mode); }
    private void OnModeChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ModeDraftViewModel.IsSafeFallback) && sender is ModeDraftViewModel { IsSafeFallback: true } selected)
            foreach (var mode in Modes.Where(m => !ReferenceEquals(m, selected))) mode.IsSafeFallback = false;
    }
    private void Move(int offset)
    {
        var selected = Selected!; var index = Modes.IndexOf(selected);
        Modes.Move(index, index + offset); Selected = selected; CommandManager.InvalidateRequerySuggested();
    }
}
