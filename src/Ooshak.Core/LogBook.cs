namespace Ooshak.Core;

public sealed record LogEntry(DateTime Time, string Message);

/// <summary>Keeps recent log lines for the interface and appends them to a size-limited file.</summary>
public sealed class LogBook
{
    private const int MaxEntries = 500;
    private const long MaxFileBytes = 2 * 1024 * 1024;

    private readonly Lock _gate = new();
    private readonly Queue<LogEntry> _entries = new();

    public static string FilePath => Path.Combine(OoshakConfig.AppDataDir, "ooshak.log");

    public event Action<LogEntry>? Added;
    public event Action? Cleared;

    public IReadOnlyList<LogEntry> Recent()
    {
        lock (_gate)
            return _entries.ToList();
    }

    public void Add(string message)
    {
        var entry = new LogEntry(DateTime.Now, message);

        lock (_gate)
        {
            _entries.Enqueue(entry);
            if (_entries.Count > MaxEntries)
                _entries.Dequeue();

            Directory.CreateDirectory(OoshakConfig.AppDataDir);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxFileBytes)
                File.Delete(FilePath);

            File.AppendAllText(FilePath, $"{entry.Time:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }

        Added?.Invoke(entry);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }

        Cleared?.Invoke();
    }
}
