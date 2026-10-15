using System.IO;

namespace DisplayQuickControl.Services;

// Deliberately excludes preset names, executable paths, monitor IDs and CLI output.
public sealed class LogService
{
    private readonly object _sync = new();
    private readonly string _directory;
    public LogService(string root) { _directory = Path.Combine(root, "Logs"); }
    public void Write(string category, string message)
    {
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(_directory);
                var files = new DirectoryInfo(_directory).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
                foreach (var old in files.Skip(6)) old.Delete();
                var path = Path.Combine(_directory, $"{DateTime.UtcNow:yyyy-MM-dd}.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.WriteAllText(path, "Log rotated.\n");
                File.AppendAllText(path, $"{DateTime.UtcNow:O} [{category}] {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
