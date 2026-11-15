namespace ObsMuteWarning;

/// <summary>Minimal append-only log in %LOCALAPPDATA%\ObsMuteWarning\log.txt (and stdout when attached).</summary>
internal static class Log
{
    private static readonly object Lock = new();
    private const long MaxSize = 1_000_000;

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ObsMuteWarning", "log.txt");

    public static bool EchoToConsole { get; set; }

    public static void Info(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        lock (Lock)
        {
            if (EchoToConsole) Console.WriteLine(line);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxSize)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }
}
