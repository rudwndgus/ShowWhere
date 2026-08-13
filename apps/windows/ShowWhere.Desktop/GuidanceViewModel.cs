using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ShowWhere.ApiClient;
using ShowWhere.Core;
using ShowWhere.Overlay;
using ShowWhere.WindowsAutomation;

namespace ShowWhere.Desktop;

public sealed class GuidanceViewModel : INotifyPropertyChanged
{
    private sealed record ApprovedReplay(
        string Goal,
        string? TargetId,
        string TargetLabel,
        string Message,
        string? SnapshotHash,
        UiBounds? TargetBounds,
        bool DeveloperVerified);

    private sealed record RecentSafeReply(
        string Goal,
        string SnapshotHash,
        GuideDecision Decision);

    private readonly IWindowsUiObserver _observer;
    private readonly IWindowsChangeMonitor _changeMonitor;
    private readonly IWindowsScreenCaptureService _screenCapture;
    private readonly IGuideApiClient _apiClient;
    private readonly IHighlightOverlay _overlay;
    private readonly ICorrectionSelectionService _correctionSelection;
    private readonly IDeveloperCorrectionStore _correctionStore;
    private readonly Action _exit;
    private readonly MobileRemoteCoordinator? _mobileRemote;
    private CancellationTokenSource? _taskCancellation;
    private TaskSession? _session;
    private string _goalText = string.Empty;
    private string _currentApplication = "관찰 대기 중";
    private string _statusText = "준비됨";
    private string _errorMessage = string.Empty;
    private bool _isLoading;
    private bool _isPaused;
    private WindowsObservation? _clarificationObservation;
    private GuideDecision? _forcedDecision;
    private WindowsObservation? _lastObservation;
    private GuideDecision? _lastDecision;
    private UiCandidate? _lastHighlightedCandidate;
    private UiBounds? _lastHighlightedBounds;
    private string _submittedGoal = string.Empty;
    private string _correctionIntentText = string.Empty;
    private string _correctionCommentText = string.Empty;
    private string _correctionSelectionSummary = "정답 영역을 아직 선택하지 않았습니다.";
    private bool _isCorrectionEditorVisible;
    private bool _saveCorrectionScreenshot = true;
    private bool _isDeveloperMode;
    private bool _isLearningHistoryVisible;
    private string _correctionTaskId = string.Empty;
    private string _correctionStateId = string.Empty;
    private string _correctionTargetConcept = string.Empty;
    private string _correctionExpectedNextState = string.Empty;
    private string _correctionExpectedEvidence = string.Empty;
    private string _correctionOutcomeLabel = "wrong_target";
    private readonly Dictionary<string, ApprovedReplay> _approvedReplays = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApprovedReplay> _recentReplays = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RecentSafeReply> _recentSafeReplies = new(StringComparer.Ordinal);
    private ChatMessageItem? _correctionAnswer;
    private AnswerFeedbackRecord? _correctionFeedback;
    private UiBounds? _pendingCorrectionSelection;
    private UiCandidate? _pendingCorrectionCandidate;
    private WindowsScreenCapture? _pendingCorrectionCapture;

