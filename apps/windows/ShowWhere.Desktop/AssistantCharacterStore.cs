using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace ShowWhere.Desktop;

public sealed record AssistantCharacterOption(string Key, string DisplayName);

public sealed class AssistantCharacterStore : INotifyPropertyChanged
{
    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShowWhere",
        "assistant-character.txt");
    private string _selectedCharacterKey;

    public AssistantCharacterStore()
    {
        _selectedCharacterKey = Load();
    }

    public IReadOnlyList<AssistantCharacterOption> Options { get; } =
    [
        new("monkey", "원숭이"),
        new("robot", "로봇"),
        new("duck", "오리"),
    ];

    public string SelectedCharacterKey
    {
        get => _selectedCharacterKey;
        set
        {
            var next = Options.Any(option => option.Key == value) ? value : "monkey";
            if (_selectedCharacterKey == next) return;
            _selectedCharacterKey = next;
            Save();
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private string Load()
    {
        try
        {
            var saved = File.Exists(_filePath) ? File.ReadAllText(_filePath).Trim() : "monkey";
            return saved is "monkey" or "robot" or "duck" ? saved : "monkey";
        }
        catch
        {
            return "monkey";
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, _selectedCharacterKey);
        }
        catch { }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
