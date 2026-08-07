using System.Text;
using System.IO;

namespace ShowWhere.Desktop;

internal static class DesktopDiagnostics
{
    private static readonly object Gate = new();

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
}
