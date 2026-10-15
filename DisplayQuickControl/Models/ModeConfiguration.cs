namespace DisplayQuickControl.Models;

public static class ModeConfiguration
{
    public static readonly string[] Encodings = ["RGB", "YCbCr 4:4:4", "YCbCr 4:2:2", "YCbCr 4:2:0"];
    public static readonly uint[] BitDepths = [6, 8, 10, 12, 16];
    public static void Validate(IReadOnlyList<DisplayMode> modes)
    {
        if (modes.Count is < 1 or > 32) throw new FormatException("请保留 1–32 个显示模式。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fallbacks = 0;
        foreach (var mode in modes)
        {
            if (mode is null || string.IsNullOrWhiteSpace(mode.Id) || !ids.Add(mode.Id)) throw new FormatException("模式标识缺失或重复，请重新添加该模式。");
            if (string.IsNullOrWhiteSpace(mode.Name) || mode.Name.Length > 80 || mode.Name.Any(char.IsControl) || !names.Add(mode.Name.Trim()))
                throw new FormatException("模式名称必须唯一，且为 1–80 个有效字符。");
            if (string.IsNullOrWhiteSpace(mode.PresetName) || mode.PresetName.Length > 200 || mode.PresetName.Any(char.IsControl))
                throw new FormatException($"{mode.Name}：请填写有效的 ColorControl 预设名称。");
            if (mode.Width is < 1 or > 32768 || mode.Height is < 1 or > 32768)
                throw new FormatException($"{mode.Name}：分辨率宽高需为 1–32768 的整数。");
            if (!double.IsFinite(mode.Hertz) || mode.Hertz is < 1 or > 1000)
                throw new FormatException($"{mode.Name}：刷新率需为 1–1000 Hz，可填写小数。");
            if (!mode.UseDefaultColorSettings && (!BitDepths.Contains(mode.Bits) || !Encodings.Contains(mode.ColorEncoding)))
                throw new FormatException($"{mode.Name}：请选择支持的色深和颜色编码。");
            if (mode.IsSafeFallback) fallbacks++;
        }
        if (fallbacks > 1) throw new FormatException("最多指定一个手动安全备用模式。");
    }
}
