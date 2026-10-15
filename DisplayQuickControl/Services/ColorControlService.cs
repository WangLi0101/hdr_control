using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace DisplayQuickControl.Services;

public sealed class ColorControlService(SettingsService settings, LogService log, TimeSpan? commandTimeout = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public void ValidatePreset(string presetName)
    {
        if (_gate.CurrentCount == 0) throw new InvalidOperationException("ColorControl 仍在执行上一条命令，请等待完成后再切换。");
        var path = settings.Current.ColorControlPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("找不到 ColorControl.exe，请打开“设置”并选择程序。");
        if (string.IsNullOrWhiteSpace(presetName)) throw new InvalidOperationException("预设名称为空，请检查设置。");
    }
    public async Task ApplyPresetAsync(string presetName)
    {
        if (!await _gate.WaitAsync(0)) throw new InvalidOperationException("ColorControl 仍在执行上一条命令，请等待完成后再切换。");
        Process? process = null; var releaseHere = true;
        try
        {
            var path = settings.Current.ColorControlPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("找不到 ColorControl.exe，请打开“设置”并选择程序。");
            if (string.IsNullOrWhiteSpace(presetName)) throw new InvalidOperationException("预设名称为空，请检查设置。");
            // Official README + CommandLineHandler.cs: --nvpreset <name/id> --nogui.
            // ArgumentList prevents quoting bugs and does not invoke a command shell.
            // Upstream Utils.OpenConsole may explicitly allocate a console; also request SW_HIDE.
            var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(path)!, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("--nvpreset"); start.ArgumentList.Add(presetName); start.ArgumentList.Add("--nogui");
            process = new Process { StartInfo = start };
            // Consume output without unbounded buffering; upstream sometimes returns exit=0 on failure.
            var cliError = 0;
            DataReceivedEventHandler onLine = (_, e) =>
            {
                if (e.Data is { } line && (line.Contains("Preset not found", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("Error executing preset", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("error while executing", StringComparison.OrdinalIgnoreCase))) Interlocked.Exchange(ref cliError, 1);
            };
            process.OutputDataReceived += onLine; process.ErrorDataReceived += onLine;
            if (!process.Start()) throw new InvalidOperationException("无法启动 ColorControl。");
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            using var timeout = new CancellationTokenSource(commandTimeout ?? TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                // Never kill/relaunch a display operation. Keep the gate closed until the process exits.
                releaseHere = false; _ = ReleaseAfterExitAsync(process); process = null;
                throw new TimeoutException("ColorControl 在 30 秒内未完成操作，未自动重试。此进程退出前无法再次切换，请检查 ColorControl。");
            }
            if (process.ExitCode != 0 || Volatile.Read(ref cliError) != 0)
                throw new InvalidOperationException($"ColorControl 报告预设错误（退出码 {process.ExitCode}）。请检查预设名称和 ColorControl 日志。");
            log.Write("Preset Applied", "Command completed; actual display state must be verified.");
        }
        catch (Win32Exception ex)
        {
            log.Write("ColorControl Error", $"Process launch failed: {ex.NativeErrorCode}.");
            throw new InvalidOperationException("无法启动 ColorControl，请检查程序路径、运行时依赖和访问权限。", ex);
        }
        catch (Exception ex) { log.Write("ColorControl Error", ex.GetType().Name); throw; }
        finally { process?.Dispose(); if (releaseHere) _gate.Release(); }
    }
    private async Task ReleaseAfterExitAsync(Process process)
    {
        try { await process.WaitForExitAsync(); }
        catch (Exception ex) { log.Write("ColorControl Error", $"Exit observation: {ex.GetType().Name}."); }
        finally { process.Dispose(); _gate.Release(); }
    }
}