    public GuidanceViewModel(
        IWindowsUiObserver observer,
        IWindowsChangeMonitor changeMonitor,
        IWindowsScreenCaptureService screenCapture,
        IGuideApiClient apiClient,
        IHighlightOverlay overlay,
        ICorrectionSelectionService correctionSelection,
        IDeveloperCorrectionStore correctionStore,
        Action exit,
        MobileRemoteCoordinator? mobileRemote = null)
    {
        _observer = observer;
        _changeMonitor = changeMonitor;
        _screenCapture = screenCapture;
        _apiClient = apiClient;
        _overlay = overlay;
        _correctionSelection = correctionSelection;
        _correctionStore = correctionStore;
        _exit = exit;
        _mobileRemote = mobileRemote;
        SubmitCommand = new AsyncRelayCommand(SubmitAsync, CanSubmit);
        SelectClarificationCommand = new AsyncParameterRelayCommand(SelectClarificationAsync);
        RecoveryCommand = new AsyncRelayCommand(
            RecoverAsync,
            () => _session is not null && !_isPaused && ClarificationChoices.Count == 0);
        CancelCommand = new RelayCommand(CancelCurrentTask, () => _session is not null);
        CaptureCorrectionCommand = new AsyncRelayCommand(CaptureCorrectionAsync, CanCaptureCorrection);
        SaveCorrectionCommand = new AsyncRelayCommand(SaveCorrectionAsync, CanSaveCorrection);
        CancelCorrectionEditCommand = new RelayCommand(CloseCorrectionEditor);
        MarkAnswerCorrectCommand = new AsyncParameterRelayCommand(MarkAnswerCorrectAsync, CanEvaluateAnswer);
        MarkAnswerIncorrectCommand = new AsyncParameterRelayCommand(MarkAnswerIncorrectAsync, CanEvaluateAnswer);
        MarkAnswerCompletedCommand = new AsyncParameterRelayCommand(MarkAnswerCompletedAsync, CanEvaluateAnswer);
        TogglePauseCommand = new RelayCommand(TogglePause);
        ToggleDeveloperModeCommand = new RelayCommand(ToggleDeveloperMode);
        ToggleLearningHistoryCommand = new RelayCommand(ToggleLearningHistory);
        ToggleLearningRecordCommand = new AsyncParameterRelayCommand(ToggleLearningRecordAsync);
        SaveLearningRecordCommand = new AsyncParameterRelayCommand(SaveLearningRecordAsync);
        ExitCommand = new RelayCommand(_exit);
        MobileConnectCommand = new AsyncRelayCommand(
            () => _mobileRemote?.ShowPairingAsync() ?? Task.CompletedTask);
        MobileDisconnectCommand = new AsyncRelayCommand(
            () => _mobileRemote?.DisconnectAsync() ?? Task.CompletedTask);
        if (_mobileRemote is not null)
            _mobileRemote.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName == nameof(MobileRemoteCoordinator.IsConnected))
                {
                    OnPropertyChanged(nameof(IsMobileConnected));
                    OnPropertyChanged(nameof(MobileConnectionText));
                }
            };
        Messages.Add(CreateAssistantMessage(
            "assistant",
            "하고 싶은 일을 입력해 주세요. 현재 앱과 Windows 작업표시줄에서 다음에 누를 위치를 찾아드릴게요."));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<UiBounds>? TargetHighlighted;
    public event Action? CorrectionSelectionStarted;
    public event Action? CorrectionSelectionCompleted;
    public ICommand SubmitCommand { get; }
    public ICommand SelectClarificationCommand { get; }
    public ICommand RecoveryCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand CaptureCorrectionCommand { get; }
    public ICommand SaveCorrectionCommand { get; }
    public ICommand CancelCorrectionEditCommand { get; }
    public ICommand MarkAnswerCorrectCommand { get; }
    public ICommand MarkAnswerIncorrectCommand { get; }
    public ICommand MarkAnswerCompletedCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand ToggleDeveloperModeCommand { get; }
    public ICommand ToggleLearningHistoryCommand { get; }
    public ICommand ToggleLearningRecordCommand { get; }
    public ICommand SaveLearningRecordCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand MobileConnectCommand { get; }
    public ICommand MobileDisconnectCommand { get; }
    public ObservableCollection<ChatMessageItem> Messages { get; } = [];
    public ObservableCollection<ClarificationChoiceItem> ClarificationChoices { get; } = [];
    public ObservableCollection<DeveloperLearningHistoryItem> LearningHistory { get; } = [];
    public IReadOnlyList<DeveloperRatingOption> LearningRatingOptions { get; } =
    [
        new("correct", "O 정답"),
        new("incorrect", "X 수정"),
        new("completed", "끝"),
    ];

    public string GoalText
    {
        get => _goalText;
        set { if (Set(ref _goalText, value)) RaiseCommandStates(); }
    }
    public bool IsMobileConnected => _mobileRemote?.IsConnected == true;
    public string MobileConnectionText => _mobileRemote?.ConnectionText ?? "모바일 연결";
    public string CurrentApplication { get => _currentApplication; private set => Set(ref _currentApplication, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string ErrorMessage { get => _errorMessage; private set => Set(ref _errorMessage, value); }
    public bool IsLoading { get => _isLoading; private set { if (Set(ref _isLoading, value)) RaiseCommandStates(); } }
    public bool IsPaused { get => _isPaused; private set { if (Set(ref _isPaused, value)) OnPropertyChanged(nameof(PauseMenuText)); } }
    public bool IsDeveloperMode
    {
        get => _isDeveloperMode;
        private set
        {
            if (!Set(ref _isDeveloperMode, value)) return;
            OnPropertyChanged(nameof(DeveloperModeText));
            if (!value)
            {
                ResetCorrectionDraft();
                IsLearningHistoryVisible = false;
            }
        }
    }
    public bool IsLearningHistoryVisible
    {
        get => _isLearningHistoryVisible;
        private set => Set(ref _isLearningHistoryVisible, value);
    }
    public bool IsCorrectionEditorVisible
    {
        get => _isCorrectionEditorVisible;
        private set { if (Set(ref _isCorrectionEditorVisible, value)) RaiseCommandStates(); }
    }
    public string CorrectionIntentText
    {
        get => _correctionIntentText;
        set { if (Set(ref _correctionIntentText, value)) RaiseCommandStates(); }
    }
    public string CorrectionCommentText
    {
        get => _correctionCommentText;
        set { if (Set(ref _correctionCommentText, value)) RaiseCommandStates(); }
    }
    public string CorrectionTaskId { get => _correctionTaskId; set { if (Set(ref _correctionTaskId, value)) RaiseCommandStates(); } }
    public string CorrectionStateId { get => _correctionStateId; set { if (Set(ref _correctionStateId, value)) RaiseCommandStates(); } }
    public string CorrectionTargetConcept { get => _correctionTargetConcept; set { if (Set(ref _correctionTargetConcept, value)) RaiseCommandStates(); } }
    public string CorrectionExpectedNextState { get => _correctionExpectedNextState; set { if (Set(ref _correctionExpectedNextState, value)) RaiseCommandStates(); } }
    public string CorrectionExpectedEvidence { get => _correctionExpectedEvidence; set { if (Set(ref _correctionExpectedEvidence, value)) RaiseCommandStates(); } }
    public string CorrectionOutcomeLabel { get => _correctionOutcomeLabel; set { if (Set(ref _correctionOutcomeLabel, value)) RaiseCommandStates(); } }
    public IReadOnlyList<string> CorrectionOutcomeLabels => DeveloperLabeling.OutcomeLabels;
    public string CorrectionSelectionSummary
    {
        get => _correctionSelectionSummary;
        private set => Set(ref _correctionSelectionSummary, value);
    }
    public bool SaveCorrectionScreenshot
    {
        get => _saveCorrectionScreenshot;
        set => Set(ref _saveCorrectionScreenshot, value);
    }
    public string CorrectionDataDirectory => _correctionStore.DataDirectory;
    public string PauseMenuText => IsPaused ? "다시 시작" : "일시 정지";
    public string DeveloperModeText => IsDeveloperMode ? "개발자 모드 ON" : "개발자 모드 OFF";

    private void ToggleLearningHistory()
    {
        if (!IsDeveloperMode) return;
        IsLearningHistoryVisible = !IsLearningHistoryVisible;
        if (IsLearningHistoryVisible) RefreshLearningHistory();
    }

    private void RefreshLearningHistory()
    {
        LearningHistory.Clear();
        foreach (var record in _correctionStore.GetHistory())
            LearningHistory.Add(new DeveloperLearningHistoryItem(record));
    }

    private async Task ToggleLearningRecordAsync(object? parameter)
    {
        if (!IsDeveloperMode || parameter is not DeveloperLearningHistoryItem item) return;
        try
        {
            await _correctionStore.SetFeedbackActiveAsync(
                item.FeedbackId,
                !item.Active,
                item.Active ? "Developer revoked this learning record in history." : "Developer restored this learning record in history.");
            item.Active = !item.Active;
            _approvedReplays.Clear();
            _recentReplays.Clear();
            _recentSafeReplies.Clear();
            StatusText = item.Active ? "학습 기록 다시 적용됨" : "학습 기록 취소됨";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            StatusText = "학습 기록 변경 오류";
        }
    }

    private async Task SaveLearningRecordAsync(object? parameter)
    {
        if (!IsDeveloperMode || parameter is not DeveloperLearningHistoryItem item) return;
        try
        {
            await _correctionStore.SaveHistoryEditAsync(new DeveloperLearningEditRecord(
                1,
                Guid.NewGuid().ToString("D"),
                DateTimeOffset.UtcNow,
                item.FeedbackId,
                item.Rating,
                item.Goal,
                item.Answer,
                item.TargetLabel,
                item.Comment));
            item.MarkSaved();
            _approvedReplays.Clear();
            _recentReplays.Clear();
            _recentSafeReplies.Clear();
            StatusText = "LOG 수정 내용 저장됨 · 다음 판단부터 즉시 적용";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            StatusText = "LOG 수정 저장 오류";
        }
    }

    private bool CanSubmit() => !IsPaused && !IsLoading && !string.IsNullOrWhiteSpace(GoalText);

    private async Task SubmitAsync()
    {
        var query = GoalText.Trim();
        if (string.IsNullOrWhiteSpace(query)) return;
        await SubmitGoalAsync(query);
    }

    public async Task SubmitRemoteAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        while (IsLoading) await Task.Delay(100);
        if (IsPaused) return;
        await SubmitGoalAsync(query.Trim());
    }

    private async Task SubmitGoalAsync(string query)
    {
        var replayObservation = _lastObservation;
        var immediateReplay = TryResolveImmediateReplay(query, replayObservation);
        var immediateSafeReply = TryResolveRecentSafeReply(query, replayObservation);
        Messages.Add(new ChatMessageItem("user", query));
        GoalText = string.Empty;
        ResetCorrectionDraft();
        _submittedGoal = query;
        _lastObservation = null;
        _lastDecision = null;
        _lastHighlightedCandidate = null;
        _lastHighlightedBounds = null;
        CancelRunningWork(markCancelled: false);
        ClearClarificationChoices();
        if (immediateReplay is not null && replayObservation is not null)
        {
            _session = TaskSessionStateMachine.Create(query);
            StatusText = "검증된 정답 즉시 적용";
            if (immediateReplay.TargetId is null && immediateReplay.TargetBounds is not null)
            {
                var message = CreateAssistantMessage(
                    "assistant",
                    $"검증된 정답입니다. '{immediateReplay.TargetLabel}' 위치를 바로 표시할게요.");
                Messages.Add(message);
                _overlay.ShowTarget(immediateReplay.TargetBounds, message.Text);
                TargetHighlighted?.Invoke(immediateReplay.TargetBounds);
                _session = TaskSessionStateMachine.WaitingForUser(_session, message.Text);
                return;
            }
            _forcedDecision = new GuideDecision(
                GuideStatuses.InProgress,
                GuideActions.Highlight,
                $"검증된 정답입니다. '{immediateReplay.TargetLabel}' 위치를 바로 표시할게요.",
                1,
                immediateReplay.TargetId,
                "검증된 항목이 열립니다.");
            _taskCancellation = new CancellationTokenSource();
            await RunGuidanceLoopAsync(replayObservation, _taskCancellation.Token);
            return;
        }
        if (immediateSafeReply is not null)
        {
            _session = TaskSessionStateMachine.Create(query);
            _lastObservation = replayObservation;
            var message = CreateAssistantMessage("assistant", immediateSafeReply.Message);
            AttachTrainingContext(message, immediateSafeReply);
            Messages.Add(message);
            _lastDecision = immediateSafeReply;
            _session = TaskSessionStateMachine.WaitingForUser(_session, immediateSafeReply.Message);
            StatusText = "같은 화면의 검증된 답변 즉시 적용";
            DesktopDiagnostics.WriteEvent(
                "safe_reply_cache_hit",
                ("action", immediateSafeReply.Action));
            return;
        }
        var effectiveGoal = _correctionStore.ResolveIntent(query);
        _session = TaskSessionStateMachine.Create(effectiveGoal);
        if (!string.Equals(effectiveGoal, query, StringComparison.Ordinal))
            Messages.Add(CreateAssistantMessage("assistant", $"저장된 개발자 교정을 적용했어요: {effectiveGoal}"));
        _taskCancellation = new CancellationTokenSource();
        await RunGuidanceLoopAsync(null, _taskCancellation.Token);
    }

    private async Task SelectClarificationAsync(object? parameter)
    {
        if (parameter is not ClarificationChoiceItem choice || IsPaused || IsLoading) return;
        Messages.Add(new ChatMessageItem("user", choice.Label));
        var observation = _clarificationObservation;
        ClearClarificationChoices();
        CancelRunningWork(markCancelled: false);
        if (choice.TargetId is not null && observation is not null && _session is not null)
        {
            _forcedDecision = new GuideDecision(
                GuideStatuses.InProgress,
                GuideActions.Highlight,
                $"좋아요. '{choice.Label}' 위치를 표시할게요.",
                1,
                choice.TargetId,
                "선택한 항목이 열립니다.");
        }
        else if (choice.ResolvedGoal is not null)
        {
            _session = TaskSessionStateMachine.Create(choice.ResolvedGoal);
            observation = null;
        }
        else
        {
            return;
        }
        _taskCancellation = new CancellationTokenSource();
        await RunGuidanceLoopAsync(observation, _taskCancellation.Token);
    }

    private async Task RecoverAsync()
    {
        if (_session is null) return;
        Messages.Add(new ChatMessageItem("user", "다른 위치를 찾아줘"));
        CancelRunningWork(markCancelled: false);
        _session = TaskSessionStateMachine.Failed(_session, "사용자가 다른 후보를 요청함");
        _taskCancellation = new CancellationTokenSource();
        await RunGuidanceLoopAsync(null, _taskCancellation.Token);
    }

    private async Task RunGuidanceLoopAsync(WindowsObservation? currentObservation, CancellationToken cancellationToken)
    {
        if (_session is null) return;
        ErrorMessage = string.Empty;
        ChatMessageItem? pendingMessage = null;
        try
        {
            for (var step = 0; step < 20 && !cancellationToken.IsCancellationRequested; step++)
            {
                IsLoading = true;
                StatusText = step == 0 ? "현재 화면 확인 중" : "다음 화면 확인 중";
                pendingMessage = CreateAssistantMessage(
                    "assistant",
                    step == 0 ? "현재 화면에서 누를 위치를 찾고 있어요…" : "화면이 바뀌었어요. 다음 위치를 찾고 있어요…",
                    isPending: true);
                Messages.Add(pendingMessage);
                _session = TaskSessionStateMachine.ObservationStarted(_session);
                currentObservation ??= await _observer.ObserveAsync(
                    _session.OriginalUserMessage,
                    cancellationToken);
                _lastObservation = currentObservation;
                CurrentApplication = string.IsNullOrWhiteSpace(currentObservation.Context.WindowTitle)
                    ? currentObservation.Context.ApplicationName
                    : $"{currentObservation.Context.ApplicationName} — {currentObservation.Context.WindowTitle}";
                var storedCompletion = _correctionStore.TryResolveCompletion(
                        _session.OriginalUserMessage,
                        currentObservation.Context,
                        currentObservation.Candidates,
                        out _);
                if (storedCompletion)
                {
                    DesktopDiagnostics.WriteEvent(
                        "stored_completion_replay_hit",
                        ("goal", _session.OriginalUserMessage),
                        ("application", currentObservation.Context.ApplicationName),
                        ("url", currentObservation.Context.Url));
                    const string completionMessage = "완료됐어요. 개발자가 검증한 최종 상태에 도착했습니다.";
                    var completionDecision = new GuideDecision(
                        GuideStatuses.Completed,
                        GuideActions.Explain,
                        completionMessage,
                        1);
                    _lastDecision = completionDecision;
                    pendingMessage.Text = completionMessage;
                    AttachTrainingContext(pendingMessage, completionDecision);
                    pendingMessage.IsPending = false;
                    pendingMessage = null;
                    _overlay.Clear();
                    _session = TaskSessionStateMachine.Completed(_session, completionMessage);
                    StatusText = "작업 완료";
                    IsLoading = false;
                    return;
                }
                var prioritizedCandidates = _correctionStore.FilterRejectedCandidates(
                    _session.OriginalUserMessage,
                    currentObservation.Context,
                    currentObservation.Candidates);
                StatusText = $"후보 {prioritizedCandidates.Count}개 분석 중";
                _session = TaskSessionStateMachine.AiRequested(_session);
                var request = new GuideRequest(_session, currentObservation.Context, prioritizedCandidates);
                GuideDecision decision;
                if (_forcedDecision is not null)
                {
                    decision = ContractValidator.ValidateDecision(_forcedDecision, request);
                    _forcedDecision = null;
                    StatusText = "선택한 위치 안내";
                }
                else if (_correctionStore.TryResolveTarget(
                    _session.OriginalUserMessage,
                    currentObservation.Context,
                    prioritizedCandidates,
                    out var correctedTarget,
                    out _))
                {
                    DesktopDiagnostics.WriteEvent(
                        "persisted_gold_replay_hit",
                        ("targetId", correctedTarget.Id),
                        ("label", correctedTarget.Label));
                    decision = new GuideDecision(
                        GuideStatuses.InProgress,
                        GuideActions.Highlight,
                        $"저장된 개발자 교정에 따라 '{correctedTarget.Label ?? correctedTarget.Role}' 위치를 표시할게요.",
                        1,
                        correctedTarget.Id,
                        "교정된 항목이 열립니다.");
                    StatusText = "개발자 교정 적용";
                }
                else if (_correctionStore.TryResolveVisualTarget(
                    _session.OriginalUserMessage,
                    currentObservation.Context,
                    currentObservation.SnapshotHash,
                    out var correctedVisualTarget,
                    out _))
                {
                    // Persisted visual feedback is normalized to the physical desktop
                    // screenshot. Re-read the current Win32 bounds instead of using WPF
                    // DIPs, which differ on 125%/150% mixed-DPI monitor layouts.
                    request = request with
                    {
                        Screenshot = "verified-replay",
                        ScreenshotBounds = WindowsScreenGeometry.GetVirtualScreenBounds(),
                    };
                    decision = new GuideDecision(
                        GuideStatuses.InProgress,
                        GuideActions.HighlightVisual,
                        $"저장된 검증 정답에 따라 '{correctedVisualTarget.Label}' 위치를 표시할게요.",
                        1,
                        VisualTarget: correctedVisualTarget);
                    StatusText = "검증된 화면 정답 적용";
                }
                else
                {
                    StatusText = $"현재 화면과 후보 {prioritizedCandidates.Count}개를 GPT가 분석 중";
                    (decision, request) = await RequestVisionDecisionAsync(request, cancellationToken);
                }
                _lastObservation = currentObservation;
                _lastDecision = decision;
                DesktopDiagnostics.WriteEvent(
                    "guide_decision",
                    ("status", decision.Status),
                    ("action", decision.Action),
                    ("targetId", decision.TargetId),
                    ("confidence", decision.Confidence));
                pendingMessage.Text = decision.Message;
                AttachTrainingContext(pendingMessage, decision);
                pendingMessage.IsPending = false;
                var answerMessage = pendingMessage;
                pendingMessage = null;
                RememberRecentSafeReply(answerMessage);

                if (decision.Action == GuideActions.AskUser
                    && decision.AlternativeTargetIds is { Count: >= 2 })
                {
                    var choices = decision.AlternativeTargetIds
                        .Select(id => prioritizedCandidates.FirstOrDefault(candidate => candidate.Id == id))
                        .Where(candidate => candidate is not null)
                        .Select(candidate => new ClarificationChoiceItem(
                            DescribeClarificationChoice(candidate!),
                            TargetId: candidate!.Id))
                        .ToArray();
                    if (choices.Length >= 2)
                    {
                        _clarificationObservation = currentObservation;
                        foreach (var choice in choices) ClarificationChoices.Add(choice);
                        _session = TaskSessionStateMachine.WaitingForUser(_session, decision.Message);
                        StatusText = "선택이 필요해요";
                        IsLoading = false;
                        RaiseCommandStates();
                        return;
                    }
                }

                if (decision.Status == GuideStatuses.Completed)
                {
                    _overlay.Clear();
                    _session = TaskSessionStateMachine.Completed(_session, decision.Message);
                    StatusText = "작업 완료";
                    IsLoading = false;
                    return;
                }

                if ((decision.Action == GuideActions.Highlight && decision.TargetId is not null)
                    || (decision.Action == GuideActions.HighlightVisual && decision.VisualTarget is not null))
                {
                    DesktopDiagnostics.WriteEvent(
                        "highlight_decision",
                        ("action", decision.Action),
                        ("confidence", decision.Confidence));
                    UiCandidate? selectedCandidate = null;
                    UiBounds bounds;
                    var isOffscreen = false;
                    var selectedLabel = decision.VisualTarget?.Label ?? decision.TargetId ?? "화면 항목";
                    if (decision.Action == GuideActions.HighlightVisual)
                    {
                        if (request.ScreenshotBounds is null || decision.VisualTarget is null)
                            throw new ContractValidationException("The visual target has no screenshot bounds.");
                        bounds = ScreenCoordinateMapper.MapVisualTarget(request.ScreenshotBounds, decision.VisualTarget);
                        if (!WindowsScreenGeometry.ContainsCenter(bounds))
                        {
                            DesktopDiagnostics.WriteEvent(
                                "visual_target_outside_connected_monitor",
                                ("x", Math.Round(bounds.X)),
                                ("y", Math.Round(bounds.Y)));
                            answerMessage.Text = "표시할 위치가 실제 연결된 모니터 안에 있는지 확인하지 못했어요. 화면을 확인한 뒤 다시 질문해 주세요.";
                            _overlay.Clear();
                            _session = TaskSessionStateMachine.WaitingForUser(_session, answerMessage.Text);
                            StatusText = "위치 재확인 필요";
                            IsLoading = false;
                            return;
                        }
                        DesktopDiagnostics.WriteEvent(
                            "visual_target_mapped",
                            ("x", Math.Round(bounds.X)),
                            ("y", Math.Round(bounds.Y)),
                            ("width", Math.Round(bounds.Width)),
                            ("height", Math.Round(bounds.Height)));
                    }
                    else
                    {
                        selectedCandidate = currentObservation.Candidates.FirstOrDefault(candidate =>
                            string.Equals(candidate.Id, decision.TargetId, StringComparison.Ordinal));
                        object? selectedAutomationId = null;
                        object? selectedProcessName = null;
                        object? selectedContainerLabel = null;
                        selectedCandidate?.Attributes?.TryGetValue("automationId", out selectedAutomationId);
                        selectedCandidate?.Attributes?.TryGetValue("processName", out selectedProcessName);
                        selectedCandidate?.Attributes?.TryGetValue("containerLabel", out selectedContainerLabel);
                        DesktopDiagnostics.WriteEvent(
                            "selected_candidate",
                            ("id", decision.TargetId),
                            ("label", selectedCandidate?.Label),
                            ("description", selectedCandidate?.Description),
                            ("role", selectedCandidate?.Role),
                            ("automationId", selectedAutomationId),
                            ("processName", selectedProcessName),
                            ("containerLabel", selectedContainerLabel));
                        if (!currentObservation.Registry.TryResolveState(
                                decision.TargetId!,
                                out bounds,
                                out isOffscreen))
                        {
                            DesktopDiagnostics.WriteEvent("stale_target_rejected", ("id", decision.TargetId));
                            _overlay.Clear();
                            currentObservation = null;
                            continue;
                        }
                        selectedLabel = selectedCandidate?.Label ?? decision.TargetId!;
                    }
                    _lastHighlightedCandidate = selectedCandidate;
                    _lastHighlightedBounds = bounds;
                    AttachTrainingContext(answerMessage, decision, selectedLabel, bounds, selectedCandidate);
                    RememberRecentReplay(answerMessage);
                    _session = TaskSessionStateMachine.GuidanceReady(_session, decision.Message, decision.ExpectedChange);
                    if (isOffscreen)
                    {
                        _overlay.ShowScrollHint(bounds, decision.Message);
                        var virtualScreen = WindowsScreenGeometry.GetVirtualScreenBounds();
                        StatusText = bounds.Y >= virtualScreen.Y + virtualScreen.Height
                            ? "아래로 스크롤해 주세요"
                            : "위로 스크롤해 주세요";
                        var visibleBounds = await currentObservation.Registry.WaitForVisibleBoundsAsync(
                            decision.TargetId!,
                            TimeSpan.FromSeconds(60),
                            cancellationToken);
                        if (visibleBounds is null)
                        {
                            _session = TaskSessionStateMachine.WaitingForUser(_session);
                            StatusText = "스크롤을 기다리는 중";
                            return;
                        }
                        bounds = visibleBounds;
                        _overlay.ShowTarget(bounds, decision.Message);
                        TargetHighlighted?.Invoke(bounds);
                    }
                    else
                    {
                        _overlay.ShowTarget(bounds, decision.Message);
                        DesktopDiagnostics.WriteEvent(
                            "overlay_show_target",
                            ("x", Math.Round(bounds.X)),
                            ("y", Math.Round(bounds.Y)),
                            ("width", Math.Round(bounds.Width)),
                            ("height", Math.Round(bounds.Height)));
                        TargetHighlighted?.Invoke(bounds);
                    }
                    StatusText = "표시된 위치에서 직접 작업해 주세요";
                    IsLoading = false;
                    var changed = await _changeMonitor.WaitForTargetInteractionAsync(
                        bounds,
                        _session.OriginalUserMessage,
                        TimeSpan.FromSeconds(60),
                        cancellationToken);
                    if (changed is null)
                    {
                        _session = TaskSessionStateMachine.WaitingForUser(_session);
                        StatusText = "변화를 기다리는 중 — 필요하면 ‘찾을 수 없어요’를 눌러주세요";
                        return;
                    }
                    _overlay.Clear();
                    _session = TaskSessionStateMachine.StepCompleted(
                        _session,
                        $"사용자가 '{selectedLabel}' 컨트롤을 클릭함. 이전 안내: {decision.Message}");
                    currentObservation = changed;
                    continue;
                }

                _overlay.Clear();
                IsLoading = false;
                if (decision.Action == GuideActions.RequestNewObservation)
                {
                    currentObservation = null;
                    continue;
                }
                _session = TaskSessionStateMachine.WaitingForUser(_session, decision.Message);
                StatusText = decision.Action == GuideActions.AskUser ? "추가 설명 필요" : "안내 확인";
                return;
            }
            StatusText = "안전을 위해 안내를 멈췄어요";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = IsPaused ? "일시 정지됨" : "작업 취소됨";
            if (pendingMessage is not null) Messages.Remove(pendingMessage);
        }
        catch (WindowsObservationException exception)
        {
            ErrorMessage = exception.Message;
            StatusText = "관찰 오류";
            CompletePendingOrAddError(pendingMessage, ErrorMessage);
        }
        catch (ContractValidationException exception)
        {
            DesktopDiagnostics.Write(exception);
            ErrorMessage = "안전하게 표시할 대상을 확인하지 못했어요. 다시 시도해 주세요.";
            StatusText = "안내 검증 오류";
            CompletePendingOrAddError(pendingMessage, ErrorMessage);
        }
        catch (GuideApiException exception)
        {
            ErrorMessage = exception.Message;
            StatusText = "서버 연결 오류";
            CompletePendingOrAddError(pendingMessage, ErrorMessage);
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            ErrorMessage = "현재 화면을 확인하지 못했어요. 잠시 후 다시 시도해 주세요.";
            StatusText = "오류";
            CompletePendingOrAddError(pendingMessage, ErrorMessage);
        }
        finally
        {
            IsLoading = false;
            RaiseCommandStates();
        }
    }

    private async Task<(GuideDecision Decision, GuideRequest Request)> RequestVisionDecisionAsync(
        GuideRequest request,
        CancellationToken cancellationToken)
    {
        _overlay.Clear();
        StatusText = "화면을 보고 정확한 위치를 찾는 중";
        var capture = await _screenCapture.CaptureAsync(cancellationToken);
        var visionRequest = request with
        {
            Screenshot = capture.DataUrl,
            ScreenshotBounds = capture.Bounds,
        };
        var decision = await _apiClient.DecideNextActionAsync(visionRequest, cancellationToken);
        return (decision, visionRequest);
    }

    private static string DescribeClarificationChoice(UiCandidate candidate)
    {
        var label = candidate.Label ?? candidate.Description ?? candidate.Role;
        if (candidate.Attributes?.TryGetValue("sourceScope", out var scopeValue) != true)
            return label;
        return Convert.ToString(scopeValue) switch
        {
            "browser_content" => $"웹사이트 안의 {label}",
            "browser_chrome" => $"브라우저 주소창의 {label}",
            _ => label,
        };
    }

    private bool CanCaptureCorrection() =>
        _session is not null && _lastObservation is not null
        && _correctionAnswer is not null && IsCorrectionEditorVisible;

    private bool CanSaveCorrection() =>
        _session is not null && _correctionAnswer is not null
        && IsCorrectionEditorVisible
        && (_pendingCorrectionSelection is not null
            || !string.IsNullOrWhiteSpace(CorrectionIntentText)
            || !string.IsNullOrWhiteSpace(CorrectionCommentText)
            || !string.IsNullOrWhiteSpace(CorrectionTaskId)
            || !string.IsNullOrWhiteSpace(CorrectionTargetConcept));

    private static bool CanEvaluateAnswer(object? parameter) =>
        parameter is ChatMessageItem { CanEvaluate: true };

    private async Task MarkAnswerCorrectAsync(object? parameter)
    {
        if (parameter is not ChatMessageItem message || !message.CanEvaluate) return;
        try
        {
            var feedback = await _correctionStore.SaveFeedbackAsync(CreateFeedbackRecord(message, "correct"));
            if (DeveloperPositiveFeedback.Create(
                    feedback,
                    message.TargetSignature,
                    message.Decision?.VisualTarget) is { } positiveCorrection)
                await _correctionStore.SaveAsync(positiveCorrection, null);
            RememberApprovedReplay(message);
            DesktopDiagnostics.WriteEvent(
                "approved_replay_saved",
                ("targetId", message.Decision?.TargetId),
                ("label", message.TargetLabel),
                ("snapshotHash", message.SnapshotHash));
            message.MarkEvaluated("correct");
            StatusText = message.TargetSignature is null
                ? "정답으로 영구 저장됨"
                : "정답으로 영구 저장됨 · 같은 의도의 질문은 AI 없이 즉시 안내";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            StatusText = "평가 저장 오류";
        }
    }

    private async Task MarkAnswerIncorrectAsync(object? parameter)
    {
        if (!IsDeveloperMode || parameter is not ChatMessageItem message || !message.CanEvaluate) return;
        try
        {
            var feedback = await _correctionStore.SaveFeedbackAsync(CreateFeedbackRecord(message, "incorrect"));
            if (!string.IsNullOrWhiteSpace(message.OriginalGoal))
            {
                var key = NormalizeGoal(message.OriginalGoal);
                _approvedReplays.Remove(key);
                _recentReplays.Remove(key);
                _recentSafeReplies.Remove(key);
            }
            message.MarkEvaluated("incorrect");
            BeginCorrectionDraft(message, feedback);
            StatusText = "X 저장됨 · 수정 내용을 작성해 주세요";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            StatusText = "평가 저장 오류";
        }
    }

    private async Task MarkAnswerCompletedAsync(object? parameter)
    {
        if (!IsDeveloperMode || parameter is not ChatMessageItem message || !message.CanEvaluate) return;
        try
        {
            CancelRunningWork(markCancelled: false);
            StatusText = "완료 상태 확인 및 저장 중";
            var goal = message.OriginalGoal
                ?? (string.IsNullOrWhiteSpace(_submittedGoal) ? _session?.OriginalUserMessage : _submittedGoal);
            if (string.IsNullOrWhiteSpace(goal)) return;
            WindowsObservation observation;
            try
            {
                observation = await _observer.ObserveAsync(goal, CancellationToken.None);
            }
            catch
            {
                if (_lastObservation is null) throw;
                observation = _lastObservation;
            }
            _lastObservation = observation;
            var feedback = await _correctionStore.SaveFeedbackAsync(CreateFeedbackRecord(message, "completed"));
            var evidence = DeveloperCompletionEvidence.Build(
                observation.Context,
                observation.Candidates);
            var intentKey = DeveloperIntentMatcher.CreateIntentKey(goal);
            var labels = DeveloperLabeling.CreateCorrectionLabels(
                goal,
                observation.Context,
                string.IsNullOrWhiteSpace(intentKey) ? "task completed" : intentKey,
                string.IsNullOrWhiteSpace(intentKey) ? null : $"task.{intentKey.Replace(':', '.')}",
                null,
                $"completed.{observation.Context.ApplicationName}.{observation.Context.WindowTitle}",
                string.Join(',', evidence),
                "task_completed");
            var completion = new DeveloperCompletionRecord(
                1,
                Guid.NewGuid().ToString("D"),
                DateTimeOffset.UtcNow,
                goal,
                message.EffectiveGoal ?? goal,
                observation.Context,
                observation.SnapshotHash,
                evidence,
                labels,
                true,
                feedback.Id,
                "Developer explicitly marked the task complete.");
            await _correctionStore.SaveCompletionAsync(completion);
            message.MarkEvaluated("completed");
            _overlay.Clear();
            if (_session is not null)
                _session = TaskSessionStateMachine.Completed(_session, "개발자가 완료 상태로 검증했습니다.");
            Messages.Add(CreateAssistantMessage(
                "assistant",
                "여기서 작업이 끝난 것으로 저장했습니다. 같은 의도와 완료 화면에서는 더 이상 다음 단계를 찾지 않습니다."));
            StatusText = "완료 상태가 human_gold로 저장됨";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            StatusText = "완료 상태 저장 오류";
        }
    }

    private void CloseCorrectionEditor()
    {
        ResetCorrectionDraft();
        _overlay.Clear();
        StatusText = "교정 취소됨";
    }

    private void BeginCorrectionDraft(ChatMessageItem answer, AnswerFeedbackRecord? feedback)
    {
        CancelRunningWork(markCancelled: false);
        _overlay.Clear();
        _correctionAnswer = answer;
        _correctionFeedback = feedback;
        CorrectionIntentText = string.Empty;
        CorrectionCommentText = string.Empty;
        CorrectionTaskId = string.Empty;
        CorrectionStateId = string.Empty;
        CorrectionTargetConcept = answer.TargetLabel ?? answer.Decision?.VisualTarget?.Label ?? string.Empty;
        CorrectionExpectedNextState = answer.Decision?.ExpectedChange ?? string.Empty;
        CorrectionExpectedEvidence = CorrectionTargetConcept;
        CorrectionOutcomeLabel = "wrong_target";
        CorrectionSelectionSummary = "정답 영역을 아직 선택하지 않았습니다.";
        _pendingCorrectionSelection = null;
        _pendingCorrectionCandidate = null;
        _pendingCorrectionCapture = null;
        IsCorrectionEditorVisible = true;
    }

    private async Task CaptureCorrectionAsync()
    {
        if (_session is null || _lastObservation is null || !CanCaptureCorrection()) return;
        var observation = _lastObservation;
        CancelRunningWork(markCancelled: false);
        _overlay.Clear();
        IsCorrectionEditorVisible = false;

        UiBounds? selection = null;
        try
        {
            CorrectionSelectionStarted?.Invoke();
            selection = await _correctionSelection.SelectRegionAsync();
        }
        finally
        {
            CorrectionSelectionCompleted?.Invoke();
        }

        if (selection is null)
        {
            IsCorrectionEditorVisible = true;
            StatusText = "정답 영역 선택 취소됨";
            return;
        }

        try
        {
            StatusText = "선택 영역 미리보기 준비 중";
            var capture = await _screenCapture.CaptureAsync(CancellationToken.None);
            var matchedCandidate = DeveloperCorrectionMatcher.FindSelectedCandidate(
                selection,
                observation.Candidates);
            var targetLabel = matchedCandidate?.Label
                ?? matchedCandidate?.Description
                ?? "사용자가 선택한 정답 영역";
            _pendingCorrectionSelection = selection;
            _pendingCorrectionCandidate = matchedCandidate;
            _pendingCorrectionCapture = capture;
            CorrectionSelectionSummary = matchedCandidate is null
                ? $"선택됨: 화면 영역 {Math.Round(selection.X)}, {Math.Round(selection.Y)} · 저장 전"
                : $"선택됨: {targetLabel} · 저장 전";
            var displayBounds = matchedCandidate?.Bounds ?? selection;
            _overlay.ShowTarget(displayBounds, "교정 미리보기입니다. 확인 후 저장을 눌러 주세요.");
            TargetHighlighted?.Invoke(displayBounds);
            IsCorrectionEditorVisible = true;
            StatusText = "선택 영역 확인 중 · 아직 저장되지 않음";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            ErrorMessage = "선택한 정답 영역을 저장하지 못했어요.";
            StatusText = "교정 저장 오류";
            IsCorrectionEditorVisible = true;
        }
    }

    private async Task SaveCorrectionAsync()
    {
        if (_session is null || _correctionAnswer is null || !CanSaveCorrection()) return;
        try
        {
            var refined = DeveloperCommentRefiner.Refine(CorrectionCommentText);
            var correctedIntent = string.IsNullOrWhiteSpace(CorrectionIntentText)
                ? null
                : CorrectionIntentText.Trim();
            VisualTarget? normalizedTarget = null;
            CorrectionTargetSignature? signature = null;
            if (_pendingCorrectionSelection is not null && _pendingCorrectionCapture is not null)
            {
                var label = _pendingCorrectionCandidate?.Label
                    ?? _pendingCorrectionCandidate?.Description
                    ?? "사용자가 선택한 정답 영역";
                normalizedTarget = DeveloperCorrectionMatcher.NormalizeSelection(
                    _pendingCorrectionCapture.Bounds,
                    _pendingCorrectionSelection,
                    label);
                if (_pendingCorrectionCandidate is not null)
                    signature = DeveloperCorrectionMatcher.CreateSignature(_pendingCorrectionCandidate);
            }

            var record = CreateCorrectionRecord(
                correctedIntent,
                _pendingCorrectionSelection,
                normalizedTarget,
                signature,
                refined);
            var saved = await _correctionStore.SaveAsync(
                record,
                SaveCorrectionScreenshot ? _pendingCorrectionCapture?.DataUrl : null);
            var selectedLabel = _pendingCorrectionCandidate?.Label ?? "정답 정보";
            var selectedCandidate = _pendingCorrectionCandidate;
            var selectedBounds = _pendingCorrectionCandidate?.Bounds ?? _pendingCorrectionSelection;
            var normalizedVisualTarget = normalizedTarget;
            var correctionDecision = selectedCandidate is not null
                ? new GuideDecision(
                    GuideStatuses.InProgress,
                    GuideActions.Highlight,
                    $"수정한 내용을 바로 적용했어요. '{selectedLabel}'을(를) 눌러보세요.",
                    1,
                    selectedCandidate.Id,
                    $"'{selectedLabel}'을(를) 누른 다음 화면을 확인합니다.")
                : normalizedVisualTarget is not null
                    ? new GuideDecision(
                        GuideStatuses.InProgress,
                        GuideActions.HighlightVisual,
                        "수정한 위치를 바로 표시했어요. 표시된 곳을 눌러보세요.",
                        1,
                        VisualTarget: normalizedVisualTarget)
                    : null;
            ChatMessageItem? appliedMessage = null;
            if (correctionDecision is not null && selectedBounds is not null)
            {
                var liveBounds = selectedBounds;
                var canApplyNow = selectedCandidate is null;
                if (selectedCandidate is not null)
                    canApplyNow = _lastObservation?.Registry.TryResolveState(
                            selectedCandidate.Id, out liveBounds, out var isOffscreen) == true
                        && !isOffscreen;
                if (canApplyNow && WindowsScreenGeometry.ContainsCenter(liveBounds))
                {
                    appliedMessage = CreateAssistantMessage("assistant", correctionDecision.Message);
                    AttachTrainingContext(
                        appliedMessage,
                        correctionDecision,
                        selectedLabel,
                        liveBounds,
                        selectedCandidate);
                    _lastDecision = correctionDecision;
                    _lastHighlightedCandidate = selectedCandidate;
                    _lastHighlightedBounds = liveBounds;
                    RememberApprovedReplay(appliedMessage);
                    _overlay.ShowTarget(liveBounds, correctionDecision.Message);
                    TargetHighlighted?.Invoke(liveBounds);
                }
            }
            ResetCorrectionDraft(clearOverlay: false);
            Messages.Add(appliedMessage ?? CreateAssistantMessage(
                "assistant",
                $"교정 내용을 확인하고 저장했어요. 다음 같은 질문에서는 '{selectedLabel}' 기준을 우선 적용합니다."));
            StatusText = appliedMessage is null
                ? $"교정 저장됨 · {saved.Id[..8]}"
                : $"교정 저장·즉시 적용됨 · {saved.Id[..8]}";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Write(exception);
            ErrorMessage = "교정 데이터를 저장하지 못했어요.";
            StatusText = "교정 저장 오류";
        }
    }

    private DeveloperCorrectionRecord CreateCorrectionRecord(
        string? correctedIntent,
        UiBounds? selectedBounds,
        VisualTarget? normalizedTarget,
        CorrectionTargetSignature? signature,
        RefinedDeveloperComment refinedComment)
    {
        var context = _correctionAnswer?.Context ?? _lastObservation?.Context
            ?? new ApplicationContext(Platforms.Windows, "unknown", Locale: System.Globalization.CultureInfo.CurrentUICulture.Name);
        return new DeveloperCorrectionRecord(
            1,
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow,
            _correctionAnswer?.OriginalGoal
                ?? (string.IsNullOrWhiteSpace(_submittedGoal) ? _session!.OriginalUserMessage : _submittedGoal),
            _correctionAnswer?.EffectiveGoal ?? _session!.OriginalUserMessage,
            correctedIntent,
            context,
            _correctionAnswer?.SnapshotHash ?? _lastObservation?.SnapshotHash,
            _correctionAnswer?.Decision?.Action ?? _lastDecision?.Action,
            _correctionAnswer?.Decision?.TargetId ?? _lastDecision?.TargetId,
            _correctionAnswer?.TargetLabel
                ?? _lastHighlightedCandidate?.Label
                ?? _lastDecision?.VisualTarget?.Label,
            _correctionAnswer?.TargetBounds ?? _lastHighlightedBounds,
            selectedBounds,
            normalizedTarget,
            signature,
            null,
            true,
            _correctionFeedback?.Id,
            string.IsNullOrWhiteSpace(refinedComment.Raw) ? null : refinedComment.Raw,
            string.IsNullOrWhiteSpace(refinedComment.Normalized) ? null : refinedComment.Normalized,
            refinedComment.IssueTags.Count == 0 ? null : refinedComment.IssueTags,
            DeveloperLabeling.CreateCorrectionLabels(
                _correctionAnswer?.OriginalGoal ?? _session!.OriginalUserMessage,
                context,
                string.IsNullOrWhiteSpace(CorrectionTargetConcept)
                    ? (_pendingCorrectionCandidate?.Label ?? _correctionAnswer?.TargetLabel ?? "unknown target")
                    : CorrectionTargetConcept,
                CorrectionTaskId,
                CorrectionStateId,
                CorrectionExpectedNextState,
                CorrectionExpectedEvidence,
                CorrectionOutcomeLabel));
    }

    private AnswerFeedbackRecord CreateFeedbackRecord(ChatMessageItem message, string rating) => new(
        1,
        Guid.NewGuid().ToString("D"),
        DateTimeOffset.UtcNow,
        rating,
        message.Id,
        message.Text,
        message.OriginalGoal,
        message.EffectiveGoal,
        message.Context,
        message.SnapshotHash,
        message.Decision?.Action,
        message.Decision?.TargetId,
        message.TargetLabel ?? message.Decision?.VisualTarget?.Label,
        message.TargetBounds);

    private void ResetCorrectionDraft(bool clearOverlay = true)
    {
        IsCorrectionEditorVisible = false;
        CorrectionIntentText = string.Empty;
        CorrectionCommentText = string.Empty;
        CorrectionTaskId = string.Empty;
        CorrectionStateId = string.Empty;
        CorrectionTargetConcept = string.Empty;
        CorrectionExpectedNextState = string.Empty;
        CorrectionExpectedEvidence = string.Empty;
        CorrectionOutcomeLabel = "wrong_target";
        CorrectionSelectionSummary = "정답 영역을 아직 선택하지 않았습니다.";
        _correctionAnswer = null;
        _correctionFeedback = null;
        _pendingCorrectionSelection = null;
        _pendingCorrectionCandidate = null;
        _pendingCorrectionCapture = null;
        if (clearOverlay) _overlay.Clear();
    }

    private void CancelCurrentTask()
    {
        CancelRunningWork(markCancelled: true);
        ClearClarificationChoices();
        StatusText = "작업 취소됨";
        Messages.Add(CreateAssistantMessage("assistant", "안내를 취소했어요. 새로운 목표를 입력해 주세요."));
    }

    private void TogglePause()
    {
        IsPaused = !IsPaused;
        if (IsPaused)
        {
            CancelRunningWork(markCancelled: false);
            ClearClarificationChoices();
            StatusText = "일시 정지됨";
            Messages.Add(CreateAssistantMessage("assistant", "화면 관찰을 일시 정지했어요."));
        }
        else
        {
            StatusText = "준비됨";
            Messages.Add(CreateAssistantMessage("assistant", "다시 시작할 목표를 입력해 주세요."));
        }
        RaiseCommandStates();
    }

    private void ToggleDeveloperMode()
    {
        IsDeveloperMode = !IsDeveloperMode;
        StatusText = IsDeveloperMode
            ? "개발자 모드 켜짐 · O/X 평가와 GPT 피드백 라벨링 사용 가능"
            : "일반 모드";
        RaiseCommandStates();
    }

    private void RememberApprovedReplay(ChatMessageItem message)
    {
        var approvedDecision = message.Decision;
        if (approvedDecision is null
            || approvedDecision.Action != GuideActions.Highlight
            && approvedDecision.Action != GuideActions.HighlightVisual
            || string.IsNullOrWhiteSpace(message.OriginalGoal)) return;
        if (approvedDecision.Action == GuideActions.Highlight
            && string.IsNullOrWhiteSpace(approvedDecision.TargetId)) return;
        _approvedReplays[NormalizeGoal(message.OriginalGoal)] = new ApprovedReplay(
            message.OriginalGoal,
            approvedDecision.TargetId,
            message.TargetLabel ?? approvedDecision.VisualTarget?.Label ?? approvedDecision.TargetId ?? "검증 대상",
            message.Text,
            message.SnapshotHash,
            message.TargetBounds,
            true);
    }

    private void RememberRecentReplay(ChatMessageItem message)
    {
        var decision = message.Decision;
        if (decision?.Action != GuideActions.Highlight
            || string.IsNullOrWhiteSpace(decision.TargetId)
            || string.IsNullOrWhiteSpace(message.OriginalGoal)
            || message.TargetBounds is null) return;
        _recentReplays[NormalizeGoal(message.OriginalGoal)] = new ApprovedReplay(
            message.OriginalGoal,
            decision.TargetId,
            message.TargetLabel ?? decision.TargetId,
            message.Text,
            message.SnapshotHash,
            message.TargetBounds,
            false);
        while (_recentReplays.Count > 32)
            _recentReplays.Remove(_recentReplays.Keys.First());
    }

    private ApprovedReplay? TryResolveImmediateReplay(
        string goal,
        WindowsObservation? observation)
    {
        _approvedReplays.TryGetValue(NormalizeGoal(goal), out var replay);
        replay ??= _approvedReplays.Values.LastOrDefault(item =>
            DeveloperIntentMatcher.IsSameIntent(goal, item.Goal));
        if (replay is null)
            _recentReplays.TryGetValue(NormalizeGoal(goal), out replay);
        if (observation is null || replay is null) return null;
        var snapshotMatches = string.Equals(
            replay.SnapshotHash,
            observation.SnapshotHash,
            StringComparison.Ordinal);
        // Pixel-only feedback is tied to the exact screenshot. Candidate-backed feedback
        // is safer and more durable: verify the live UIA element instead of rejecting it
        // whenever ads, clocks, cart counts, or other unrelated pixels change the hash.
        if (replay.TargetId is null)
            return replay.TargetBounds is not null
                && DeveloperReplayPolicy.CanReuseImmediately(replay.TargetId, snapshotMatches, false)
                    ? replay
                    : null;
        var liveTargetResolved = observation.Registry.TryResolveState(
                replay.TargetId,
                out var liveBounds,
                out var isOffscreen)
            && !isOffscreen
            && observation.Candidates.Any(candidate =>
                string.Equals(candidate.Id, replay.TargetId, StringComparison.Ordinal)
                && candidate.Visible && candidate.Enabled && candidate.Clickable);
        if (!DeveloperReplayPolicy.CanReuseImmediately(
                replay.TargetId, snapshotMatches, liveTargetResolved, replay.DeveloperVerified))
            return null;
        DesktopDiagnostics.WriteEvent(
            "verified_replay_hit",
            ("targetId", replay.TargetId),
            ("source", replay.DeveloperVerified ? "developer" : "exact_screen_cache"),
            ("snapshotChanged", !snapshotMatches));
        return replay with { TargetBounds = liveBounds };
    }

    private void RememberRecentSafeReply(ChatMessageItem message)
    {
        var decision = message.Decision;
        if (decision is null
            || string.IsNullOrWhiteSpace(message.OriginalGoal)
            || string.IsNullOrWhiteSpace(message.SnapshotHash)
            || !DeveloperReplayPolicy.CanReuseSafeReply(
                decision.Action, decision.Status, snapshotMatches: true)) return;
        _recentSafeReplies[NormalizeGoal(message.OriginalGoal)] = new RecentSafeReply(
            message.OriginalGoal,
            message.SnapshotHash,
            decision);
        while (_recentSafeReplies.Count > 32)
            _recentSafeReplies.Remove(_recentSafeReplies.Keys.First());
    }

    private GuideDecision? TryResolveRecentSafeReply(
        string goal,
        WindowsObservation? observation)
    {
        if (observation is null
            || !_recentSafeReplies.TryGetValue(NormalizeGoal(goal), out var reply)) return null;
        var snapshotMatches = string.Equals(
            reply.SnapshotHash,
            observation.SnapshotHash,
            StringComparison.Ordinal);
        return DeveloperReplayPolicy.CanReuseSafeReply(
            reply.Decision.Action,
            reply.Decision.Status,
            snapshotMatches)
            ? reply.Decision
            : null;
    }

    private static string NormalizeGoal(string value) => string.Concat(
        value.Trim().ToLowerInvariant().Where(character => !char.IsWhiteSpace(character)));

    private void CancelRunningWork(bool markCancelled)
    {
        _taskCancellation?.Cancel();
        _taskCancellation?.Dispose();
        _taskCancellation = null;
        _overlay.Clear();
        _forcedDecision = null;
        if (markCancelled && _session is not null)
            _session = TaskSessionStateMachine.Cancelled(_session);
    }

    private void RaiseCommandStates()
    {
        (SubmitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RecoveryCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CaptureCorrectionCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SaveCorrectionCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (MarkAnswerCorrectCommand as AsyncParameterRelayCommand)?.RaiseCanExecuteChanged();
        (MarkAnswerIncorrectCommand as AsyncParameterRelayCommand)?.RaiseCanExecuteChanged();
        (MarkAnswerCompletedCommand as AsyncParameterRelayCommand)?.RaiseCanExecuteChanged();
        (SelectClarificationCommand as AsyncParameterRelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ClearClarificationChoices()
    {
        ClarificationChoices.Clear();
        _clarificationObservation = null;
    }

    private void CompletePendingOrAddError(ChatMessageItem? pendingMessage, string message)
    {
        if (pendingMessage is null)
        {
            Messages.Add(CreateAssistantMessage("assistant", message));
            return;
        }
        pendingMessage.Text = message;
        AttachTrainingContext(pendingMessage, _lastDecision);
        pendingMessage.IsPending = false;
    }

    private ChatMessageItem CreateAssistantMessage(string role, string text, bool isPending = false)
    {
        var message = new ChatMessageItem(role, text, isPending);
        AttachTrainingContext(message, _lastDecision);
        return message;
    }

    private void AttachTrainingContext(
        ChatMessageItem message,
        GuideDecision? decision,
        string? targetLabel = null,
        UiBounds? targetBounds = null,
        UiCandidate? targetCandidate = null)
    {
        message.AttachTrainingContext(
            string.IsNullOrWhiteSpace(_submittedGoal) ? _session?.OriginalUserMessage : _submittedGoal,
            _session?.OriginalUserMessage,
            _lastObservation?.Context,
            _lastObservation?.SnapshotHash,
            decision,
            targetLabel,
            targetBounds,
            targetCandidate is null ? null : DeveloperCorrectionMatcher.CreateSignature(targetCandidate));
    }

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
