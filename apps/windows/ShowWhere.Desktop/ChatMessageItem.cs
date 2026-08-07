using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ShowWhere.Desktop;

public sealed class ChatMessageItem : INotifyPropertyChanged
{
    private string _text;
    private bool _isPending;

    public ChatMessageItem(string role, string text, bool isPending = false)
    {
        Role = role;
        _text = text;
        _isPending = isPending;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Role { get; }
    public string Text { get => _text; set => Set(ref _text, value); }
    public bool IsPending { get => _isPending; set => Set(ref _isPending, value); }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
