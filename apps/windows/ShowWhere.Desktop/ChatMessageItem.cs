using System.ComponentModel;
using System.Runtime.CompilerServices;
using ShowWhere.Core;

namespace ShowWhere.Desktop;

public sealed class ChatMessageItem : INotifyPropertyChanged
{
    private string _text;
    private bool _isPending;
    private string? _evaluation;

    public ChatMessageItem(string role, string text, bool isPending = false)
    {
        Role = role;
        _text = text;
        _isPending = isPending;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id { get; } = Guid.NewGuid().ToString("D");
    public string Role { get; }
    public string Text { get => _text; set => Set(ref _text, value); }
    public bool IsPending
    {
        get => _isPending;
        set
        {
            if (!Set(ref _isPending, value)) return;
            OnPropertyChanged(nameof(CanEvaluate));
        }
    }
    public string? Evaluation
    {
        get => _evaluation;
        private set
        {
            if (!Set(ref _evaluation, value)) return;
            OnPropertyChanged(nameof(CanEvaluate));
            OnPropertyChanged(nameof(IsEvaluated));
            OnPropertyChanged(nameof(EvaluationLabel));
        }
    }
    public bool CanEvaluate => Role == "assistant" && !IsPending && Evaluation is null;
    public bool IsEvaluated => Evaluation is not null;
    public string EvaluationLabel => Evaluation switch
    {
        "correct" => "O · 좋은 답변으로 저장됨",
        "incorrect" => "X · 수정 필요로 저장됨",
        "completed" => "끝 · 최종 완료 상태로 저장됨",
        _ => string.Empty,
    };
    public string? OriginalGoal { get; private set; }
    public string? EffectiveGoal { get; private set; }
    public ApplicationContext? Context { get; private set; }
    public string? SnapshotHash { get; private set; }
    public GuideDecision? Decision { get; private set; }
    public string? TargetLabel { get; private set; }
    public UiBounds? TargetBounds { get; private set; }
    public CorrectionTargetSignature? TargetSignature { get; private set; }

    public void AttachTrainingContext(
        string? originalGoal,
        string? effectiveGoal,
        ApplicationContext? context,
        string? snapshotHash,
        GuideDecision? decision,
        string? targetLabel = null,
        UiBounds? targetBounds = null,
        CorrectionTargetSignature? targetSignature = null)
    {
        OriginalGoal = originalGoal;
        EffectiveGoal = effectiveGoal;
        Context = context;
        SnapshotHash = snapshotHash;
        Decision = decision;
        TargetLabel = targetLabel;
        TargetBounds = targetBounds;
        TargetSignature = targetSignature;
    }

    public void MarkEvaluated(string evaluation) => Evaluation = evaluation;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
