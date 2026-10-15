using System.ComponentModel;
using System.Runtime.InteropServices;
using DisplayQuickControl.Models;
using static DisplayQuickControl.Native.NativeMethods;

namespace DisplayQuickControl.Services;

public sealed class DisplayService(LogService log)
{
    public Task<IReadOnlyList<DisplayState>> ReadAsync() => Task.Run(Read);
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
                    var adapter = "Unknown adapter"; var primary = false;
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
                    bool? hdr = null; uint? bits = null; var encoding = "Unknown";
                    var color2 = new AdvancedColor2 { Header = Header<AdvancedColor2>(15, path.Target.Adapter, path.Target.Id) };
                    var colorError = GetAdvancedColor2(ref color2);
                    if (colorError == 0)
                    {
                        // SDK activeColorMode: SDR=0, WCG=1, HDR=2. WCG alone is NOT HDR.
                        hdr = color2.ActiveColorMode == 2;
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
                        adapter, width, height, path.Target.RefreshRate.Value, hdr, bits, encoding, primary));
                }
                log.Write("Display State", $"Read {states.Count} active target(s).");
                return states;
            }
            throw new InvalidOperationException("Display topology is changing. Wait for the signal to stabilize, then Refresh.");
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
