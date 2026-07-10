namespace RectangleWinPlus;

internal static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RectangleWinPlus");

    public static string FilePath { get; } = Path.Combine(Directory, "log.txt");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
                Trim();
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private static void Trim()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length <= MaxBytes) return;
        var lines = File.ReadAllLines(FilePath);
        File.WriteAllLines(FilePath, lines.Skip(lines.Length / 2));
    }
}
