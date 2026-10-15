using System.ComponentModel;
using System.Runtime.InteropServices;
using DisplayQuickControl.Models;
using static DisplayQuickControl.Native.NativeMethods;

namespace DisplayQuickControl.Services;

public interface IDisplayControl
{
    Task<IReadOnlyList<DisplayState>> ReadAsync();
    Task<bool> TryDisableHdrAsync(string devicePath);
    Task SetAutoColorManagementAsync(string devicePath, bool enabled);
}

public sealed class DisplayService(LogService log) : IDisplayControl
{
    public Task<IReadOnlyList<DisplayState>> ReadAsync() => Task.Run(Read);
    public Task<bool> TryDisableHdrAsync(string devicePath) => Task.Run(() =>
    {
        var target = FindTarget(devicePath);
        var info = new AdvancedColor2 { Header = Header<AdvancedColor2>(15, target.Adapter, target.Id) };
        var error = GetAdvancedColor2(ref info);
        // Older systems retain the existing ColorControl path. Never fall back to the broad
        // legacy advanced-color setter on systems exposing separate HDR/WCG controls.
        if (error is 50 or 87) return false;
        Check(error);
        if (info.ActiveColorMode != 2) return true;
        var request = new SetColorState { Header = Header<SetColorState>(16, target.Adapter, target.Id), Enabled = 0 };
        Check(SetAdvancedColorState(ref request));
        log.Write("Display Transition", "Requested HDR Off using SET_HDR_STATE.");
        return true;
    });
    public Task SetAutoColorManagementAsync(string devicePath, bool enabled) => Task.Run(() =>
    {
        var target = FindTarget(devicePath);
        var request = new SetColorState { Header = Header<SetColorState>(17, target.Adapter, target.Id), Enabled = enabled ? 1u : 0u };
        Check(SetAdvancedColorState(ref request));
        log.Write("Display Transition", $"Restored ACM preference: {enabled}.");
    });
    private static PathTarget FindTarget(string devicePath)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Check(GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount));
            var paths = new DisplayPath[pathCount]; var modes = new ModeInfo[modeCount];
            var error = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0);
            if (error == 122) continue;
            Check(error);
            for (var i = 0; i < pathCount; i++)
            {
                var target = paths[i].Target;
                var name = new TargetName { Header = Header<TargetName>(2, target.Adapter, target.Id) };
                Check(GetTargetName(ref name));
                if (string.Equals(name.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase)) return target;
            }
            throw new InvalidOperationException("所选显示器已断开，已停止切换。");
        }
        throw new InvalidOperationException("显示器连接状态正在变化，请等待信号稳定后重试。");
    }
    private IReadOnlyList<DisplayState> Read()
    {
        try
        {
            // Buffer sizes can change during a hot-plug. Only retry this read, never a preset.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                Check(GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount));
                var paths = new DisplayPath[pathCount]; var modes = new ModeInfo[modeCount];
                var error = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0);
                if (error == 122) continue;
                Check(error);
                var states = new List<DisplayState>();
                for (var i = 0; i < pathCount; i++)
                {
                    var path = paths[i];
                    var name = new TargetName { Header = Header<TargetName>(2, path.Target.Adapter, path.Target.Id) };
                    Check(GetTargetName(ref name));
                    var source = new SourceName { Header = Header<SourceName>(1, path.Source.Adapter, path.Source.Id) };
                    Check(GetSourceName(ref source));
                    var adapter = "未知显卡"; var primary = false;
                    for (uint a = 0; ; a++)
                    {
                        var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
                        if (!EnumDisplayDevices(null, a, ref device, 0)) break;
                        if (!string.Equals(device.DeviceName, source.GdiName, StringComparison.OrdinalIgnoreCase)) continue;
                        adapter = device.DeviceString; primary = (device.StateFlags & 4) != 0; break;
                    }
                    uint width = 0, height = 0;
                    if (path.Source.ModeIndex < modeCount)
                    {
                        var mode = modes[path.Source.ModeIndex];
                        if (mode.InfoType == 1) { width = mode.Source.Width; height = mode.Source.Height; }
                    }
                    bool? hdr = null, acm = null; uint? bits = null; var encoding = "Unknown";
                    var color2 = new AdvancedColor2 { Header = Header<AdvancedColor2>(15, path.Target.Adapter, path.Target.Id) };
                    var colorError = GetAdvancedColor2(ref color2);
                    if (colorError == 0)
                    {
                        // SDK activeColorMode: SDR=0, WCG=1, HDR=2. WCG alone is NOT HDR.
                        hdr = color2.ActiveColorMode == 2;
                        acm = (color2.Flags & 128) != 0;
                        bits = ValidBits(color2.BitsPerChannel); encoding = Encoding(color2.ColorEncoding);
                    }
                    else
                    {
                        var color = new AdvancedColor { Header = Header<AdvancedColor>(9, path.Target.Adapter, path.Target.Id) };
                        colorError = GetAdvancedColor(ref color);
                        if (colorError == 0)
                        {
                            // Legacy Windows advanced-color API; wideColorEnforced denotes WCG-only mode.
                            hdr = (color.Flags & 2) != 0 && (color.Flags & 4) == 0;
                            bits = ValidBits(color.BitsPerChannel); encoding = Encoding(color.ColorEncoding);
                        }
                        else log.Write("Display API Error", $"Advanced color query: {colorError}.");
                    }
                    states.Add(new(name.DevicePath, string.IsNullOrWhiteSpace(name.FriendlyName) ? source.GdiName : name.FriendlyName,
                        adapter, width, height, path.Target.RefreshRate.Value, hdr, bits, encoding, primary)
                        { AutoColorManagementEnabled = acm });
                }
                log.Write("Display State", $"Read {states.Count} active target(s).");
                return states;
            }
            throw new InvalidOperationException("显示器连接状态正在变化，请等待信号稳定后再刷新。");
        }
        catch (Exception ex)
        {
            log.Write("Display API Error", ex is Win32Exception win32 ? $"Win32 {win32.NativeErrorCode}." : ex.GetType().Name);
            throw;
        }
    }
    private static void Check(int error) { if (error != 0) throw new Win32Exception(error); }
    private static uint? ValidBits(uint bits) => bits is >= 6 and <= 16 ? bits : null;
    private static string Encoding(uint encoding) => encoding switch { 0 => "RGB", 1 => "YCbCr 4:4:4", 2 => "YCbCr 4:2:2", 3 => "YCbCr 4:2:0", 4 => "Intensity", _ => "Unknown" };
}
