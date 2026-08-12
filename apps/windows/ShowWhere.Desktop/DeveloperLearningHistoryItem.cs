using System.ComponentModel;
using System.Runtime.CompilerServices;
using ShowWhere.Core;

namespace ShowWhere.Desktop;

public sealed class DeveloperLearningHistoryItem : INotifyPropertyChanged
{
    private bool _active;

    public DeveloperLearningHistoryItem(DeveloperLearningHistoryRecord record)
    {
        FeedbackId = record.FeedbackId;
        CreatedAtUtc = record.CreatedAtUtc;
        Rating = record.Rating;
        Goal = record.Goal;
        Answer = record.Answer;
        TargetLabel = record.TargetLabel;
        Comment = record.Comment;
        _active = record.Active;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string FeedbackId { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public string Rating { get; }
    public string Goal { get; }
    public string Answer { get; }
    public string? TargetLabel { get; }
    public string? Comment { get; }
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Active)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToggleText)));
        }
    }
    public string RatingText => Rating switch
    {
        "correct" => "O 정답",
        "incorrect" => "X 수정",
        "completed" => "끝",
        _ => Rating,
    };
    public string StatusText => Active ? "적용 중" : "취소됨";
    public string ToggleText => Active ? "이 기록 취소" : "다시 적용";
    public string TimeText => CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
