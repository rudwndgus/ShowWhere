using System.IO;
using System.Text.Json;

namespace ShowWhere.Desktop;

public sealed record AssistantPosition(double Left, double Top);

public sealed class AssistantPositionStore
{
    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShowWhere",
        "window-state.json");

    public AssistantPosition? Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? JsonSerializer.Deserialize<AssistantPosition>(File.ReadAllText(_filePath))
                : null;
        }
        catch { return null; }
    }

    public void Save(double left, double top)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(new AssistantPosition(left, top)));
        }
        catch { }
    }
}
