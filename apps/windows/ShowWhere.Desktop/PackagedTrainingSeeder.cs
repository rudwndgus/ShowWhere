using System.IO;
using System.Reflection;

namespace ShowWhere.Desktop;

internal static class PackagedTrainingSeeder
{
    private static readonly string[] FileNames =
    [
        "answer-feedback.jsonl",
        "completions.jsonl",
        "corrections.jsonl",
        "learning-edits.jsonl",
        "learning-status.jsonl",
    ];

    public static void Seed(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var fileName in FileNames)
        {
            var destination = Path.Combine(dataDirectory, fileName);
            if (File.Exists(destination) && new FileInfo(destination).Length > 0) continue;

            using var source = assembly.GetManifestResourceStream($"ShowWhere.Training.{fileName}");
            if (source is null) continue;

            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            source.CopyTo(output);
        }
    }
}
