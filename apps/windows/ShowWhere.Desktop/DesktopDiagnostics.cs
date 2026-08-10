using System.Text;
using System.IO;

namespace ShowWhere.Desktop;

internal static class DesktopDiagnostics
{
    private static readonly object Gate = new();

    public static void WriteEvent(string eventName, params (string Name, object? Value)[] fields)
    {
        try
        {
            var entry = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" event=").Append(eventName);
            foreach (var (name, value) in fields)
                entry.Append(' ').Append(name).Append('=').Append(value);
            entry.AppendLine();
            WriteToFile("windows-debug.log", entry.ToString());
        }
        catch { }
    }

    public static void Write(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ShowWhere");
            Directory.CreateDirectory(directory);
            var entry = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" type=").Append(exception.GetType().FullName)
                .Append(" message=").AppendLine(exception.Message)
                .AppendLine(exception.StackTrace)
                .ToString();
            lock (Gate)
                File.AppendAllText(Path.Combine(directory, "windows-error.log"), entry);
        }
        catch { }
    }

    private static void WriteToFile(string fileName, string entry)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShowWhere");
        Directory.CreateDirectory(directory);
        lock (Gate)
            File.AppendAllText(Path.Combine(directory, fileName), entry);
    }
}
