using DisplayQuickControl.Models;
using DisplayQuickControl.Services;

internal static class DisplayTransitionChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var hdr = new DisplayState("selected", "Monitor", "NVIDIA", 2560, 1440, 180, true, 8, "RGB", true)
        { AutoColorManagementEnabled = false };
        var display = new FakeDisplay(hdr);
        var calls = 0;
        var log = new LogService(root);
        DisplayTransitionService Service(Func<string, Task> apply) => new(display, apply, log, _ => Task.CompletedTask);
        await Service(_ =>
        {
            check(display.State.HdrEnabled == false && display.Reads >= 2, "SDR color preset runs only after two observations of HDR Off");
            calls++;
            // Simulate a legacy dependency unexpectedly changing the independent WCG preference.
            display.State = display.State with { AutoColorManagementEnabled = true };
            return Task.CompletedTask;
        }).ApplyAsync(DisplayMode.Sdr180, hdr);
        check(calls == 1 && display.HdrWrites == 1 && display.AcmWrites == 1
            && display.State.AutoColorManagementEnabled == false, "SDR transition invokes one preset and restores the previous ACM Off preference");

        var sdr = hdr with { HdrEnabled = false, AutoColorManagementEnabled = true };
        display = new FakeDisplay(sdr);
        await Service(_ => { calls++; return Task.CompletedTask; }).ApplyAsync(DisplayMode.Sdr180, sdr);
        check(display.HdrWrites == 0 && display.AcmWrites == 0 && display.State.AutoColorManagementEnabled == true,
            "Already-SDR mode preserves intentional ACM On without unnecessary writes");

        display = new FakeDisplay(sdr);
        await Service(_ => { display.State = hdr with { AutoColorManagementEnabled = false }; return Task.CompletedTask; })
            .ApplyAsync(DisplayMode.Hdr180, sdr);
        check(display.AcmWrites == 1 && display.State.HdrEnabled == true && display.State.AutoColorManagementEnabled == true,
            "Entering HDR preserves intentional ACM On independently of HDR");

        display = new FakeDisplay(hdr) { IgnoreHdrWrite = true };
        calls = 0;
        try
        {
            await Service(_ => { calls++; return Task.CompletedTask; }).ApplyAsync(DisplayMode.Sdr180, hdr);
            throw new Exception("Unstable HDR exit accepted");
        }
        catch (InvalidOperationException)
        { check(calls == 0 && display.HdrWrites == 1, "HDR exit timeout stops before the SDR preset and never retries the write"); }

        display = new FakeDisplay(hdr) { SeparateApiAvailable = false };
        await Service(_ => { display.State = sdr with { AutoColorManagementEnabled = false }; return Task.CompletedTask; })
            .ApplyAsync(DisplayMode.Sdr180, hdr);
        check(display.HdrWrites == 0, "Older systems use ColorControl without the unsupported HDR setter");

        display = new FakeDisplay(hdr);
        calls = 0;
        try
        {
            await Service(_ => { calls++; throw new TimeoutException(); }).ApplyAsync(DisplayMode.Sdr180, hdr);
            throw new Exception("Command timeout swallowed");
        }
        catch (TimeoutException)
        { check(calls == 1 && display.AcmWrites == 0, "Command timeout performs no retry or competing post-command display writes"); }

        display = new FakeDisplay(sdr);
        try
        {
            await Service(_ => { display.Disconnected = true; return Task.CompletedTask; }).ApplyAsync(DisplayMode.Sdr180, sdr);
            throw new Exception("Disconnected display accepted");
        }
        catch (InvalidOperationException)
        { check(display.AcmWrites == 0, "Disconnect never restores color preferences onto a different display"); }

        display = new FakeDisplay(sdr) { AlternateColors = true };
        calls = 0;
        try
        {
            await Service(_ => { calls++; return Task.CompletedTask; }).ApplyAsync(DisplayMode.Sdr180, sdr);
            throw new Exception("Unstable colors accepted");
        }
        catch (InvalidOperationException)
        { check(calls == 1, "Fluctuating color encoding cannot pass signal stability checks even under default color policy"); }
    }

    private sealed class FakeDisplay(DisplayState state) : IDisplayControl
    {
        public DisplayState State = state;
        public int HdrWrites, AcmWrites, Reads;
        public bool IgnoreHdrWrite, Disconnected, AlternateColors;
        public bool SeparateApiAvailable = true;
        public Task<IReadOnlyList<DisplayState>> ReadAsync()
        {
            Reads++;
            var snapshot = AlternateColors ? State with { ColorEncoding = Reads % 2 == 0 ? "RGB" : "YCbCr 4:4:4" } : State;
            return Task.FromResult<IReadOnlyList<DisplayState>>(Disconnected ? [] : [snapshot]);
        }
        public Task<bool> TryDisableHdrAsync(string devicePath)
        {
            if (!SeparateApiAvailable) return Task.FromResult(false);
            HdrWrites++;
            if (!IgnoreHdrWrite) State = State with { HdrEnabled = false };
            return Task.FromResult(true);
        }
        public Task SetAutoColorManagementAsync(string devicePath, bool enabled)
        {
            if (devicePath != State.DevicePath) throw new Exception("Wrong target");
            AcmWrites++; State = State with { AutoColorManagementEnabled = enabled };
            return Task.CompletedTask;
        }
    }
}
