using DisplayQuickControl.Models;

namespace DisplayQuickControl.Services;

// One preset invocation per action. Polling only observes; it never reapplies a preset.
public sealed class DisplayTransitionService(IDisplayControl display, Func<string, Task> applyPreset,
    LogService log, Func<TimeSpan, Task>? delay = null)
{
    private Task Delay(int milliseconds) => (delay ?? Task.Delay)(TimeSpan.FromMilliseconds(milliseconds));

    public async Task<DisplayState> ApplyAsync(DisplayMode mode, DisplayState before)
    {
        var target = before.DevicePath;
        if (!mode.Hdr && before.HdrEnabled == true)
        {
            if (await display.TryDisableHdrAsync(target))
            {
                // Apply NVIDIA's default SDR color policy only after HDR has actually exited.
                var exited = await ObserveAsync(target, s => s.HdrEnabled == false);
                if (!exited.Stable) throw new InvalidOperationException("HDR 尚未稳定关闭，未执行 SDR 颜色预设。请等待后重试。");
            }
            else log.Write("Display Transition", "Separate HDR API unavailable; using ColorControl compatibility path.");
        }

        await applyPreset(mode.PresetName);
        await Delay(1800);
        var current = await ReadTargetAsync(target);
        var acm = before.AutoColorManagementEnabled;
        if (acm is { } preference && current.AutoColorManagementEnabled is { } actual && preference != actual)
            await display.SetAutoColorManagementAsync(target, preference);

        var result = await ObserveAsync(target, s => mode.Matches(s)
            && (acm is null || s.AutoColorManagementEnabled == acm));
        if (!result.Stable)
            throw new InvalidOperationException("预设已执行，但显示信号或颜色管理状态尚未稳定匹配目标。请刷新核对；未自动重试。");
        log.Write("Display Transition", $"Stable HDR={result.State.HdrEnabled}; {result.State.RefreshRate:0.###}Hz; {result.State.BitsPerChannel}bpc; {result.State.ColorEncoding}; ACM={result.State.AutoColorManagementEnabled?.ToString() ?? "Unknown"}.");
        return result.State;
    }

    private async Task<(DisplayState State, bool Stable)> ObserveAsync(string target, Func<DisplayState, bool> matches)
    {
        DisplayState? previous = null;
        DisplayState? current = null;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Delay(500);
            current = await ReadTargetAsync(target);
            if (matches(current) && previous is not null && SameSignal(previous, current)) return (current, true);
            previous = matches(current) ? current : null;
        }
        return (current!, false);
    }

    private async Task<DisplayState> ReadTargetAsync(string target) => (await display.ReadAsync())
        .FirstOrDefault(s => string.Equals(s.DevicePath, target, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("所选显示器已断开，已停止后续切换。");

    private static bool SameSignal(DisplayState left, DisplayState right) => left.Width == right.Width
        && left.Height == right.Height && left.HdrEnabled == right.HdrEnabled
        && Math.Abs(left.RefreshRate - right.RefreshRate) < 0.1
        && left.BitsPerChannel == right.BitsPerChannel && left.ColorEncoding == right.ColorEncoding
        && left.AutoColorManagementEnabled == right.AutoColorManagementEnabled;
}
