using System.ComponentModel;
using System.Runtime.CompilerServices;
using ShowWhere.Core;

namespace ShowWhere.Desktop;

public sealed class DeveloperLearningHistoryItem : INotifyPropertyChanged
{
    private bool _active;
    private string _rating;
    private string _goal;
    private string _answer;
    private string? _targetLabel;
    private string? _comment;
    private bool _isDirty;

    public DeveloperLearningHistoryItem(DeveloperLearningHistoryRecord record)
    {
        FeedbackId = record.FeedbackId;
        CreatedAtUtc = record.CreatedAtUtc;
        _rating = record.Rating;
        _goal = record.Goal;
        _answer = record.Answer;
        _targetLabel = record.TargetLabel;
        _comment = record.Comment;
        _active = record.Active;
        HasEdits = record.HasEdits;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string FeedbackId { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public string Rating { get => _rating; set { if (Set(ref _rating, value)) OnPropertyChanged(nameof(RatingText)); } }
    public string Goal { get => _goal; set => Set(ref _goal, value); }
    public string Answer { get => _answer; set => Set(ref _answer, value); }
    public string? TargetLabel { get => _targetLabel; set => Set(ref _targetLabel, value); }
    public string? Comment { get => _comment; set => Set(ref _comment, value); }
    public bool HasEdits { get; private set; }
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SaveText));
        }
    }
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ToggleText));
        }
    }
    public string RatingText => Rating switch
    {
        "correct" => "O Correct",
        "incorrect" => "X Correct",
        "completed" => "Done",
        _ => Rating,
    };
    public string StatusText => Active ? "Active" : "Disabled";
    public string ToggleText => Active ? "Disable record" : "Reapply";
    public string SaveText => IsDirty ? "Save changes *" : "Save changes";
    public string TimeText => CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public void MarkSaved()
    {
        HasEdits = true;
        IsDirty = false;
        OnPropertyChanged(nameof(HasEdits));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        IsDirty = true;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record DeveloperRatingOption(string Value, string Label);

public sealed record DeveloperCorrectionReasonOption(string Value, string Label);
