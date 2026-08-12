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
        UiBounds? TargetBounds);

    private readonly IWindowsUiObserver _observer;
    private readonly IWindowsChangeMonitor _changeMonitor;
    private readonly IWindowsScreenCaptureService _screenCapture;
    private readonly IGuideApiClient _apiClient;
    private readonly IHighlightOverlay _overlay;
    private readonly ICorrectionSelectionService _correctionSelection;
    private readonly IDeveloperCorrectionStore _correctionStore;
    private readonly Action _exit;
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
    private string _correctionTaskId = string.Empty;
    private string _correctionStateId = string.Empty;
    private string _correctionTargetConcept = string.Empty;
    private string _correctionExpectedNextState = string.Empty;
    private string _correctionExpectedEvidence = string.Empty;
    private string _correctionOutcomeLabel = "wrong_target";
    private readonly Dictionary<string, ApprovedReplay> _approvedReplays = new(StringComparer.Ordinal);
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
        Action exit)
    {
        _observer = observer;
        _changeMonitor = changeMonitor;
        _screenCapture = screenCapture;
        _apiClient = apiClient;
        _overlay = overlay;
        _correctionSelection = correctionSelection;
        _correctionStore = correctionStore;
        _exit = exit;
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
        ExitCommand = new RelayCommand(_exit);
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
    public ICommand ExitCommand { get; }
    public ObservableCollection<ChatMessageItem> Messages { get; } = [];
    public ObservableCollection<ClarificationChoiceItem> ClarificationChoices { get; } = [];

    public string GoalText
    {
        get => _goalText;
        set { if (Set(ref _goalText, value)) RaiseCommandStates(); }
    }
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
            if (!value) ResetCorrectionDraft();
        }
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

    private bool CanSubmit() => !IsPaused && !IsLoading && !string.IsNullOrWhiteSpace(GoalText);

    private async Task SubmitAsync()
    {
        var query = GoalText.Trim();
        if (string.IsNullOrWhiteSpace(query)) return;
        var replayObservation = _lastObservation;
        var immediateReplay = TryResolveImmediateReplay(query, replayObservation);
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
        if (WindowsGoalClarificationResolver.TryCreate(query, out var clarification))
        {
            _session = TaskSessionStateMachine.Create(query);
            Messages.Add(CreateAssistantMessage("assistant", clarification.Message));
            foreach (var choice in clarification.Choices)
                ClarificationChoices.Add(new ClarificationChoiceItem(choice.Label, choice.ResolvedGoal));
            StatusText = "선택이 필요해요";
            RaiseCommandStates();
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
                var automaticCompletion = GoalCompletionResolver.TryResolve(
                        _session.OriginalUserMessage,
                        currentObservation.Context,
                        currentObservation.Candidates,
                        out var automaticCompletionMessage);
                if (storedCompletion || automaticCompletion)
                {
                    var completionMessage = string.IsNullOrWhiteSpace(automaticCompletionMessage)
                        ? "완료됐어요. 개발자가 검증한 최종 상태에 도착했습니다."
                        : automaticCompletionMessage;
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
                var prioritizedCandidates = WindowsCandidatePrioritizer.Prioritize(
                    _session.OriginalUserMessage,
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
                    request = request with
                    {
                        Screenshot = "verified-replay",
                        ScreenshotBounds = new UiBounds(
                            SystemParameters.VirtualScreenLeft,
                            SystemParameters.VirtualScreenTop,
                            SystemParameters.VirtualScreenWidth,
                            SystemParameters.VirtualScreenHeight),
                    };
                    decision = new GuideDecision(
                        GuideStatuses.InProgress,
                        GuideActions.HighlightVisual,
                        $"저장된 검증 정답에 따라 '{correctedVisualTarget.Label}' 위치를 표시할게요.",
                        1,
                        VisualTarget: correctedVisualTarget);
                    StatusText = "검증된 화면 정답 적용";
                }
                else if (WindowsFastPathResolver.TryResolve(request, out var fastDecision))
                {
                    StatusText = "Windows 빠른 안내";
                    decision = ContractValidator.ValidateDecision(fastDecision, request);
                }
                else
                {
                    GuideDecision? deferredDecision = null;
                    if (currentObservation.ForegroundScanDeferred)
                    {
                        StatusText = "열린 Windows 화면 확인 중";
                        await Task.Delay(250, cancellationToken);
                        currentObservation = await _observer.ObserveAsync(
                            _session.OriginalUserMessage,
                            cancellationToken);
                        CurrentApplication = string.IsNullOrWhiteSpace(currentObservation.Context.WindowTitle)
                            ? currentObservation.Context.ApplicationName
                            : $"{currentObservation.Context.ApplicationName} — {currentObservation.Context.WindowTitle}";
                        prioritizedCandidates = WindowsCandidatePrioritizer.Prioritize(
                            _session.OriginalUserMessage,
                            currentObservation.Candidates);
                        request = new GuideRequest(_session, currentObservation.Context, prioritizedCandidates);
                        if (WindowsFastPathResolver.TryResolve(request, out fastDecision))
                        {
                            StatusText = "Windows 빠른 안내";
                            deferredDecision = ContractValidator.ValidateDecision(fastDecision, request);
                        }
                    }
                    if (deferredDecision is not null)
                    {
                        decision = deferredDecision;
                    }
                    else if (WindowsFastPathResolver.IsKnownSystemGoal(_session.OriginalUserMessage))
                    {
                        GuideDecision? localDecision = null;
                        for (var retry = 0; retry < 4 && localDecision is null; retry++)
                        {
                            StatusText = "Windows 메뉴가 열리기를 기다리는 중";
                            await Task.Delay(300, cancellationToken);
                            currentObservation = await _observer.ObserveAsync(
                                _session.OriginalUserMessage,
                                cancellationToken);
                            CurrentApplication = string.IsNullOrWhiteSpace(currentObservation.Context.WindowTitle)
                                ? currentObservation.Context.ApplicationName
                                : $"{currentObservation.Context.ApplicationName} — {currentObservation.Context.WindowTitle}";
                            prioritizedCandidates = WindowsCandidatePrioritizer.Prioritize(
                                _session.OriginalUserMessage,
                                currentObservation.Candidates);
                            request = new GuideRequest(_session, currentObservation.Context, prioritizedCandidates);
                            if (WindowsFastPathResolver.TryResolve(request, out fastDecision))
                                localDecision = ContractValidator.ValidateDecision(fastDecision, request);
                        }

                        if (localDecision is not null)
                        {
                            decision = localDecision;
                        }
                        else
                        {
                            (decision, request) = await RequestVisionDecisionAsync(request, cancellationToken);
                        }
                    }
                    else
                    {
                        StatusText = $"후보 {prioritizedCandidates.Count}개 AI 분석 중";
                        decision = await _apiClient.DecideNextActionAsync(request, cancellationToken);
                        if (decision.Action == GuideActions.RequestVision)
                            (decision, request) = await RequestVisionDecisionAsync(request, cancellationToken);
                    }
                }
                _lastObservation = currentObservation;
                _lastDecision = decision;
                pendingMessage.Text = decision.Message;
                AttachTrainingContext(pendingMessage, decision);
                pendingMessage.IsPending = false;
                var answerMessage = pendingMessage;
                pendingMessage = null;

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
                        bounds = MapVisualTarget(request.ScreenshotBounds, decision.VisualTarget);
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
                            throw new ContractValidationException("The selected Windows element is stale.");
                        selectedLabel = selectedCandidate?.Label ?? decision.TargetId!;
                    }
                    _lastHighlightedCandidate = selectedCandidate;
                    _lastHighlightedBounds = bounds;
                    AttachTrainingContext(answerMessage, decision, selectedLabel, bounds, selectedCandidate);
                    _session = TaskSessionStateMachine.GuidanceReady(_session, decision.Message, decision.ExpectedChange);
                    if (isOffscreen)
                    {
                        _overlay.ShowScrollHint(bounds, decision.Message);
                        StatusText = bounds.Y >= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight
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
                    if (selectedCandidate is not null
                        && WindowsSystemOutcomeResolver.TryResolve(
                            _session.OriginalUserMessage,
                            selectedCandidate,
                            out var completedMessage))
                    {
                        Messages.Add(CreateAssistantMessage("assistant", completedMessage));
                        _session = TaskSessionStateMachine.Completed(_session, completedMessage);
                        StatusText = "작업 완료";
                        return;
                    }
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
        catch (ContractValidationException)
        {
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
            // Reaching this path means UI Automation did not find the requested
            // control. Do not let a generic candidate such as Search distract the
            // vision model from a more direct control that is visible in pixels.
            Candidates = [],
            Screenshot = capture.DataUrl,
            ScreenshotBounds = capture.Bounds,
        };
        var decision = await _apiClient.DecideNextActionAsync(visionRequest, cancellationToken);
        return (decision, visionRequest);
    }

    private static UiBounds MapVisualTarget(UiBounds screenshotBounds, VisualTarget target)
    {
        var width = Math.Min(screenshotBounds.Width, Math.Max(8, target.Width * screenshotBounds.Width));
        var height = Math.Min(screenshotBounds.Height, Math.Max(8, target.Height * screenshotBounds.Height));
        var x = Math.Clamp(
            screenshotBounds.X + target.X * screenshotBounds.Width,
            screenshotBounds.X,
            screenshotBounds.X + screenshotBounds.Width - width);
        var y = Math.Clamp(
            screenshotBounds.Y + target.Y * screenshotBounds.Height,
            screenshotBounds.Y,
            screenshotBounds.Y + screenshotBounds.Height - height);
        return new UiBounds(x, y, width, height);
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
            message.MarkEvaluated("correct");
            StatusText = message.TargetSignature is null
                ? "정답으로 영구 저장됨"
                : "정답으로 영구 저장됨 · 다음 동일 질문은 AI 없이 즉시 안내";
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
            var evidence = !string.IsNullOrWhiteSpace(observation.Context.WindowTitle)
                ? new[] { observation.Context.WindowTitle.Trim() }
                : !string.IsNullOrWhiteSpace(observation.Context.Url)
                    ? new[] { observation.Context.Url.Trim() }
                    : new[] { observation.Context.ApplicationName.Trim() };
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
            ResetCorrectionDraft(clearOverlay: false);
            Messages.Add(CreateAssistantMessage(
                "assistant",
                $"교정 내용을 확인하고 저장했어요. 다음 같은 질문에서는 '{selectedLabel}' 기준을 우선 적용합니다."));
            StatusText = $"교정 저장됨 · {saved.Id[..8]}";
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
            ? "개발자 모드 켜짐 · O/X 평가와 Brain v2 라벨링 사용 가능"
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
            message.TargetBounds);
    }

    private ApprovedReplay? TryResolveImmediateReplay(
        string goal,
        WindowsObservation? observation)
    {
        _approvedReplays.TryGetValue(NormalizeGoal(goal), out var replay);
        replay ??= _approvedReplays.Values.LastOrDefault(item =>
            DeveloperIntentMatcher.IsSameIntent(goal, item.Goal));
        if (observation is null
            || replay is null
            || !string.Equals(replay.SnapshotHash, observation.SnapshotHash, StringComparison.Ordinal)) return null;
        if (replay.TargetId is null) return replay.TargetBounds is null ? null : replay;
        if (!observation.Registry.TryResolveState(replay.TargetId, out _, out var isOffscreen)
            || isOffscreen
            || !observation.Candidates.Any(candidate =>
                string.Equals(candidate.Id, replay.TargetId, StringComparison.Ordinal)
                && candidate.Visible && candidate.Enabled && candidate.Clickable)) return null;
        return replay;
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
